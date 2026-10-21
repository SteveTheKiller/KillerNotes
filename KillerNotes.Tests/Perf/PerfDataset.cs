using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests.Perf
{
    /// <summary>
    /// Opt-in benchmark tests: skipped unless KN_PERF=1, which tests\Performance\Run.ps1 sets.
    /// Normal test runs never pay for them.
    /// </summary>
    public sealed class PerfFactAttribute : FactAttribute
    {
        public PerfFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("KN_PERF") != "1")
                Skip = "Benchmark: run tests\\Performance\\Run.ps1";
        }
    }

    /// <summary>
    /// A synthetic notebook for the benchmarks, the same for a given size every time (fixed seed).
    /// About 70% short markdown notes, 20% rich notes with headings and a table, 10% rich notes with
    /// a sketch and a recording, each linking two others; plus three stress notes: a 5,000-line
    /// note, a 50 x 10 table and a note with 20 pictures. Written with plain SQL for speed, then
    /// opened through NoteStore so its own start-up work (schema, folds) is part of what is measured.
    /// </summary>
    internal static class PerfDataset
    {
        internal const string LongNote = "Stress: 5000 lines", TableNote = "Stress: 50x10 table", PictureNote = "Stress: 20 pictures";
        internal const string RareWord = "zanzibarquokka";

        private static readonly string[] Words =
            ("network switch firewall vlan dhcp printer backup restore server laptop ticket client " +
             "invoice password reset outlook teams onedrive sharepoint license update patch driver " +
             "lorem ipsum dolor amet café zürich straße ελλάδα привет façade naïve résumé").Split(' ');

        /// <summary>Builds the notebook in dir/test.db and leaves it closed.</summary>
        internal static void Build(string dir, int notes)
        {
            Directory.CreateDirectory(dir);
            NoteStore.DbDirOverride = dir;
            NoteStore.DemoDbFile = "test.db";
            NoteStore.Open();
            string db = NoteStore.DbPath;
            NoteStore.Close();

            var rng = new Random(20261021 + notes);
            byte[] richTemplate = null!, mediaTemplate = null!, longNote = null!, tableNote = null!, pictureNote = null!;
            var sketch = new Attachment(AttachmentKind.Sketch, Enumerable.Range(0, 2000).Select(i => (byte)i).ToArray());
            var audio = new Attachment(AttachmentKind.Recording, Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7)).ToArray(), 12_000);
            Sta.Run(() =>
            {
                richTemplate = Package(RichDoc(rng));
                mediaTemplate = Package(MediaDoc(sketch, audio));
                longNote = Package(LongDoc(rng));
                tableNote = Package(TableDoc(50, 10));
                pictureNote = Package(PictureDoc(20));
            });

            using var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ConnectionString);
            raw.Open();
            using var tx = raw.BeginTransaction();
            Exec(raw, tx, "INSERT OR IGNORE INTO attachments(hash, kind, payload, duration) VALUES ($h, $k, $p, $d)",
                ("$h", sketch.Hash), ("$k", 1), ("$p", sketch.Payload), ("$d", 0));
            Exec(raw, tx, "INSERT OR IGNORE INTO attachments(hash, kind, payload, duration) VALUES ($h, $k, $p, $d)",
                ("$h", audio.Hash), ("$k", 2), ("$p", audio.Payload), ("$d", audio.DurationMs));

            var stamp = new DateTime(2026, 1, 1);
            for (int i = 0; i < notes; i++)
            {
                string title = "Note " + i;
                string body = Sentence(rng, 40 + rng.Next(80)) + $" [[Note {rng.Next(notes)}]] [[Note {rng.Next(notes)}]]";
                if (i == notes / 2) body += " " + RareWord;
                int kind = rng.Next(100);
                object content; int format;
                if (kind < 70) { content = MarkdownBlob.Encode(body); format = 1; }
                else { content = kind < 90 ? richTemplate : mediaTemplate; format = 0; }
                long id = Insert(raw, tx, title, "Group " + (i % 25), i % 7 == 0 ? "Red, Blue" : "", stamp.AddMinutes(i), content, body, format, i);
                foreach (string target in WikiLinks.Parse(body))
                    Exec(raw, tx, "INSERT OR IGNORE INTO note_links(src, target) VALUES ($s, $t)", ("$s", id), ("$t", target));
                if (kind >= 90)
                    foreach (var a in new[] { sketch, audio })
                        Exec(raw, tx, "INSERT OR IGNORE INTO attachment_refs(owner, hash) VALUES ($o, $h)", ("$o", "n:" + id), ("$h", a.Hash));
            }
            Insert(raw, tx, LongNote, "", "", stamp, longNote, "long", 0, notes);
            Insert(raw, tx, TableNote, "", "", stamp, tableNote, "table", 0, notes + 1);
            Insert(raw, tx, PictureNote, "", "", stamp, pictureNote, "pictures", 0, notes + 2);
            tx.Commit();
        }

        private static long Insert(SqliteConnection raw, SqliteTransaction tx, string title, string group, string tags,
            DateTime when, object content, string plain, int format, int order)
        {
            using var cmd = raw.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO notes(title, notebook, tags, created, modified, content, plain, format, sort_order) " +
                              "VALUES ($t, $n, $g, $c, $c, $b, $p, $f, $o); SELECT last_insert_rowid()";
            cmd.Parameters.AddWithValue("$t", title);
            cmd.Parameters.AddWithValue("$n", group);
            cmd.Parameters.AddWithValue("$g", tags);
            cmd.Parameters.AddWithValue("$c", when.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$b", content);
            cmd.Parameters.AddWithValue("$p", plain);
            cmd.Parameters.AddWithValue("$f", format);
            cmd.Parameters.AddWithValue("$o", order);
            return (long)cmd.ExecuteScalar()!;
        }

        private static void Exec(SqliteConnection raw, SqliteTransaction tx, string sql, params (string Name, object Value)[] args)
        {
            using var cmd = raw.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
            cmd.ExecuteNonQuery();
        }

        private static string Sentence(Random rng, int words)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < words; i++) sb.Append(Words[rng.Next(Words.Length)]).Append(i % 12 == 11 ? ". " : " ");
            return sb.ToString().Trim();
        }

        internal static byte[] Package(FlowDocument doc)
        {
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            return ms.ToArray();
        }

        private static FlowDocument RichDoc(Random rng)
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Bold(new Run("Heading"))) { FontSize = 20 });
            for (int i = 0; i < 8; i++) doc.Blocks.Add(new Paragraph(new Run(Sentence(rng, 30))));
            doc.Blocks.Add(Table(5, 4));
            return doc;
        }

        private static FlowDocument MediaDoc(Attachment sketch, Attachment audio)
        {
            var p = new Paragraph(new Run("Site visit with a sketch and a call recording "));
            p.Inlines.Add(new InlineUIContainer(Picture(Attachments.Marker(AttachmentKind.Sketch, sketch.Hash), 400, 250)));
            p.Inlines.Add(new InlineUIContainer(Picture(Attachments.Marker(AttachmentKind.Recording, audio.Hash), 220, 28)));
            return new FlowDocument(p);
        }

        private static FlowDocument LongDoc(Random rng)
        {
            var doc = new FlowDocument();
            for (int i = 0; i < 5000; i++) doc.Blocks.Add(new Paragraph(new Run(i + ": " + Sentence(rng, 12))));
            return doc;
        }

        private static FlowDocument TableDoc(int rows, int cols) => new(Table(rows, cols));

        private static FlowDocument PictureDoc(int count)
        {
            var p = new Paragraph();
            for (int i = 0; i < count; i++) p.Inlines.Add(new InlineUIContainer(Picture(null, 640, 400)));
            return new FlowDocument(p);
        }

        private static Table Table(int rows, int cols)
        {
            var table = new Table();
            var group = new TableRowGroup();
            for (int r = 0; r < rows; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < cols; c++) row.Cells.Add(new TableCell(new Paragraph(new Run($"r{r}c{c}"))));
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);
            return table;
        }

        private static Image Picture(string? marker, int w, int h)
        {
            var pixels = new byte[w * h * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i * 13); pixels[i + 1] = (byte)(i >> 3); pixels[i + 2] = 90; pixels[i + 3] = 255; }
            return new Image { Source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4), ToolTip = marker };
        }
    }
}

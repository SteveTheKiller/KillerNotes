using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Controls;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests.Perf
{
    /// <summary>
    /// KN-P07 performance budget: times the everyday operations on synthetic notebooks of 1,000,
    /// 10,000 and 50,000 notes and writes one JSON file per size to KN_PERF_OUT. Run through
    /// tests\Performance\Run.ps1, which also keeps the scratch folder pruned. Everything runs on this
    /// machine's own copy of the code; nothing here ships in the app.
    ///
    /// Typing is measured on a RichTextBox in an off-screen window with the long note loaded: the
    /// cost of inserting a character and laying the note out again. The app's own per-keystroke
    /// handlers (syntax colors, find refresh) are not included, so it is a floor, not the full cost.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class PerfBenchmarks
    {
        [PerfFact] public void Notes1k() => Run(1_000);
        [PerfFact] public void Notes10k() => Run(10_000);
        [PerfFact] public void Notes50k() => Run(50_000);

        private sealed class Result
        {
            public string Name = "";
            public List<double> Ms = new();
            public long ManagedBytesAfter, PrivateBytesAfter;
        }

        private static void Run(int size)
        {
            string outDir = Environment.GetEnvironmentVariable("KN_PERF_OUT") ?? Path.Combine(Path.GetTempPath(), "kn-perf");
            string dataDir = Path.Combine(outDir, "data-" + size);
            var results = new List<Result>();
            var build = Stopwatch.StartNew();
            PerfDataset.Build(dataDir, size);
            build.Stop();
            try
            {
                Measure(results, "open database", 5, () => { NoteStore.Close(); NoteStore.Open(); });
                Measure(results, "sidebar list", 5, () => NoteStore.List());
                Measure(results, "search common word", 5, () => NoteStore.List("lorem"));
                Measure(results, "search rare word", 5, () => NoteStore.List(PerfDataset.RareWord));
                Measure(results, "search two words", 5, () => NoteStore.List("café backup"));

                // What every note open and save refreshes: its links out, links in and mentions.
                var middle = NoteStore.List().Single(n => n.Title == "Note " + (size / 2));
                Measure(results, "backlinks panel (one note)", 5, () =>
                {
                    NoteStore.OutgoingLinks(middle.Id);
                    NoteStore.Backlinks(middle.Title);
                    NoteStore.UnlinkedMentions(middle.Title);
                });

                long longId = NoteStore.List().Single(n => n.Title == PerfDataset.LongNote).Id;
                Sta.Run(() => UiScenarios(results, longId));

                Measure(results, "global search over every note", 1, () =>
                {
                    int hits = 0;
                    foreach (var n in NoteStore.List())
                    {
                        var doc = NoteSearch.LoadDocument(NoteStore.LoadContent(n.Id), n.Format);
                        hits += NoteSearch.CountMatches(new TextRange(doc.ContentStart, doc.ContentEnd).Text, "backup");
                    }
                }, sta: true);
                Measure(results, "open link graph", 1, () =>
                {
                    if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(GraphWindow).Assembly;
                    var g = new GraphWindow(_ => { }, _ => { }, -1, "Graph");
                    g.Close();
                }, sta: true);
                string vault = Path.Combine(outDir, "vault-" + size);
                Measure(results, "vault export", 1, () =>
                {
                    Directory.CreateDirectory(vault);
                    var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
                    typeof(MainWindow).GetMethod("ExportVaultTo", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(shell, new object[] { vault, NoteStore.List() });
                }, sta: true);
                try { Directory.Delete(vault, true); } catch { }
            }
            finally
            {
                NoteStore.Close();
                NoteStore.DbDirOverride = null;
                NoteStore.DemoDbFile = null;
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(dataDir, true); } catch { }   // only the results are kept
            }
            Write(outDir, size, build.Elapsed.TotalSeconds, results);
        }

        private static void UiScenarios(List<Result> results, long longId)
        {
            var editor = new RichTextBox();
            var window = new Window { Content = editor, Width = 900, Height = 700, Left = -20000, Top = -20000,
                                      ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show();
            try
            {
                Measure(results, "open long note", 5, () =>
                {
                    editor.Document = new FlowDocument();
                    using var ms = new MemoryStream(NoteStore.LoadContent(longId)!);
                    new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Load(ms, DataFormats.XamlPackage);
                    editor.UpdateLayout();
                });
                Measure(results, "save long note", 5, () =>
                {
                    using var ms = new MemoryStream();
                    new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Save(ms, DataFormats.XamlPackage);
                    NoteStore.SaveEditorState(longId, PerfDataset.LongNote, ms.ToArray(), "long", Array.Empty<string>(), Array.Empty<Attachment>());
                });

                // Typing in the middle of the long note, one character at a time.
                editor.CaretPosition = editor.Document.Blocks.ElementAt(2500).ContentEnd;
                var typing = new Result { Name = "typing in long note (per key)" };
                for (int i = 0; i < 220; i++)
                {
                    var sw = Stopwatch.StartNew();
                    editor.CaretPosition.InsertTextInRun("x");
                    editor.CaretPosition = editor.CaretPosition.GetPositionAtOffset(1) ?? editor.CaretPosition;
                    editor.UpdateLayout();
                    sw.Stop();
                    if (i >= 20) typing.Ms.Add(sw.Elapsed.TotalMilliseconds);   // first 20 are warm-up
                }
                Snapshot(typing);
                results.Add(typing);
            }
            finally { window.Close(); }
        }

        private static void Measure(List<Result> results, string name, int runs, Action action, bool sta = false)
        {
            var r = new Result { Name = name };
            void Go()
            {
                if (runs > 1) action();   // warm-up
                for (int i = 0; i < runs; i++)
                {
                    var sw = Stopwatch.StartNew();
                    action();
                    sw.Stop();
                    r.Ms.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            if (sta) Sta.Run(Go); else Go();
            Snapshot(r);
            results.Add(r);
        }

        private static void Snapshot(Result r)
        {
            r.ManagedBytesAfter = GC.GetTotalMemory(false);
            r.PrivateBytesAfter = Process.GetCurrentProcess().PrivateMemorySize64;
        }

        private static void Write(string outDir, int size, double buildSeconds, List<Result> results)
        {
            Directory.CreateDirectory(outDir);
            var p = Process.GetCurrentProcess();
            var sb = new StringBuilder();
            sb.Append("{\n  \"notes\": ").Append(size)
              .Append(",\n  \"machine\": \"").Append(Environment.MachineName)
              .Append("\",\n  \"generateSeconds\": ").Append(F(buildSeconds))
              .Append(",\n  \"peakWorkingSetMB\": ").Append(F(p.PeakWorkingSet64 / 1048576.0))
              .Append(",\n  \"scenarios\": [\n");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                var s = r.Ms.OrderBy(x => x).ToList();
                double P(double q) => s[Math.Min(s.Count - 1, (int)Math.Ceiling(q * s.Count) - 1)];
                sb.Append("    { \"name\": \"").Append(r.Name).Append("\", \"runs\": ").Append(s.Count)
                  .Append(", \"medianMs\": ").Append(F(P(0.5))).Append(", \"p95Ms\": ").Append(F(P(0.95)))
                  .Append(", \"maxMs\": ").Append(F(s.Last()))
                  .Append(", \"managedMBAfter\": ").Append(F(r.ManagedBytesAfter / 1048576.0))
                  .Append(", \"privateMBAfter\": ").Append(F(r.PrivateBytesAfter / 1048576.0))
                  .Append(" }").Append(i < results.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(outDir, $"results-{size}.json"), sb.ToString());
        }

        private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}

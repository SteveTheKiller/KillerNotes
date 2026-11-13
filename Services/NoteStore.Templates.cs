using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    public static partial class NoteStore
    {
        /// <summary>Copies a saved note, optionally filling its template placeholders.</summary>
        public static long CreateFromTemplate(long templateId, string? title = null, string? group = null, bool expand = true)
        {
            if (!IsOpen || IsReadOnly) return -1;
            var tpl = List().FirstOrDefault(n => n.Id == templateId);
            if (tpl == null) return -1;
            var now = DateTime.Now;
            string newTitle = title ?? (expand ? TemplateText.Expand(tpl.Title, now) : tpl.Title);
            byte[]? blob = LoadContent(templateId);
            byte[] content;
            string plain;
            if (tpl.IsMarkdown)
            {
                plain = MarkdownBlob.Decode(blob);
                if (expand) plain = TemplateText.Expand(plain, now);
                content = MarkdownBlob.Encode(plain);
            }
            else
            {
                var doc = new FlowDocument();
                if (blob != null)
                {
                    using var ms = new MemoryStream(blob);
                    new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage);
                }
                if (expand) TemplateText.ExpandDocument(doc, now);
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                using var outMs = new MemoryStream();
                range.Save(outMs, DataFormats.XamlPackage);
                content = outMs.ToArray();
                plain = range.Text;
            }
            using var tx = _db!.BeginTransaction();
            if (group != null) EnsureGroupPath(group, tx);
            long id = AllocateNoteId(tx);
            using (var insert = _db.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO notes(id, title, notebook, tags, created, modified, content, plain, title_color, spellcheck, format, syntax, sort_order) " +
                    "VALUES ($id, $title, $group, $tags, $now, $now, $content, $plain, $color, $spell, $format, $syntax, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM notes))";
                insert.Parameters.AddWithValue("$id", id);
                insert.Parameters.AddWithValue("$title", newTitle);
                insert.Parameters.AddWithValue("$group", group ?? "");
                insert.Parameters.AddWithValue("$tags", tpl.Tags);
                insert.Parameters.AddWithValue("$now", Ts(now));
                insert.Parameters.AddWithValue("$content", content);
                insert.Parameters.AddWithValue("$plain", plain);
                insert.Parameters.AddWithValue("$color", tpl.TitleColor);
                insert.Parameters.AddWithValue("$spell", tpl.SpellCheck ? 1 : 0);
                insert.Parameters.AddWithValue("$format", tpl.Format);
                insert.Parameters.AddWithValue("$syntax", tpl.SyntaxHighlight ? 1 : 0);
                insert.ExecuteNonQuery();
            }
            SetLinks(id, WikiLinks.Parse(plain), tx);
            CopyNoteAttachments(_db, _db, templateId, id, true, true, tx);
            tx.Commit();
            return id;
        }
    }
}

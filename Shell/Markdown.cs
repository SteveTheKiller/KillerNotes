// ═══════════════════════════════════════════════════════════
//  MARKDOWN NOTES  -  the second content type, and converting between them
// ═══════════════════════════════════════════════════════════
//
// A note is rich text (notes.format 0, content is a XamlPackage) or markdown (format 1, content
// is UTF-8 markdown text). Rich text is the default and every existing note is one.
//
// The editor control is the SAME RichTextBox for both. A markdown note is loaded as one
// Paragraph per line with no formatting, so everything already built on the FlowDocument keeps
// working untouched: FindBar's adorner walk, GlobalReplace, dictation, caret and scroll
// persistence, word wrap. Swapping in a TextBox for markdown notes would have forked all of it.
//
// The consequence to keep in mind: markdown notes ARE plain text, so the formatting commands
// have to be inert while one is open. ApplyFormatMode does that, and MarkdownGuard is the check
// every formatting entry point calls. A bold run inside a markdown note would be silently
// discarded on the next save, which is worse than the command doing nothing.
//
// Paragraph margins are zeroed for markdown. The rich-text default puts space between
// paragraphs, and since every line of markdown source is its own paragraph, that default
// double-spaces the file on screen and misrepresents what is in it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        // Content type of the note currently open in the editor. Set by OpenNote from the row
        // metadata, so it is never a second read of the database.
        private int _currentFormat;

        private bool CurrentIsMarkdown => _currentFormat == Note.FormatMarkdown;

        // ---- Load / save ----

        /// <summary>Fills the editor from a markdown blob: one Paragraph per line, no
        /// formatting. A null or empty blob yields a single empty paragraph, not an empty
        /// document, so the caret has somewhere to land.</summary>
        private void LoadMarkdownIntoEditor(byte[]? blob)
        {
            string text = blob == null || blob.Length == 0
                ? ""
                : new UTF8Encoding(false).GetString(blob);

            // Normalize first: a vault file edited elsewhere can arrive with either ending, and
            // splitting on a raw \n would otherwise leave a trailing \r on every line.
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var p = new Paragraph(new Run(line)) { Margin = new Thickness(0) };
                Editor.Document.Blocks.Add(p);
            }
            if (Editor.Document.Blocks.Count == 0)
                Editor.Document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        }

        /// <summary>The open markdown note's body as it should be stored. The blob and the
        /// plain-text projection are the same bytes here, which is the point of the format:
        /// what is on disk is what the user typed.</summary>
        private byte[] MarkdownBlobFromEditor(out string plain)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var block in Editor.Document.Blocks)
            {
                if (!first) sb.Append('\n');
                first = false;
                if (block is Paragraph p) sb.Append(ParagraphText(p));
            }
            plain = sb.ToString();
            return new UTF8Encoding(false).GetBytes(plain);
        }

        /// <summary>One paragraph's text, LineBreaks included. TextRange over the whole document
        /// would insert its own paragraph separators and re-encode the line endings; this keeps
        /// the file byte-for-byte what the editor shows.</summary>
        private static string ParagraphText(Paragraph p)
        {
            var sb = new StringBuilder();
            void Walk(InlineCollection inlines)
            {
                foreach (var i in inlines)
                {
                    switch (i)
                    {
                        case Run r: sb.Append(r.Text); break;
                        case LineBreak: sb.Append('\n'); break;
                        case Span s: Walk(s.Inlines); break;
                        case Hyperlink h: Walk(h.Inlines); break;
                    }
                }
            }
            Walk(p.Inlines);
            return sb.ToString();
        }

        // ---- Mode gating ----

        /// <summary>Turns the rich-text affordances off for a markdown note and back on for a
        /// rich one. Called by OpenNote after the content is in.</summary>
        private void ApplyFormatMode()
        {
            bool md = CurrentIsMarkdown;
            // The format bar has no meaning over plain text. Collapsed rather than disabled:
            // a row of dead buttons invites clicking them.
            if (FormatBar != null)
                FormatBar.Visibility = md ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Guard for every formatting entry point. Returns true when the command must
        /// NOT run because the open note is markdown, and says so in the status line so the
        /// keystroke does not just vanish with no explanation.</summary>
        private bool MarkdownGuard()
        {
            if (!CurrentIsMarkdown) return false;
            StatusText.Text = Loc("Str_St_MarkdownNoFormatting");
            return true;
        }

        // ---- Convert ----

        private void ConvertFormat_Click(object sender, RoutedEventArgs e)
        {
            if (NotesList.SelectedItems.Cast<Note>().FirstOrDefault() is not Note note) return;
            if (NoteStore.IsReadOnly)
            {
                StatusText.Text = string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner);
                return;
            }

            // Flush first. Converting from a stale blob would throw away whatever is on screen
            // but not yet saved, which for the open note is the most recent thing the user did.
            if (note.Id == _currentId) SaveCurrentNote(refreshList: false);

            if (note.IsMarkdown) ConvertToRich(note);
            else ConvertToMarkdown(note);
        }

        private void ConvertToMarkdown(Note note)
        {
            byte[]? blob = NoteStore.LoadContent(note.Id);
            var doc = new FlowDocument();
            if (blob != null)
            {
                using var ms = new MemoryStream(blob);
                new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage);
            }

            // Name what will not survive BEFORE anything is rewritten. An empty list still gets
            // a confirmation, because the conversion is not undoable through the editor's stack.
            var losses = MarkdownConvert.Losses(doc);
            string body = losses.Count == 0
                ? Loc("Str_Dlg_ToMarkdownBody")
                : Loc("Str_Dlg_ToMarkdownBody") + "\n\n" +
                  string.Join("\n", losses.Select(k => "- " + Loc(k)));

            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_ToMarkdownHead"), note.Title),
                body,
                Loc("Str_Btn_Convert")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            string md = MarkdownConvert.FromDocument(doc);
            byte[] bytes = new UTF8Encoding(false).GetBytes(md);
            NoteStore.SetFormat(note.Id, Note.FormatMarkdown, bytes, md);
            FinishConvert(note, Note.FormatMarkdown);
        }

        private void ConvertToRich(Note note)
        {
            byte[]? blob = NoteStore.LoadContent(note.Id);
            string md = blob == null || blob.Length == 0
                ? ""
                : new UTF8Encoding(false).GetString(blob);

            // Markdown to rich loses nothing, so there is nothing to warn about. It is still
            // confirmed, because it rewrites the stored bytes and the editor cannot undo it.
            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_ToRichHead"), note.Title),
                Loc("Str_Dlg_ToRichBody"),
                Loc("Str_Btn_Convert")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            var doc = MarkdownConvert.ToDocument(md, Editor.FontSize);
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.XamlPackage);
            NoteStore.SetFormat(note.Id, Note.FormatRich, ms.ToArray(), range.Text);
            FinishConvert(note, Note.FormatRich);
        }

        /// <summary>Shared tail: update both sidebar lists in place and reopen the note so the
        /// editor is showing the converted content rather than the pre-conversion document.</summary>
        private void FinishConvert(Note note, int format)
        {
            // BOTH lists, for the reason SaveCurrentNote spells out: ReconcileSidebar can leave
            // _sidebarItems holding an older instance than _notes.
            foreach (var meta in new[] { _notes.FirstOrDefault(n => n.Id == note.Id),
                                         _sidebarItems.OfType<Note>().FirstOrDefault(n => n.Id == note.Id) })
                if (meta != null) { meta.Format = format; meta.Modified = DateTime.Now; }

            if (note.Id == _currentId)
            {
                _dirty = false;   // the store already holds the converted bytes
                OpenNote(note.Id);
            }
            RefreshList(preserveScroll: true);
            StatusText.Text = Loc(format == Note.FormatMarkdown
                ? "Str_St_ConvertedToMarkdown"
                : "Str_St_ConvertedToRich");
        }

        /// <summary>Sets the context-menu row's label to name the direction for the
        /// right-clicked note. Called from NotesContextMenu_Opened.</summary>
        private void UpdateConvertMenuItem()
        {
            if (ConvertFormatItem == null) return;
            var selected = NotesList.SelectedItems.Cast<Note>().ToList();
            // One note at a time: a mixed selection has no single direction, and converting a
            // batch silently in both directions is not something to do behind a single click.
            bool one = _noteContextTarget && selected.Count == 1;
            ConvertFormatItem.Visibility = one ? Visibility.Visible : Visibility.Collapsed;
            if (!one) return;
            ConvertFormatItem.Header = Loc(selected[0].IsMarkdown
                ? "Str_Ctx_ConvertToRich"
                : "Str_Ctx_ConvertToMarkdown");
        }
    }
}

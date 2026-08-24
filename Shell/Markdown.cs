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
// The consequence to keep in mind: markdown notes ARE plain text. ApplyFormatMode hides the
// format bar, and RejectsObject blocks the four paths that would put a non-text object into the
// document - an image, a table, a sketch or an embedded recording. Those are the ones that
// actually lose data: MarkdownBlobFromEditor walks Runs and LineBreaks only, so an object in a
// markdown note is gone at the next autosave with nothing on screen to say so.
//
// Formatting is deliberately NOT blocked. Bold applied by Ctrl+B loses the weight on save but
// keeps every character, so the text survives; refusing the keystroke would be a bigger
// surprise than the formatting quietly not sticking. Objects are the opposite: the whole thing
// disappears, so those are refused up front.
//
// Paragraph margins are zeroed for markdown. The rich-text default puts space between
// paragraphs, and since every line of markdown source is its own paragraph, that default
// double-spaces the file on screen and misrepresents what is in it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            MarkdownBlob.Fill(Editor.Document, MarkdownBlob.Decode(blob));
        }

        /// <summary>The open markdown note's body as it should be stored. The plain-text
        /// projection is the source itself, which is the point of the format: what is on disk is
        /// what the user typed. The blob wraps that source in a XamlPackage so builds older than
        /// this one can still open the note (MarkdownBlob.cs).</summary>
        private byte[] MarkdownBlobFromEditor(out string plain)
        {
            plain = MarkdownBlob.TextOf(Editor.Document);
            return MarkdownBlob.Encode(plain);
        }

        // ---- Mode gating ----

        /// <summary>Turns the rich-text affordances off for a markdown note and back on for a
        /// rich one. Called by OpenNote after the content is in.</summary>
        private void ApplyFormatMode()
        {
            bool md = CurrentIsMarkdown;
            // The format bar has no meaning over plain text. Collapsed rather than disabled:
            // a row of dead buttons invites clicking them.
            FormatBar?.Visibility = md ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>True when the open note is markdown and therefore cannot hold the object the
        /// caller is about to insert. Says so in the status line, because a command that does
        /// nothing and explains nothing reads as a bug.
        ///
        /// Called from the four insertion points rather than from their click handlers, so the
        /// keyboard shortcut, the rail button, paste and drag-drop are all covered at once:
        /// InsertImageAtCaret, InsertTable, PrintSketchToNote, EmbedRecordingInNote.
        ///
        /// PrintDictationToNote is NOT guarded. It inserts plain text, which is exactly what a
        /// markdown note stores, so transcription keeps working; only embedding the audio does
        /// not. Killculator's print is text for the same reason and stays available.</summary>
        private bool RejectsObject()
        {
            if (!CurrentIsMarkdown) return false;
            StatusText.Text = Loc("Str_St_MarkdownNoObjects");
            return true;
        }

        // ---- Convert ----

        /// <summary>Creates a new markdown note. Without this the format is unreachable from a
        /// clean database: converting only works on a note that already exists.</summary>
        private void NewMarkdownNote_Click(object sender, RoutedEventArgs e)
            => CreateNewNote(focusTitle: true, format: Note.FormatMarkdown);

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

            // Capture the original bytes BEFORE the rewrite so Ctrl+Z can put them back exactly.
            // A re-serialized document would not be byte-identical to what was stored.
            byte[] beforeBlob = blob ?? [];
            string beforePlain = new TextRange(doc.ContentStart, doc.ContentEnd).Text;

            string md = MarkdownConvert.FromDocument(doc);
            NoteStore.SetFormat(note.Id, Note.FormatMarkdown, MarkdownBlob.Encode(md), md);
            PushConvertUndo(note, Note.FormatRich, beforeBlob, beforePlain);
            FinishConvert(note, Note.FormatMarkdown);
        }

        private void ConvertToRich(Note note)
        {
            byte[]? blob = NoteStore.LoadContent(note.Id);
            string md = MarkdownBlob.Decode(blob);

            // Markdown to rich loses nothing, so there is nothing to warn about. It is still
            // confirmed, because it rewrites the stored bytes and the editor cannot undo it.
            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_ToRichHead"), note.Title),
                Loc("Str_Dlg_ToRichBody"),
                Loc("Str_Btn_Convert")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            byte[] beforeBlob = blob ?? [];

            var doc = MarkdownConvert.ToDocument(md, Editor.FontSize);
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.XamlPackage);
            NoteStore.SetFormat(note.Id, Note.FormatRich, ms.ToArray(), range.Text);
            PushConvertUndo(note, Note.FormatMarkdown, beforeBlob, md);
            FinishConvert(note, Note.FormatRich);
        }

        /// <summary>Registers the conversion as ONE app-level undo step (ActionUndo.cs), the same
        /// way a cross-note replace does. The original blob is restored verbatim rather than
        /// converted back: a round trip through the other format is lossy, so re-converting would
        /// hand back something subtly different from what the user had.</summary>
        private void PushConvertUndo(Note note, int beforeFormat, byte[] beforeBlob, string beforePlain)
        {
            long id = note.Id;
            PushUndo(() =>
            {
                if (NoteStore.IsReadOnly) return;
                NoteStore.SetFormat(id, beforeFormat, beforeBlob, beforePlain);
                var row = _notes.FirstOrDefault(n => n.Id == id)
                          ?? _sidebarItems.OfType<Note>().FirstOrDefault(n => n.Id == id);
                if (row != null) FinishConvert(row, beforeFormat);
                else RefreshList(preserveScroll: true);
            });
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

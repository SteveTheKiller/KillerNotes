using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Open/save a note, reading position, and dirty tracking.
    public partial class MainWindow
    {
        // ---- Open / save ----

        private void OpenNote(long id)
        {
            var meta = _notes.FirstOrDefault(n => n.Id == id);
            if (meta == null) return;

            SaveNotePosition();   // remember where the outgoing note was left (1.1.1)

            _loadingNote = true;
            _currentId = id;
            // Remembered for the next launch (OpenStartupNote). Demo sessions must
            // never touch real settings.
            if (NoteStore.DemoDbFile == null)
                App.SetSetting("LastNote", $"{NoteStore.ActiveDbFile}|{id}");
            TitleBox.Text = meta.Title;

            DeselectImage();          // ImageResize.cs (handles must not outlive the document swap)
            StopEmbeddedPlayback();   // Editor.Dictation.cs (same reason - and audio from the note
                                      // you just navigated away from must not keep playing)
            Editor.Document.Blocks.Clear();
            Editor.Document.Tag = null; // A note without the marker must not inherit the prior note's syntax state.
            var blob = NoteStore.LoadContent(id);
            // Content type decides how the blob is read (1.3.0). It comes off the row metadata
            // rather than a second query, and must be set before the load so a markdown blob is
            // never handed to the XamlPackage deserializer.
            _currentFormat = meta.Format;
            if (CurrentIsMarkdown)
            {
                LoadMarkdownIntoEditor(blob);   // Markdown.cs
            }
            else if (blob != null)
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var ms = new MemoryStream(blob);
                range.Load(ms, DataFormats.XamlPackage);
            }
            NormalizeThemeColors(Editor.Document);   // Editor.cs (default text follows the live theme)
            NormalizeContentFont(Editor.Document);   // Fonts.cs (baked save-time font must not defeat the ContentFont slot)
            ApplyImageQuality(Editor.Document);      // ImageResize.cs (Fant scaling on loaded images)
            EnsureEditableTail();   // Editor.cs (rule/table as last block traps the caret)
            ApplyFormatMode();      // Markdown.cs (hide the format bar over a plain-text note)
            LoadSyntaxHighlightState();
            ApplyWordWrap(_wordWrap);   // Editor.cs (re-assert the word-wrap page width after the load)
            _loadingNote = false;
            _dirty = false;
            ApplySpellCheck(meta.SpellCheck);   // Editor.cs (per-note flag, off by default)
            ApplyTitleColor(meta);
            ShowEditor(true);
            // The TextSelection object SURVIVES Blocks.Clear() + range.Load and renormalizes its
            // pointers into the NEW note's content - so switching notes could carry a ghost
            // selection that highlighted arbitrary text in the note being opened (seen as opaque
            // blocks over the words, 2026-08-08). Collapse it before restoring the position;
            // RestoreNotePosition only collapses it when a saved caret exists.
            Editor.Selection.Select(Editor.Document.ContentStart, Editor.Document.ContentStart);
            RestoreNotePosition(id);   // reopen where the note was left, not at the top (1.1.1)
            UpdatePreviewState();   // Preview.cs (md/html detection for this note)
            LinkSketchPayloads(id);   // SketchPad: re-attach sketch strokes to their images (Editor.cs)
        }

        // ---- Remembered reading position (1.1.1, #8 follow-up) ----
        // The caret offset and scroll are saved when a note is left (note switch, alt-tab)
        // and restored on open, so a long running note reopens at the spot you were working
        // instead of the top. Position-only changes never touch the modified stamp.

        private void SaveNotePosition()
        {
            if (_currentId < 0 || !NoteStore.IsOpen) return;
            int caret = Editor.Document.ContentStart.GetOffsetToPosition(Editor.CaretPosition);
            NoteStore.SetNotePosition(_currentId, caret, Editor.VerticalOffset);
        }

        /// <summary>Invalidates in-flight scroll-restore retry chains. The id guard alone is not
        /// enough: switch A to B and back to A inside the retry window and the FIRST chain wakes
        /// up (id matches again), yanks the scroll, and runs concurrently with the second.</summary>
        private int _restoreToken;

        private void RestoreNotePosition(long id)
        {
            int token = ++_restoreToken;
            var (caret, scroll) = NoteStore.GetNotePosition(id);
            if (caret > 0 &&
                Editor.Document.ContentStart.GetPositionAtOffset(caret) is TextPointer p)
                Editor.CaretPosition = p;
            if (scroll > 0)
            {
                // Deferred AND retried. The freshly loaded document has no layout yet, so an
                // immediate scroll clamps to 0 - and a LARGE document keeps formatting in the
                // background, so its scrollable extent GROWS for a while after Loaded fires. The
                // old single deferred scroll was clamped to however much had formatted by that
                // moment, which on a 6000-line note was roughly the first quarter - reported as
                // "position memory stops working on big notes" (#16, MrPapaya-JRR). Retry on a
                // short timer until the target offset is reachable, the extent stops growing, or
                // the user switches notes; small notes still land on the first try.
                double lastExtent = -1; int tries = 0;
                void TryScroll()
                {
                    if (_currentId != id || token != _restoreToken) return;   // switched away or superseded: stop
                    Editor.ScrollToVerticalOffset(scroll);
                    bool reached = Editor.VerticalOffset >= scroll - 1;
                    bool growing = Editor.ExtentHeight > lastExtent + 1;
                    lastExtent = Editor.ExtentHeight;
                    if (reached || (!growing && tries >= 5) || ++tries >= 100) return;
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    t.Tick += (_, _) => { t.Stop(); TryScroll(); };
                    t.Start();
                }
                Dispatcher.BeginInvoke(new Action(TryScroll), DispatcherPriority.Loaded);
            }
        }

        /// <summary>Colors the open note's title box (concrete brush) or restores the
        /// theme-reactive default when the note has no title color.</summary>
        private void ApplyTitleColor(Note meta)
        {
            if (meta.TitleBrush is Brush b) TitleBox.Foreground = b;
            else TitleBox.SetResourceReference(ForegroundProperty, "TextBrush");
        }

        /// <summary>Persists the open note (title, XamlPackage blob, plain text for search).</summary>
        private void SaveCurrentNote(bool refreshList = true)
        {
            _saveTimer.Stop();
            if (_currentId < 0 || !_dirty || !NoteStore.IsOpen) return;

            // Syntax colors are a transient view over the user's real rich formatting.
            // Never bake TextEffects into the XamlPackage; restore them immediately after.
            bool rehighlight = _syntaxHighlight;
            if (rehighlight) ClearSyntaxHighlighting();

            byte[] blob;
            string bodyText;
            if (CurrentIsMarkdown)
            {
                // Markdown notes store their source text, not a XamlPackage. The blob and the
                // search projection are the same bytes (Markdown.cs).
                blob = MarkdownBlobFromEditor(out bodyText);
            }
            else
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var ms = new MemoryStream();
                range.Save(ms, DataFormats.XamlPackage);
                blob = ms.ToArray();
                bodyText = range.Text;
            }
            // Sketch text labels ride at the END of the stored plain text (Editor.Sketch.cs), so
            // a labeled diagram is searchable without its labels ever displacing the snippet.
            string sketchLabels = CollectSketchLabelText();
            string storedPlain = sketchLabels.Length == 0 ? bodyText : bodyText + "\n" + sketchLabels;
            NoteStore.Save(_currentId, TitleBox.Text, blob, storedPlain);
            if (rehighlight) ApplySyntaxHighlighting();
            SaveSketchPayloads(_currentId);   // SketchPad: persist sketch strokes by image ordinal (Editor.cs)
            _dirty = false;

            // ALWAYS sync the in-memory row too: OpenNote reads titles from this list, so
            // a stale row resurrected the old title on the next visit and the following
            // save wrote it back over the real one (the "title never saved" bug).
            //
            // BOTH lists: ReconcileSidebar keeps the EXISTING row object when its display
            // data is unchanged, so _sidebarItems can hold an OLDER instance than _notes.
            // A refreshList:false save (note switch, alt-tab) repaints via Items.Refresh -
            // updating only the _notes instance redrew the stale displayed row, and since
            // that save cleared _dirty, the 2s timer's full refresh never ran either, so
            // a new title/snippet never appeared in the sidebar.
            string plain = storedPlain.TrimStart();   // matches the DB's substr snippet, labels included
            int nl = plain.IndexOfAny(['\r', '\n']);
            if (nl >= 0) plain = plain[..nl];
            string snippet = plain.Length > 120 ? plain[..120] : plain;
            foreach (var meta in new[] { _notes.FirstOrDefault(n => n.Id == _currentId),
                                         _sidebarItems.OfType<Note>().FirstOrDefault(n => n.Id == _currentId) })
                if (meta != null)
                {
                    meta.Title = TitleBox.Text;
                    meta.Modified = DateTime.Now;
                    meta.Snippet = snippet;
                }

            if (refreshList)
            {
                // Autosave updates the visible row but must never behave like a new
                // search/sort and throw the reader back to the top of the sidebar.
                RefreshList(preserveScroll: true);
            }
            else
            {
                // Repaint the rows in place so the sidebar text stays accurate.
                _syncingSelection = true;
                NotesList.Items.Refresh();
                _syncingSelection = false;
            }
            UpdatePreviewState();   // Preview.cs (re-detect + refresh an open pane)
        }

        private void MarkDirty()
        {
            if (_loadingNote || _applyingSyntax || _currentId < 0) return;
            _dirty = true;
            _lastActionWasOrg = false;   // a text edit is now the most recent undoable thing (ActionUndo.cs)
            _saveTimer.Stop();
            _saveTimer.Start();
            QueueSyntaxHighlighting();
        }

        private void TitleBox_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();
        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            MarkDirty();
            // Only marks the find bar's flattened copy of the note stale - it does not re-walk
            // anything unless the bar is open and a match is actually being asked for
            // (FindBar.cs).
            InvalidateFindCache();
        }

        // Clicking the note title jumps the view back to the top of the note (Dantex's
        // suggestion, Opera-style). Preview only scrolls the editor viewport - the click
        // still lands in the TextBox for title editing, and the editor caret stays put.
        private void TitleBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => Editor.ScrollToHome();

    }
}

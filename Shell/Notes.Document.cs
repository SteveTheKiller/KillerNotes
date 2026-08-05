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

            DeselectImage();   // ImageResize.cs (handles must not outlive the document swap)
            Editor.Document.Blocks.Clear();
            var blob = NoteStore.LoadContent(id);
            if (blob != null)
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var ms = new MemoryStream(blob);
                range.Load(ms, DataFormats.XamlPackage);
            }
            NormalizeThemeColors(Editor.Document);   // Editor.cs (default text follows the live theme)
            NormalizeContentFont(Editor.Document);   // Fonts.cs (baked save-time font must not defeat the ContentFont slot)
            ApplyImageQuality(Editor.Document);      // ImageResize.cs (Fant scaling on loaded images)
            EnsureEditableTail();   // Editor.cs (rule/table as last block traps the caret)
            ApplyWordWrap(_wordWrap);   // Editor.cs (re-assert the word-wrap page width after the load)
            _loadingNote = false;
            _dirty = false;
            ApplySpellCheck(meta.SpellCheck);   // Editor.cs (per-note flag, off by default)
            ApplyTitleColor(meta);
            ShowEditor(true);
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

        private void RestoreNotePosition(long id)
        {
            var (caret, scroll) = NoteStore.GetNotePosition(id);
            if (caret > 0 &&
                Editor.Document.ContentStart.GetPositionAtOffset(caret) is TextPointer p)
                Editor.CaretPosition = p;
            if (scroll > 0)
                // Deferred: the freshly loaded document has no layout yet, so an immediate
                // scroll would be clamped to 0. Loaded priority runs after measure/arrange.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_currentId == id) Editor.ScrollToVerticalOffset(scroll);
                }), DispatcherPriority.Loaded);
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

            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.XamlPackage);
            NoteStore.Save(_currentId, TitleBox.Text, ms.ToArray(), range.Text);
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
            string plain = range.Text.TrimStart();
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
                RefreshList();
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
            if (_loadingNote || _currentId < 0) return;
            _dirty = true;
            _lastActionWasOrg = false;   // a text edit is now the most recent undoable thing (ActionUndo.cs)
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void TitleBox_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();
        private void Editor_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

        // Clicking the note title jumps the view back to the top of the note (Dantex's
        // suggestion, Opera-style). Preview only scrolls the editor viewport - the click
        // still lands in the TextBox for title editing, and the editor caret stays put.
        private void TitleBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => Editor.ScrollToHome();

    }
}

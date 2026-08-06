using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Empty-state interactions, delete, editor visibility, shutdown.
    public partial class MainWindow
    {
        // ---- Empty-state interactions (no note open) ----

        private void EmptyState_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!NoteStore.IsOpen) return;
            CreateNewNote(focusTitle: false);
        }

        private void EmptyState_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = NoteStore.IsOpen && !_noteDragOut ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void EmptyState_Drop(object sender, DragEventArgs e)
        {
            if (!NoteStore.IsOpen || _noteDragOut) return;
            // Document files become their own notes (ImportExport.cs); images and raw
            // text keep the original behavior of starting one fresh note carrying them.
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsDocImport))
            {
                ImportFiles(files);
                return;
            }
            CreateNewNote(focusTitle: false);
            if (!HandleEditorDrop(e))   // Editor.cs (images); text lands below
            {
                string? txt = e.Data.GetData(DataFormats.UnicodeText) as string
                           ?? e.Data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrEmpty(txt)) { Editor.AppendText(txt); MarkDirty(); }
            }
        }

        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            // ContextMenu DataContext propagation is unreliable (menu lives outside the
            // visual tree), so fall back to the list selection.
            var n = (sender as MenuItem)?.DataContext as Note ?? NotesList.SelectedItem as Note;
            if (n == null) return;
            var sel = NotesList.SelectedItems.Cast<Note>().ToList();
            if (sel.Count > 1 && sel.Contains(n)) DeleteNotesWithConfirm(sel);
            else DeleteNoteWithConfirm(n);
        }

        // Shared by the context menu and the Delete key (Shortcuts.cs).
        private void DeleteNoteWithConfirm(Note n)
        {
            var dlg = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_DeleteNoteHead"), n.Title),
                Loc("Str_Dlg_DeleteNoteBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            if (n.Id == _currentId) SaveNotePosition();   // freshest caret/scroll into the row first
            var snap = NoteStore.CaptureRow(n.Id);   // for Ctrl+Z (ActionUndo.cs)
            NoteStore.Delete(n.Id);
            if (n.Id == _currentId)
            {
                _currentId = -1;
                _dirty = false;
            }
            if (snap != null)
                PushUndo(() => { NoteStore.RestoreRow(snap); RefreshList(); });
            RefreshList();
            StatusText.Text = Loc("Str_St_NoteDeleted");
            OpenStartupNote();   // never drop back to the empty screen
        }

        // Mass delete for a Ctrl/Shift multi-selection: one confirm, one list refresh.
        private void DeleteNotesWithConfirm(List<Note> notes)
        {
            if (notes.Count == 0) return;
            if (notes.Count == 1) { DeleteNoteWithConfirm(notes[0]); return; }

            var dlg = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_DeleteNotesHead"), notes.Count),
                Loc("Str_Dlg_DeleteNoteBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            if (notes.Any(x => x.Id == _currentId)) SaveNotePosition();   // freshest caret/scroll into the row first
            var snaps = notes.Select(x => NoteStore.CaptureRow(x.Id)).Where(s => s != null).Select(s => s!).ToList();
            foreach (var n in notes)
            {
                NoteStore.Delete(n.Id);
                if (n.Id == _currentId)
                {
                    _currentId = -1;
                    _dirty = false;
                }
            }
            if (snaps.Count > 0)
                PushUndo(() => { foreach (var s in snaps) NoteStore.RestoreRow(s); RefreshList(); });
            RefreshList();
            StatusText.Text = string.Format(Loc("Str_St_NotesDeleted"), notes.Count);
            OpenStartupNote();   // never drop back to the empty screen
        }

        // ---- Editor pane visibility ----

        private void ShowEditor(bool visible)
        {
            EmptyState.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            TitleArea.Visibility = FormatBar.Visibility = Editor.Visibility =
                visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) PopInFormatBar();   // FormatBar.cs (first show only)
            else
            {
                PreviewMenuItem.Visibility = Visibility.Collapsed;
                ClosePreview();   // Preview.cs
            }
        }

        // Save on close; Chrome.cs saves window placement in OnClosed.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_closeFadeComplete && IsLoaded && RootGrid.Opacity > 0.01)
            {
                e.Cancel = true;
                var fade = new DoubleAnimation(RootGrid.Opacity, 0,
                    TimeSpan.FromMilliseconds(Anim.FadeMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                fade.Completed += (_, _) =>
                {
                    _closeFadeComplete = true;
                    Dispatcher.BeginInvoke(new Action(Close));
                };
                RootGrid.BeginAnimation(UIElement.OpacityProperty, fade);
                return;
            }
            SaveCurrentNote(refreshList: false);
            NoteStore.Close();
            base.OnClosing(e);
        }
    }
}

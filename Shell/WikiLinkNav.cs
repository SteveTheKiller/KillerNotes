// ═══════════════════════════════════════════════════════════
//  WIKILINK NAVIGATION  -  Ctrl+Click a [[link]] to follow it
// ═══════════════════════════════════════════════════════════
//
// Deliberately NOT implemented by turning [[...]] into Hyperlink elements. Doing that would write
// link markup into the stored XamlPackage, which means: the text you typed is no longer the text
// on disk, a markdown note could not carry links at all (it stores source, not a document), the
// undo stack fills with rewrites nobody asked for, and renaming a note would leave dead elements
// behind. The link lives in the TEXT, exactly as it was typed, and this file is a reader of it -
// the same rule syntax highlighting follows.
//
// Ctrl+Click is the gesture, matching the hyperlink behaviour already in Links.cs, so there is one
// way to follow a link in this editor rather than two.

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private void InitWikiLinks()
        {
            // PREVIEW, so it lands before the editor moves the caret to the click point. Handled
            // only when a link is actually under the pointer, so an ordinary Ctrl+Click still does
            // whatever it did before.
            Editor.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                if (TargetUnder(e.GetPosition(Editor)) is not string target) return;
                e.Handled = true;
                FollowWikiLink(target);
            };

            // The hand says "this is clickable" before you commit to the click, which is the only
            // affordance a link has when it is plain text rather than a styled element.
            Editor.PreviewMouseMove += (_, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                Editor.Cursor = TargetUnder(e.GetPosition(Editor)) != null ? Cursors.Hand : null;
            };
            Editor.PreviewKeyUp += (_, e) =>
            {
                if (e.Key is Key.LeftCtrl or Key.RightCtrl) Editor.Cursor = null;
            };
        }

        /// <summary>The link target under a point in the editor, or null. The offset is measured
        /// the same way SaveCurrentNote measures the text it parses, so the two agree.</summary>
        private string? TargetUnder(Point pt)
        {
            try
            {
                var pos = Editor.GetPositionFromPoint(pt, true);
                if (pos == null) return null;
                string all = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
                int offset = new TextRange(Editor.Document.ContentStart, pos).Text.Length;
                return WikiLinks.TargetAt(all, offset);
            }
            catch (InvalidOperationException) { return null; }   // document changed under the hit test
        }

        /// <summary>Opens the note a link points at. An unresolved target OFFERS TO CREATE IT
        /// rather than doing nothing: writing the link before the note exists is the normal way
        /// to use these, so the dead end is the moment the note should get made.</summary>
        private void FollowWikiLink(string target)
        {
            long id = NoteStore.ResolveTitle(target);
            if (id >= 0)
            {
                SaveCurrentNote(refreshList: false);   // do not lose the edit that is on screen
                OpenNote(id);
                SelectNoteInList(id);
                return;
            }

            if (NoteStore.IsReadOnly)
            {
                FlashStatus(string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner));
                return;
            }

            var confirm = new Controls.ConfirmDialog(
                string.Format(Loc("Str_Dlg_WikiNewHead"), target),
                Loc("Str_Dlg_WikiNewBody"),
                Loc("Str_Btn_Create")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            SaveCurrentNote(refreshList: false);
            long created = NoteStore.Create(target);
            if (created < 0) return;
            RefreshList();
            OpenNote(created);
            SelectNoteInList(created);
        }

        /// <summary>Puts the sidebar selection on a note without re-entering OpenNote - the list
        /// selection handler would open it a second time.</summary>
        private void SelectNoteInList(long id)
        {
            var row = _sidebarItems.OfType<Note>().FirstOrDefault(n => n.Id == id)
                      ?? _notes.FirstOrDefault(n => n.Id == id);
            if (row == null) return;
            // _syncingSelection is the flag the list's own SelectionChanged checks before opening
            // a note. Without it, setting the selection here opens the note a SECOND time, which
            // every other programmatic-selection site in the app already guards against.
            _syncingSelection = true;
            NotesList.SelectedItem = row;
            NotesList.ScrollIntoView(row);
            _syncingSelection = false;
        }
    }
}

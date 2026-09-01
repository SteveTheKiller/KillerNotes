// ═══════════════════════════════════════════════════════════
//  PINNED NOTES  -  a note that stays at the top of its section
// ═══════════════════════════════════════════════════════════
//
// One flag on the row (notes.pinned). RefreshList floats pinned notes to the head of the
// list with a stable sort, so they lead whichever section they belong to - a group, or the
// loose notes underneath - while the chosen sort still orders everything inside the pinned
// run and everything after it. The row shows a small pin at its right edge.
//
// Pinning is presentation, not editing: modified is untouched, so a time-sorted sidebar does
// not reshuffle and the note does not look changed. Alt+P, or the row's right-click menu.

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        // Menu row (MainWindow.xaml PinItem): names the action for the selection. A mixed
        // selection reads "Pin note", and the toggle then pins the rest, matching how the tag
        // rows treat a mixed state.
        private void UpdatePinMenuItem(List<Note> selected)
        {
            bool allPinned = selected.Count > 0 && selected.All(n => n.Pinned);
            PinItem.Header = Loc(allPinned ? "Str_Ctx_UnpinNote" : "Str_Ctx_PinNote");
            PinItem.IsEnabled = selected.Count > 0;
        }

        private void PinNote_Click(object sender, RoutedEventArgs e)
            => TogglePin(NotesList.SelectedItems.OfType<Note>().Where(n => !n.IsDeleted).ToList());

        /// <summary>Alt+P (Shortcuts.cs): the sidebar selection when there is one, else the
        /// note that is open in the editor.</summary>
        private void PinShortcut()
        {
            var notes = NotesList.SelectedItems.OfType<Note>().Where(n => !n.IsDeleted).ToList();
            if (notes.Count == 0 && _notes.FirstOrDefault(n => n.Id == _currentId) is Note open) notes.Add(open);
            TogglePin(notes);
        }

        /// <summary>Pins every note in a selection that is not yet pinned; unpins them all when
        /// they already are. Ctrl+Z puts each note back to the state it had.</summary>
        private void TogglePin(List<Note> notes)
        {
            if (notes.Count == 0 || !NoteStore.IsOpen) return;
            bool pin = notes.Any(n => !n.Pinned);
            var snap = notes.Select(n => (n.Id, n.Pinned)).ToList();
            foreach (var n in notes)
            {
                NoteStore.SetPinned(n.Id, pin);
                n.Pinned = pin;   // notifying, so the row's glyph updates before the refresh
            }
            PushUndo(() =>
            {
                foreach (var (id, was) in snap) NoteStore.SetPinned(id, was);
                RefreshList(preserveScroll: true);
            });
            RefreshList(preserveScroll: true);
            FlashStatus(Loc(pin ? "Str_St_Pinned" : "Str_St_Unpinned"));
        }
    }
}

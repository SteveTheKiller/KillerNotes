using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Group name color (mirrors the per-note title color).
    public partial class MainWindow
    {
        // ---- Group name color (mirrors the per-note title color, Notes.cs) ----

        private void GroupColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            var initial = g.NameBrush is SolidColorBrush sb ? sb.Color
                : (TryFindResource("TextBrush") as SolidColorBrush)?.Color ?? Colors.White;
            string groupName = g.Path;
            string original = g.NameColor;
            var dlg = new ColorPickerDialog(this, initial);
            // Live preview: recolor the group header + its notes' connector line as the color
            // changes in the picker (PreviewGroupColor). The RefreshList below then rebuilds
            // with the stored color (cancel) or the newly saved one (OK).
            dlg.ColorChanged += c => PreviewGroupColor(groupName, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
            if (dlg.ShowDialog() == true)
            {
                NoteStore.SetGroupColor(groupName,
                    $"#{dlg.SelectedColor.R:X2}{dlg.SelectedColor.G:X2}{dlg.SelectedColor.B:X2}");
                PushUndo(() => RestoreGroupColor(groupName, original));
            }
            RefreshList(preserveScroll: true);   // in-place edit - hold position
        }

        // Undo target: restore a group's stored name color by path.
        private void RestoreGroupColor(string path, string hex)
        {
            NoteStore.SetGroupColor(path, hex);
            RefreshList(preserveScroll: true);
        }

        /// <summary>Recolors the open group's header and its notes' spine in place while the
        /// color picker is open, so the change previews as you drag. Transient only - the
        /// caller's RefreshList restores the stored color on cancel (or the saved one on OK).
        /// GroupHeader/Note raise PropertyChanged for the color, so the rows update without a
        /// list rebuild that would reset the scroll position.</summary>
        private void PreviewGroupColor(string groupName, string hex)
        {
            if (NotesList.ItemsSource is not System.Collections.IEnumerable items) return;
            foreach (var it in items)
            {
                if (it is GroupHeader gh &&
                    string.Equals(gh.Path, groupName, StringComparison.OrdinalIgnoreCase))
                    gh.NameColor = hex;
                else if (it is Note n &&
                    string.Equals(n.Notebook, groupName, StringComparison.OrdinalIgnoreCase))
                    n.GroupColor = hex;
            }
        }

        private void GroupColorReset_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            string path = g.Path, original = g.NameColor;
            NoteStore.SetGroupColor(path, "");
            PushUndo(() => RestoreGroupColor(path, original));
            RefreshList(preserveScroll: true);   // in-place edit - hold position
        }

    }
}

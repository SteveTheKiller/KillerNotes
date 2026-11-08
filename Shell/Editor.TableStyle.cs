using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private void TableStyle_Click(object sender, RoutedEventArgs e) => ShowTableStyle();

        private void ShowTableStyle(TableSizingMode initialSizing = TableSizingMode.Keep)
        {
            TableCell? cell = _tblMenuCell ?? CellAt(Editor.Selection.Start);
            _tblMenuCell = null;
            if (cell?.Parent is not TableRow row || !CanEditCurrentNote() || !SaveCurrentNote(false)) return;
            Table table = TableOf(cell);
            long note = _currentId;
            var layer = AdornerLayer.GetAdornerLayer(Editor);
            if (layer == null) return;
            using var session = new TableStyleSession(Editor, table, SaveMarked(Editor.Document.ContentStart, Editor.Document.ContentEnd));
            NormalizeThemeColors(session.PreviewDocument);
            var preview = new TableStylePreviewAdorner(Editor, session.PreviewDocument);
            var hidden = new List<(Adorner Adorner, double Opacity)>();
            double opacity = Editor.Opacity;
            try
            {
                foreach (var adorner in layer.GetAdorners(Editor) ?? Array.Empty<Adorner>())
                {
                    hidden.Add((adorner, adorner.Opacity));
                    adorner.Opacity = 0;
                }
                layer.Add(preview);
                Editor.Opacity = 0;
                var dialog = new TableStyleDialog(table, row.Cells.IndexOf(cell), initialSizing) {
                    Owner = this, MaxHeight = Math.Max(240, ActualHeight - 24) };
                dialog.PreviewChanged += (appearance, sizing) => { session.Preview(appearance, sizing); preview.Reveal(session.PreviewTable); };
                dialog.ShowDialog();
                if (!dialog.Confirmed) return;
                var result = _currentId == note && CanEditCurrentNote() ? session.Commit() : TableStyleCommitResult.TargetChanged;
                if (result == TableStyleCommitResult.TargetChanged)
                {
                    FlashStatus(Loc("Str_TableStyle_ScopeChanged"));
                    return;
                }
                if (result == TableStyleCommitResult.Applied) MarkDirty();
                if (dialog.MakeDefault)
                {
                    string setting = dialog.Appearance.ToSetting();
                    App.SetSetting("DefaultTableStyle", setting);
                    if (App.GetSetting("DefaultTableStyle") != setting) FlashStatus(Loc("Str_TableStyle_DefaultFailed"));
                }
            }
            finally
            {
                layer.Remove(preview);
                Editor.Opacity = opacity;
                foreach (var (adorner, original) in hidden) adorner.Opacity = original;
                Editor.Focus();
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Insert table, with the Office-style size picker.
    public partial class MainWindow
    {
        // ---- Insert table (with Office-style size picker) ----
        // Pressing the table button opens a hover grid: press-hold-drag-release OR
        // click-then-hover-then-click both select a rows x cols size. The inserted table is
        // a real FlowDocument Table; borders bind CardBorderBrush through SetResourceReference
        // so they follow live theme switches (net48 family gotcha: a snapshot would not).

        private const int TblMaxCols = 8;
        private const int TblMaxRows = 6;
        private int _tblCols, _tblRows;
        private int _tblOpenedAt;   // TickCount when the popup opened (click-through guard)
        private (Table Table, int ColumnIndex, double StartX, double StartWidth)? _tableResize;

        private void InitTableSizePicker()
        {
            Editor.PreviewMouseMove += Editor_TableResizeMove;
            Editor.PreviewMouseLeftButtonDown += Editor_TableResizeDown;
            Editor.PreviewMouseLeftButtonUp += Editor_TableResizeUp;
            Editor.LostMouseCapture += (_, _) => _tableResize = null;

            for (int i = 0; i < TblMaxRows * TblMaxCols; i++)
            {
                var cell = new Border
                {
                    Width = 14, Height = 14,
                    Margin = new Thickness(1),
                    BorderThickness = new Thickness(1),
                    Tag = i,
                };
                cell.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                cell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                cell.MouseEnter += TableCell_MouseEnter;
                cell.MouseLeftButtonUp += TableCell_Commit;    // release after press-drag
                cell.MouseLeftButtonDown += TableCell_Commit;  // click in hover mode
                TableSizeCells.Children.Add(cell);
            }
        }

        // Direct table-wall resizing. WPF FlowDocument tables do not expose resize handles,
        // so the editor detects the internal boundary from the first row's laid-out content
        // positions and changes the corresponding TableColumn width while dragging.
        private void Editor_TableResizeMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(Editor);
            if (_tableResize is { } drag && e.LeftButton == MouseButtonState.Pressed)
            {
                drag.Table.Columns[drag.ColumnIndex].Width =
                    new GridLength(Math.Max(28, drag.StartWidth + p.X - drag.StartX));
                Editor.Cursor = Cursors.SizeWE;
                e.Handled = true;
                return;
            }
            Editor.Cursor = TryGetTableBoundary(p, out _, out _, out _) ? Cursors.SizeWE : Cursors.IBeam;
        }

        private void Editor_TableResizeDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(Editor);
            if (!TryGetTableBoundary(p, out Table? table, out int columnIndex, out double[]? widths) ||
                table == null || widths == null) return;

            // Auto-sized FlowDocument columns are interdependent: changing one makes WPF
            // redistribute the rest. Freeze the complete laid-out geometry at drag start so
            // one wall changes one column (and every cell in that column), predictably.
            for (int i = 0; i < widths.Length; i++)
                table.Columns[i].Width = new GridLength(widths[i]);
            _tableResize = (table, columnIndex, p.X, widths[columnIndex]);
            Editor.CaptureMouse();
            e.Handled = true;
        }

        private void Editor_TableResizeUp(object sender, MouseButtonEventArgs e)
        {
            if (_tableResize == null) return;
            _tableResize = null;
            if (Editor.IsMouseCaptured) Editor.ReleaseMouseCapture();
            Editor.Cursor = Cursors.IBeam;
            MarkDirty();
            e.Handled = true;
        }

        private bool TryGetTableBoundary(Point point, out Table? hitTable, out int columnIndex, out double[]? widths)
        {
            hitTable = null;
            columnIndex = -1;
            widths = null;
            foreach (Table table in Tables(Editor.Document.Blocks))
            {
                if (table.RowGroups.Count == 0 || table.RowGroups[0].Rows.Count == 0) continue;
                TableRow first = table.RowGroups[0].Rows[0];
                if (first.Cells.Count == 0 || table.Columns.Count == 0) continue;

                double top = double.MaxValue, bottom = double.MinValue;
                foreach (TableRowGroup group in table.RowGroups)
                    foreach (TableRow row in group.Rows)
                        foreach (TableCell cell in row.Cells)
                        {
                            Rect a = cell.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                            Rect b = cell.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                            if (!a.IsEmpty) top = Math.Min(top, a.Top - cell.Padding.Top - 4);
                            if (!b.IsEmpty) bottom = Math.Max(bottom, b.Bottom + cell.Padding.Bottom + 4);
                        }
                if (point.Y < top || point.Y > bottom) continue;

                int count = Math.Min(first.Cells.Count, table.Columns.Count);
                var lefts = new double[count];
                bool measurable = true;
                for (int i = 0; i < count; i++)
                {
                    Rect rect = first.Cells[i].ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    if (rect.IsEmpty) { measurable = false; break; }
                    lefts[i] = rect.X - first.Cells[i].Padding.Left;
                }
                if (!measurable) continue;

                // A new table fills the document width. Once explicit widths exist, their
                // sum is the authoritative outer edge and remains draggable thereafter.
                double rightEdge = table.Columns.Cast<TableColumn>().Take(count).All(c => c.Width.IsAbsolute)
                    ? lefts[0] + table.Columns.Cast<TableColumn>().Take(count).Sum(c => c.Width.Value)
                    : Math.Max(lefts[count - 1] + 28,
                        Editor.ActualWidth - Editor.Padding.Right - Editor.BorderThickness.Right - 2);
                var measured = new double[count];
                for (int i = 0; i < count; i++)
                    measured[i] = Math.Max(28, (i + 1 < count ? lefts[i + 1] : rightEdge) - lefts[i]);

                // Include the final outer wall: dragging it resizes the last column and thus
                // the whole table, instead of leaving the right edge permanently fixed.
                for (int i = 0; i < count; i++)
                {
                    double wall = i + 1 < count ? lefts[i + 1] : rightEdge;
                    if (Math.Abs(point.X - wall) <= 5)
                    {
                        hitTable = table;
                        columnIndex = i;
                        widths = measured;
                        return true;
                    }
                }
            }
            return false;
        }

        private static System.Collections.Generic.IEnumerable<Table> Tables(System.Collections.Generic.IEnumerable<Block> blocks)
        {
            foreach (Block block in blocks)
                if (block is Table table) yield return table;
                else if (block is Section section)
                    foreach (Table nested in Tables(section.Blocks)) yield return nested;
                else if (block is List list)
                    foreach (ListItem item in list.ListItems)
                        foreach (Table nested in Tables(item.Blocks)) yield return nested;
        }

        // e.Handled keeps the Button from capturing the mouse, so a held drag delivers
        // MouseEnter/MouseUp to the popup cells instead of dying inside the button.
        private void TableBtn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentId < 0) return;
            _tblCols = _tblRows = 0;
            TableSizeLabel.Text = Loc("Str_Lbl_Size");   // same key as its XAML default
            foreach (Border b in TableSizeCells.Children)
                b.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            TableSizePopup.IsOpen = true;
            _tblOpenedAt = Environment.TickCount;
            if (TableSizePopup.Child is UIElement ch) Anim.FadeIn(ch);
            e.Handled = true;
        }

        private void TableCell_MouseEnter(object sender, MouseEventArgs e)
        {
            if ((sender as Border)?.Tag is not int idx) return;
            _tblRows = idx / TblMaxCols + 1;
            _tblCols = idx % TblMaxCols + 1;
            TableSizeLabel.Text = $"{_tblCols} x {_tblRows}";
            int i = 0;
            foreach (Border b in TableSizeCells.Children)
            {
                int r = i / TblMaxCols, c = i % TblMaxCols; i++;
                b.SetResourceReference(Border.BackgroundProperty,
                    r < _tblRows && c < _tblCols ? "RowSelectedBrush" : "SurfaceBrush");
            }
        }

        private void TableCell_Commit(object sender, MouseButtonEventArgs e)
        {
            // Click-through guard: a quick click on the toolbar button releases over the
            // popup a moment later, which used to insert a table by accident. A release
            // within 300ms of opening just leaves the flyout open (for hovering the grid
            // or typing a custom size); the press-hold-DRAG gesture takes longer than
            // that, so it still commits on release.
            if (e.RoutedEvent == MouseLeftButtonUpEvent &&
                Environment.TickCount - _tblOpenedAt < 300) return;

            TableSizePopup.IsOpen = false;
            if (_tblCols > 0 && _tblRows > 0) InsertTable(_tblRows, _tblCols);
            e.Handled = true;
        }

        // Custom size row under the hover grid, for anything bigger than 8x6.
        private void TblCustomInsert_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TblColsBox.Text.Trim(), out int cols) ||
                !int.TryParse(TblRowsBox.Text.Trim(), out int rows) ||
                cols < 1 || rows < 1 || cols > 50 || rows > 200)
            {
                TableSizeLabel.Text = Loc("Str_St_CustomRange");
                return;
            }
            TableSizePopup.IsOpen = false;
            InsertTable(rows, cols);
        }

        private void TblCustom_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { TblCustomInsert_Click(sender, e); e.Handled = true; }
        }

        private void InsertTable(int rows, int cols)
        {
            if (_currentId < 0) return;

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6) };
            table.SetResourceReference(Table.BorderBrushProperty, "CardBorderBrush");
            table.BorderThickness = new Thickness(1, 1, 0, 0);

            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();
            for (int r = 0; r < rows; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < cols; c++)
                {
                    var cell = new TableCell(new Paragraph(new Run("")))
                    {
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(6, 3, 6, 3),
                    };
                    cell.SetResourceReference(TableCell.BorderBrushProperty, "CardBorderBrush");
                    row.Cells.Add(cell);
                }
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);

            var para = Editor.CaretPosition.Paragraph;
            if (para != null && para.Parent is FlowDocument doc) doc.Blocks.InsertAfter(para, table);
            else Editor.Document.Blocks.Add(table);
            EnsureEditableTail();

            MarkDirty();
            Editor.Focus();
        }

        // Sidebar rail SketchPad button (MainWindow.xaml) - same action as F7 / Ctrl+Shift+D.
    }
}

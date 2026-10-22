using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Table editing beyond what a FlowDocument Table gives for free: pasting a copied table as a
    // real table, pasting rows of tab, space or comma separated data into an existing table,
    // deleting a selected table, and the editor menu's Table submenu (rows, columns, delete).
    public partial class MainWindow
    {
        private TableCell? _tblPressCell;   // cell under the last right-click, read when the menu opens
        private TableCell? _tblMenuCell;    // cell the open menu's Table items act on

        private void InitTableEditing()
        {
            Editor.PreviewMouseRightButtonDown += (_, e) =>
                _tblPressCell = CellAt(Editor.GetPositionFromPoint(e.GetPosition(Editor), true));
            Editor.PreviewKeyDown += Editor_TableDeleteKey;
            Editor.PreviewKeyDown += Editor_ImageDeleteKey;   // ImageResize.cs
        }

        // ---- Building ----

        /// <summary>An empty rows x cols table in the family look: one-line grid in the theme's
        /// card border, bound with SetResourceReference so it follows live theme switches.</summary>
        private static Table BuildTable(int rows, int cols)
        {
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6) };
            table.SetResourceReference(Table.BorderBrushProperty, "CardBorderBrush");
            table.BorderThickness = new Thickness(1, 1, 0, 0);

            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();
            for (int r = 0; r < rows; r++) group.Rows.Add(NewTableRow(cols));
            table.RowGroups.Add(group);
            return table;
        }

        private static TableRow NewTableRow(int cols)
        {
            var row = new TableRow();
            for (int c = 0; c < cols; c++) row.Cells.Add(NewTableCell());
            return row;
        }

        private static TableCell NewTableCell(string text = "")
        {
            var cell = new TableCell
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(6, 3, 6, 3),
            };
            cell.SetResourceReference(TableCell.BorderBrushProperty, "CardBorderBrush");
            SetCellText(cell, text);
            return cell;
        }

        /// <summary>Replaces a cell's content with plain text, one paragraph per line.</summary>
        private static void SetCellText(TableCell cell, string text)
        {
            cell.Blocks.Clear();
            foreach (string line in (text ?? "").Split('\n'))
                cell.Blocks.Add(new Paragraph(new Run(line)));
        }

        /// <summary>The table cell holding a position, or null outside every table.</summary>
        private static TableCell? CellAt(TextPointer? at)
        {
            for (DependencyObject? d = at?.Parent; d != null; d = (d as FrameworkContentElement)?.Parent)
                if (d is TableCell cell) return cell;
            return null;
        }

        private static Table TableOf(TableCell cell) => (Table)((TableRowGroup)((TableRow)cell.Parent).Parent).Parent;

        private static IEnumerable<TableRow> AllRows(Table table) => table.RowGroups.SelectMany(g => g.Rows);

        private static int ColumnCount(Table table)
            => Math.Max(table.Columns.Count, AllRows(table).Select(r => r.Cells.Count).DefaultIfEmpty(0).Max());

        // ---- Paste ----

        /// <summary>
        /// Table paste, ahead of the native paste. Outside a table, a clipboard that holds just a
        /// table (a browser, Excel or Word selection) becomes a real table with the same rows and
        /// columns; WPF has no HTML paste path, and a browser offers no RTF, so it used to land as
        /// tab-separated lines. Inside a table, a copied table or tab, space or comma separated
        /// text fills the cells from the caret cell across and down, adding rows and columns when
        /// the data runs past the edge. Returns false to leave the paste to the native path.
        /// </summary>
        private bool TryPasteTable(DataObjectPastingEventArgs e)
        {
            if (_currentId < 0 || CurrentIsMarkdown) return false;
            try
            {
                TableCell? at = CellAt(Editor.Selection.Start);
                List<string[]>? grid = null;
                if (e.DataObject.GetDataPresent(DataFormats.Html) &&
                    e.DataObject.GetData(DataFormats.Html) is string cf && cf.Length > 0 && cf.Length <= 2_000_000)
                    grid = TableGrid.FromHtml(CfHtmlFragment(cf), tableOnly: at == null);
                if (grid == null && at != null &&
                    e.DataObject.GetData(DataFormats.UnicodeText) is string text && text.Length <= 2_000_000)
                    grid = TableGrid.FromText(text);
                if (grid == null) return false;

                e.CancelCommand();
                Editor.BeginChange();
                try
                {
                    if (at != null) FillTable(at, grid);
                    else InsertGridTable(grid);
                }
                finally { Editor.EndChange(); }
                MarkDirty();
                return true;
            }
            catch { return false; }   // a parse hiccup must never block the native paste
        }

        private void InsertGridTable(List<string[]> grid)
        {
            int cols = grid.Max(r => r.Length);
            Table table = BuildTable(grid.Count, cols);
            var rows = table.RowGroups[0].Rows;
            for (int r = 0; r < grid.Count; r++)
                for (int c = 0; c < grid[r].Length; c++)
                    SetCellText(rows[r].Cells[c], grid[r][c]);

            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            Paragraph? para = Editor.CaretPosition.Paragraph;
            if (para != null && CellAt(para.ContentStart) == null)
            {
                // An empty line takes the table in its place above it; otherwise the table goes
                // under the line the caret is on, as Insert table does.
                if (new TextRange(para.ContentStart, para.ContentEnd).Text.Length == 0)
                    para.SiblingBlocks.InsertBefore(para, table);
                else
                    para.SiblingBlocks.InsertAfter(para, table);
            }
            else Editor.Document.Blocks.Add(table);
            EnsureEditableTail();
            Editor.CaretPosition = rows[grid.Count - 1].Cells[cols - 1].ContentEnd;
        }

        private void FillTable(TableCell anchor, List<string[]> grid)
        {
            var row = (TableRow)anchor.Parent;
            var group = (TableRowGroup)row.Parent;
            Table table = TableOf(anchor);
            int r0 = group.Rows.IndexOf(row);
            int c0 = row.Cells.IndexOf(anchor);
            int width = grid.Max(r => r.Length);

            int cols = ColumnCount(table);
            for (int extra = c0 + width - cols; extra > 0; extra--) AddColumnAt(table, ColumnCount(table));
            cols = ColumnCount(table);

            TableCell last = anchor;
            for (int i = 0; i < grid.Count; i++)
            {
                int target = r0 + i;
                while (group.Rows.Count <= target) group.Rows.Add(NewTableRow(cols));
                TableRow tr = group.Rows[target];
                while (tr.Cells.Count < c0 + grid[i].Length) tr.Cells.Add(NewTableCell());
                for (int j = 0; j < grid[i].Length; j++)
                {
                    last = tr.Cells[c0 + j];
                    SetCellText(last, grid[i][j]);
                }
            }
            Editor.CaretPosition = last.ContentEnd;
        }

        // ---- Rows, columns, delete ----

        /// <summary>Inserts an empty column at an index in every row. A table whose columns were
        /// resized by dragging has fixed widths, so the new column copies its neighbor's.</summary>
        private static void AddColumnAt(Table table, int index)
        {
            if (table.Columns.Count > 0)
            {
                int at = Math.Min(index, table.Columns.Count);
                TableColumn near = table.Columns[Math.Min(at, table.Columns.Count - 1)];
                table.Columns.Insert(at, new TableColumn { Width = near.Width.IsAbsolute ? near.Width : GridLength.Auto });
            }
            foreach (TableRow r in AllRows(table))
                r.Cells.Insert(Math.Min(index, r.Cells.Count), NewTableCell());
        }

        private void TableEdit(Action<TableCell> edit)
        {
            TableCell? cell = _tblMenuCell;
            _tblMenuCell = null;
            if (cell == null || cell.Parent is not TableRow || _currentId < 0) return;
            Editor.BeginChange();
            try { edit(cell); }
            finally { Editor.EndChange(); }
            MarkDirty();
            Editor.Focus();
        }

        /// <summary>Keyboard entry for the Table items (ActionShortcuts.cs): acts on the caret's
        /// cell, or on the menu's cell while the menu itself has the keyboard.</summary>
        private void RunTableAction(RoutedEventHandler action)
        {
            if (Keyboard.FocusedElement is not MenuItem || _tblMenuCell == null)
                _tblMenuCell = CurrentIsMarkdown ? null : CellAt(Editor.Selection.Start);
            if (_tblMenuCell == null) return;
            action(this, new RoutedEventArgs());
        }

        private void TableRowAbove_Click(object sender, RoutedEventArgs e) => TableEdit(cell => InsertRow(cell, below: false));
        private void TableRowBelow_Click(object sender, RoutedEventArgs e) => TableEdit(cell => InsertRow(cell, below: true));
        private void TableColLeft_Click(object sender, RoutedEventArgs e) => TableEdit(cell => InsertColumn(cell, right: false));
        private void TableColRight_Click(object sender, RoutedEventArgs e) => TableEdit(cell => InsertColumn(cell, right: true));
        private void TableDelRow_Click(object sender, RoutedEventArgs e) => TableEdit(DeleteRow);
        private void TableDelCol_Click(object sender, RoutedEventArgs e) => TableEdit(DeleteColumn);
        private void TableDelete_Click(object sender, RoutedEventArgs e) => TableEdit(cell => DeleteTable(TableOf(cell)));

        private void InsertRow(TableCell cell, bool below)
        {
            var row = (TableRow)cell.Parent;
            var group = (TableRowGroup)row.Parent;
            int col = row.Cells.IndexOf(cell);
            TableRow fresh = NewTableRow(Math.Max(row.Cells.Count, ColumnCount(TableOf(cell))));
            group.Rows.Insert(group.Rows.IndexOf(row) + (below ? 1 : 0), fresh);
            Editor.CaretPosition = fresh.Cells[Math.Min(col, fresh.Cells.Count - 1)].ContentStart;
        }

        private void InsertColumn(TableCell cell, bool right)
        {
            var row = (TableRow)cell.Parent;
            int index = row.Cells.IndexOf(cell) + (right ? 1 : 0);
            AddColumnAt(TableOf(cell), index);
            Editor.CaretPosition = row.Cells[Math.Min(index, row.Cells.Count - 1)].ContentStart;
        }

        private void DeleteRow(TableCell cell)
        {
            var row = (TableRow)cell.Parent;
            var group = (TableRowGroup)row.Parent;
            Table table = TableOf(cell);
            if (AllRows(table).Count() <= 1) { DeleteTable(table); return; }

            int index = group.Rows.IndexOf(row);
            int col = row.Cells.IndexOf(cell);
            group.Rows.Remove(row);
            if (group.Rows.Count == 0) table.RowGroups.Remove(group);
            else
            {
                TableRow next = group.Rows[Math.Min(index, group.Rows.Count - 1)];
                if (next.Cells.Count > 0) Editor.CaretPosition = next.Cells[Math.Min(col, next.Cells.Count - 1)].ContentStart;
            }
        }

        private void DeleteColumn(TableCell cell)
        {
            var row = (TableRow)cell.Parent;
            Table table = TableOf(cell);
            if (ColumnCount(table) <= 1) { DeleteTable(table); return; }

            int index = row.Cells.IndexOf(cell);
            foreach (TableRow r in AllRows(table).ToList())
                if (index < r.Cells.Count && r.Cells.Count > 1) r.Cells.RemoveAt(index);
            if (index < table.Columns.Count && table.Columns.Count > 1) table.Columns.RemoveAt(index);
            if (row.Cells.Count > 0) Editor.CaretPosition = row.Cells[Math.Min(index, row.Cells.Count - 1)].ContentStart;
        }

        /// <summary>Removes a whole table and leaves the caret where it stood.</summary>
        private void DeleteTable(Table table)
        {
            Block? next = table.NextBlock ?? table.PreviousBlock;
            table.SiblingBlocks.Remove(table);
            EnsureEditableTail();
            Editor.CaretPosition = next?.ContentStart.GetInsertionPosition(LogicalDirection.Forward)
                                   ?? Editor.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        }

        /// <summary>
        /// Delete or Backspace over a selection that covers whole tables removes them. The native
        /// delete only empties the cells of a selected table, leaving the empty grid behind, which
        /// is why a table could not be deleted at all.
        /// </summary>
        private void Editor_TableDeleteKey(object sender, KeyEventArgs e)
        {
            if ((e.Key != Key.Delete && e.Key != Key.Back) || Keyboard.Modifiers != ModifierKeys.None ||
                Editor.Selection.IsEmpty || Editor.IsReadOnly || _currentId < 0) return;

            TextPointer start = Editor.Selection.Start, end = Editor.Selection.End;
            var covered = Tables(Editor.Document.Blocks).Where(t =>
            {
                TableCell[] cells = [.. AllRows(t).SelectMany(r => r.Cells)];
                // A one-cell table only goes when the selection reaches past it, so selecting the
                // text of its single cell and deleting still just clears the text.
                return cells.Length > 0 &&
                       start.CompareTo(cells[0].ContentStart) <= 0 &&
                       end.CompareTo(cells[^1].ContentEnd) >= 0 &&
                       (cells.Length > 1 || start.CompareTo(t.ContentStart) < 0 || end.CompareTo(t.ContentEnd) > 0);
            }).ToList();
            if (covered.Count == 0) return;

            Editor.BeginChange();
            try
            {
                foreach (Table t in covered) t.SiblingBlocks.Remove(t);
                // Whatever text the selection held around the tables goes as well.
                if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
                EnsureEditableTail();
            }
            finally { Editor.EndChange(); }
            MarkDirty();
            e.Handled = true;
        }

        // ---- Context menu ----

        /// <summary>Shows the Table submenu when the right-click (or the menu key) lands in a
        /// table cell. Called from Editor_ContextMenuOpening, after the object/text split.</summary>
        private void UpdateTableMenu(bool onObject)
        {
            _tblMenuCell = onObject || CurrentIsMarkdown ? null : _tblPressCell ?? CellAt(Editor.Selection.Start);
            _tblPressCell = null;
            Visibility v = _tblMenuCell != null ? Visibility.Visible : Visibility.Collapsed;
            TableSeparator.Visibility = v;
            TableMenuItem.Visibility = v;
        }
    }
}

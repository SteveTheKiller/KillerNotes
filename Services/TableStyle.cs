using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KillerNotes.Services
{
    internal enum TableLines { Grid, Thick, Rows, Outline, None }

    internal enum TableSpacing { Compact, Normal, Roomy }

    /// <summary>The Table menu's look and size options. Everything here is stored on the table
    /// itself (border thickness, cell padding, column widths), so it saves with the note. A
    /// FlowDocument row has no height of its own and always fits its text, so row spacing works
    /// through cell padding.</summary>
    internal static partial class TableStyle
    {
        private const double MinColumn = 24;
        internal const double MaxColumn = 1000000;

        internal static bool IsValidColumnWidth(double width) =>
            !double.IsNaN(width) && !double.IsInfinity(width) && width > 0 && width <= MaxColumn;

        private static (Thickness Table, Thickness Cell) Borders(TableLines lines) => lines switch
        {
            TableLines.Thick => (new Thickness(2, 2, 0, 0), new Thickness(0, 0, 2, 2)),
            TableLines.Rows => (new Thickness(0, 1, 0, 0), new Thickness(0, 0, 0, 1)),
            TableLines.Outline => (new Thickness(1), new Thickness(0)),
            TableLines.None => (new Thickness(0), new Thickness(0)),
            _ => (new Thickness(1, 1, 0, 0), new Thickness(0, 0, 1, 1)),
        };

        private static Thickness Padding(TableSpacing spacing) => new(6, spacing switch
        {
            TableSpacing.Compact => 1,
            TableSpacing.Roomy => 8,
            _ => 3,
        }, 6, spacing switch
        {
            TableSpacing.Compact => 1,
            TableSpacing.Roomy => 8,
            _ => 3,
        });

        private static System.Collections.Generic.IEnumerable<TableCell> Cells(Table table) =>
            table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells);

        internal static void ApplyLines(Table table, TableLines lines)
        {
            var (outer, cell) = Borders(lines);
            table.BorderThickness = outer;
            foreach (TableCell c in Cells(table)) c.BorderThickness = cell;
        }

        /// <summary>The style the table carries, read before a structural edit can replace its first cell.</summary>
        internal static TableLines ReadLines(Table table)
        {
            TableCell? first = Cells(table).FirstOrDefault();
            foreach (TableLines lines in Enum.GetValues(typeof(TableLines)))
            {
                var (outer, cell) = Borders(lines);
                if (table.BorderThickness == outer && (first == null || first.BorderThickness == cell)) return lines;
            }
            return TableLines.Grid;
        }

        internal static void ApplySpacing(Table table, TableSpacing spacing)
        {
            foreach (TableCell c in Cells(table)) c.Padding = Padding(spacing);
        }

        internal static TableSpacing ReadSpacing(Table table)
        {
            TableCell? first = Cells(table).FirstOrDefault();
            foreach (TableSpacing spacing in Enum.GetValues(typeof(TableSpacing)))
                if (first != null && first.Padding == Padding(spacing)) return spacing;
            return TableSpacing.Normal;
        }

        /// <summary>Brings cells added by a row, column or paste to the table's own look.</summary>
        internal static void Refresh(Table table)
        {
            ApplyAppearance(table, ReadAppearance(table));
        }

        internal static void EditStructure(Table table, Action edit)
        {
            TableAppearance appearance = ReadAppearance(table);
            var oldRows = table.RowGroups.SelectMany(g => g.Rows).ToArray();
            var oldFirstCells = oldRows.Where(r => r.Cells.Count > 0).Select(r => r.Cells[0]).ToArray();
            edit();
            var rows = table.RowGroups.SelectMany(g => g.Rows).ToArray();
            if (appearance.HeaderRow && oldRows.Length > 0 && rows.Length > 0 &&
                !ReferenceEquals(oldRows[0], rows[0]) && rows.Contains(oldRows[0]))
            {
                oldRows[0].ClearValue(TextElement.FontWeightProperty);
                oldRows[0].ClearValue(TextElement.BackgroundProperty);
            }
            if (appearance.HeaderColumn)
                foreach (TableCell cell in oldFirstCells)
                    if (cell.Parent is TableRow row && rows.Contains(row) && row.Cells.IndexOf(cell) > 0)
                    {
                        cell.ClearValue(TextElement.FontWeightProperty);
                        cell.ClearValue(TextElement.BackgroundProperty);
                    }
            ApplyAppearance(table, appearance);
        }

        /// <summary>Sets one column. The others are shared columns that move whenever one changes,
        /// so they freeze at what is left of the page first.</summary>
        internal static void SetColumnWidth(Table table, int index, double width, double available)
        {
            var columns = table.Columns;
            if (!IsValidColumnWidth(width) || double.IsNaN(available) || double.IsInfinity(available) ||
                index < 0 || index >= columns.Count) return;
            double fixedWidth = columns.Where(c => c.Width.IsAbsolute).Sum(c => c.Width.Value);
            int shared = columns.Count(c => !c.Width.IsAbsolute);
            double share = shared == 0 ? MinColumn : Math.Max(MinColumn, (available - fixedWidth) / shared);
            var widths = columns.Select(c => c.Width.IsAbsolute ? c.Width.Value : share).ToArray();
            widths[index] = Math.Max(MinColumn, width);
            // The width just given no longer comes out of the others' share unless it was shared.
            if (shared > 0)
            {
                double left = Math.Max(MinColumn * (columns.Count - 1), available - widths[index]);
                double others = widths.Where((c, i) => i != index).Sum();
                if (others > 0)
                    for (int i = 0; i < columns.Count; i++)
                        if (i != index) widths[i] = Math.Max(MinColumn, widths[i] * left / others);
            }
            if (widths.Any(w => !IsValidColumnWidth(w))) return;
            for (int i = 0; i < columns.Count; i++) columns[i].Width = new GridLength(widths[i]);
        }

        /// <summary>Each column as wide as its longest line.</summary>
        internal static void FitToContent(Table table, double fontSize, FontFamily font, double pixelsPerDip)
        {
            var widths = new double[table.Columns.Count];
            foreach (TableRow row in table.RowGroups.SelectMany(g => g.Rows))
                for (int i = 0; i < row.Cells.Count && i < widths.Length; i++)
                {
                    TableCell cell = row.Cells[i];
                    // XamlPackage carries embedded images and effective inherited text formatting.
                    // Layout owns only this copy, so measurement cannot change the editor or undo history.
                    var copy = new FlowDocument { FontFamily = cell.FontFamily, FontSize = cell.FontSize,
                        FontWeight = cell.FontWeight, FontStyle = cell.FontStyle, FontStretch = cell.FontStretch,
                        FlowDirection = cell.FlowDirection, Language = cell.Language };
                    using (var stream = new MemoryStream())
                    {
                        new TextRange(cell.ContentStart, cell.ContentEnd).Save(stream, DataFormats.XamlPackage);
                        stream.Position = 0;
                        new TextRange(copy.ContentStart, copy.ContentEnd).Load(stream, DataFormats.XamlPackage);
                    }
                    double width = MeasureBlocks(copy.Blocks);
                    double room = Edge(cell.Padding.Left) + Edge(cell.Padding.Right) + Edge(cell.BorderThickness.Right) + 4;
                    widths[i] = Math.Max(widths[i], Math.Min(MaxColumn, Math.Ceiling(width + room)));
                }
            for (int i = 0; i < widths.Length; i++) table.Columns[i].Width = new GridLength(Math.Max(MinColumn, widths[i]));
        }

        private static double MeasureBlocks(BlockCollection blocks)
        {
            double width = 0;
            foreach (Block block in blocks.ToArray())
            {
                double content = 0;
                if (block is Paragraph paragraph)
                {
                    var text = new TextBlock { FontFamily = paragraph.FontFamily, FontSize = paragraph.FontSize,
                        FontWeight = paragraph.FontWeight, FontStyle = paragraph.FontStyle,
                        FontStretch = paragraph.FontStretch, FlowDirection = paragraph.FlowDirection,
                        Language = paragraph.Language, TextWrapping = TextWrapping.NoWrap };
                    while (paragraph.Inlines.FirstInline is Inline inline)
                    {
                        paragraph.Inlines.Remove(inline);
                        text.Inlines.Add(inline);
                    }
                    text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    content = text.DesiredSize.Width + Math.Max(0, paragraph.TextIndent);
                }
                else if (block is BlockUIContainer container && container.Child is UIElement child)
                {
                    child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    content = child.DesiredSize.Width;
                }
                else if (block is Section section) content = MeasureBlocks(section.Blocks);
                else if (block is System.Windows.Documents.List list)
                    foreach (ListItem item in list.ListItems.ToArray())
                        content = Math.Max(content, MeasureBlocks(item.Blocks));
                else if (block is Table nested)
                    foreach (TableRow row in nested.RowGroups.SelectMany(g => g.Rows).ToArray())
                        content = Math.Max(content, row.Cells.ToArray().Sum(c => MeasureBlocks(c.Blocks) +
                            Edge(c.Padding.Left) + Edge(c.Padding.Right) + Edge(c.BorderThickness.Left) + Edge(c.BorderThickness.Right) + nested.CellSpacing));
                width = Math.Max(width, content + Edge(block.Margin.Left) + Edge(block.Margin.Right) +
                    Edge(block.Padding.Left) + Edge(block.Padding.Right));
            }
            return width;
        }

        // Automatic WPF margins serialize as NaN and contribute no fixed horizontal space.
        private static double Edge(double value) => double.IsNaN(value) ? 0 : value;

        /// <summary>The columns share the page evenly, as a new table does.</summary>
        internal static void FitToPage(Table table)
        {
            foreach (TableColumn c in table.Columns) c.Width = new GridLength(1, GridUnitType.Star);
        }
    }
}

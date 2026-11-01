using System;
using System.Globalization;
using System.Linq;
using System.Windows;
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
    internal static class TableStyle
    {
        private const double MinColumn = 24;

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

        /// <summary>The style the table carries, read from its first cell, which an added cell
        /// never is.</summary>
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
            TableLines lines = ReadLines(table);
            TableSpacing spacing = ReadSpacing(table);
            ApplyLines(table, lines);
            ApplySpacing(table, spacing);
        }

        /// <summary>Sets one column. The others are shared columns that move whenever one changes,
        /// so they freeze at what is left of the page first.</summary>
        internal static void SetColumnWidth(Table table, int index, double width, double available)
        {
            var columns = table.Columns;
            double fixedWidth = columns.Where(c => c.Width.IsAbsolute).Sum(c => c.Width.Value);
            int shared = columns.Count(c => !c.Width.IsAbsolute);
            double share = shared == 0 ? MinColumn : Math.Max(MinColumn, (available - fixedWidth) / shared);
            foreach (TableColumn c in columns.Where(c => !c.Width.IsAbsolute)) c.Width = new GridLength(share);
            columns[index].Width = new GridLength(Math.Max(MinColumn, width));
            // The width just given no longer comes out of the others' share unless it was shared.
            if (shared > 0)
            {
                double left = Math.Max(MinColumn * (columns.Count - 1), available - columns[index].Width.Value);
                double others = columns.Where((c, i) => i != index).Sum(c => c.Width.Value);
                if (others > 0)
                    for (int i = 0; i < columns.Count; i++)
                        if (i != index) columns[i].Width = new GridLength(Math.Max(MinColumn, columns[i].Width.Value * left / others));
            }
        }

        /// <summary>Each column as wide as its longest line.</summary>
        internal static void FitToContent(Table table, double fontSize, FontFamily font, double pixelsPerDip)
        {
            var face = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var widths = new double[table.Columns.Count];
            foreach (TableRow row in table.RowGroups.SelectMany(g => g.Rows))
                for (int i = 0; i < row.Cells.Count && i < widths.Length; i++)
                {
                    TableCell cell = row.Cells[i];
                    string text = new TextRange(cell.ContentStart, cell.ContentEnd).Text;
                    foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    {
                        double width = new FormattedText(line, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            face, fontSize, Brushes.Black, pixelsPerDip).WidthIncludingTrailingWhitespace;
                        double room = cell.Padding.Left + cell.Padding.Right + cell.BorderThickness.Right + 4;
                        widths[i] = Math.Max(widths[i], Math.Ceiling(width + room));
                    }
                }
            for (int i = 0; i < widths.Length; i++) table.Columns[i].Width = new GridLength(Math.Max(MinColumn, widths[i]));
        }

        /// <summary>The columns share the page evenly, as a new table does.</summary>
        internal static void FitToPage(Table table)
        {
            foreach (TableColumn c in table.Columns) c.Width = new GridLength(1, GridUnitType.Star);
        }
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace KillerNotes.Services
{
    internal enum TableLinePattern { Solid, Dotted, Dashed }

    internal sealed class TableAppearance
    {
        internal TableLinePattern Pattern { get; set; }
        internal TableLines Lines { get; set; } = TableLines.Grid;
        internal double Thickness { get; set; } = 1;
        internal TableSpacing Spacing { get; set; } = TableSpacing.Normal;
        internal bool HeaderRow { get; set; }
        internal bool HeaderColumn { get; set; }
        internal string ToSetting() => string.Join("|", "1", Lines.ToString(), Pattern.ToString(),
            Thickness.ToString("R", CultureInfo.InvariantCulture), Spacing.ToString(), HeaderRow ? "1" : "0", HeaderColumn ? "1" : "0");

        internal static TableAppearance FromSetting(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new();
            string[] fields = value!.Split('|');
            if (fields.Length != 7 || fields[0] != "1" ||
                !Enum.TryParse(fields[1], out TableLines lines) || !Enum.IsDefined(typeof(TableLines), lines) ||
                !Enum.TryParse(fields[2], out TableLinePattern pattern) || !Enum.IsDefined(typeof(TableLinePattern), pattern) ||
                !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double thickness) ||
                double.IsNaN(thickness) || double.IsInfinity(thickness) || thickness <= 0 || thickness > 8 ||
                !Enum.TryParse(fields[4], out TableSpacing spacing) || !Enum.IsDefined(typeof(TableSpacing), spacing) ||
                (fields[5] != "0" && fields[5] != "1") || (fields[6] != "0" && fields[6] != "1")) return new();
            return new TableAppearance { Lines = lines, Pattern = pattern, Thickness = thickness, Spacing = spacing,
                HeaderRow = fields[5] == "1", HeaderColumn = fields[6] == "1" };
        }
    }

    internal static partial class TableStyle
    {
        internal static void ApplyAppearance(Table table, TableAppearance appearance)
        {
            double thickness = appearance.Lines == TableLines.Thick ? 2 : appearance.Thickness;
            var (outer, inner) = Borders(appearance.Lines == TableLines.Thick ? TableLines.Grid : appearance.Lines);
            table.BorderThickness = Scale(outer, thickness);
            foreach (TableCell cell in Cells(table)) cell.BorderThickness = Scale(inner, thickness);
            ApplySpacing(table, appearance.Spacing);
            ApplyPattern(table, appearance.Pattern);
            ApplyHeaders(table, appearance.HeaderRow, appearance.HeaderColumn);
        }

        internal static TableAppearance ReadAppearance(Table table)
        {
            var rows = table.RowGroups.SelectMany(g => g.Rows).ToArray();
            TableCell? first = rows.FirstOrDefault()?.Cells.FirstOrDefault();
            Thickness outer = table.BorderThickness;
            Thickness inner = first?.BorderThickness ?? new Thickness(0);
            double thickness = Math.Max(MaxEdge(outer), MaxEdge(inner));
            var lines = outer == new Thickness(0) && inner == new Thickness(0) ? TableLines.None
                : outer.Left > 0 && outer.Top > 0 && outer.Right > 0 && outer.Bottom > 0 && inner == new Thickness(0) ? TableLines.Outline
                : outer.Left == 0 && outer.Top > 0 && inner.Right == 0 && inner.Bottom > 0 ? TableLines.Rows
                : TableLines.Grid;
            return new TableAppearance { Pattern = ReadPattern(table.BorderBrush), Lines = lines,
                Thickness = thickness > 0 ? thickness : 1, Spacing = ReadSpacing(table),
                HeaderRow = rows.Length > 0 && HasHeaderFormat(rows[0]),
                HeaderColumn = rows.Length > 0 && rows.All(r => r.Cells.Count > 0 && HasHeaderFormat(r.Cells[0])) };
        }

        private static Thickness Scale(Thickness value, double scale) =>
            new(value.Left * scale, value.Top * scale, value.Right * scale, value.Bottom * scale);

        private static double MaxEdge(Thickness value) => Math.Max(Math.Max(value.Left, value.Top), Math.Max(value.Right, value.Bottom));

        // This native drawing survives note serialization and identifies owned header shading.
        // Ordinary bold, colored rows keep their chosen backgrounds when a note is reopened.
        private static bool HasHeaderFormat(TextElement element) =>
            element.FontWeight == FontWeights.Bold && element.Background is DrawingBrush {
                Viewbox: { X: 0, Y: 0, Width: 17, Height: 19 }, ViewboxUnits: BrushMappingMode.Absolute,
                Drawing: GeometryDrawing { Geometry: RectangleGeometry { Rect: { X: 0, Y: 0, Width: 17, Height: 19 } } } };

        private static Brush HeaderBrush() => (Brush)XamlReader.Parse(@"<DrawingBrush
            xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
            Viewbox='0,0,17,19' ViewboxUnits='Absolute' Stretch='Fill'>
            <DrawingBrush.Drawing><GeometryDrawing Brush='{DynamicResource TableHeaderBrush}'>
            <GeometryDrawing.Geometry><RectangleGeometry Rect='0,0,17,19'/></GeometryDrawing.Geometry>
            </GeometryDrawing></DrawingBrush.Drawing></DrawingBrush>");

        private static void ApplyHeaders(Table table, bool headerRow, bool headerColumn)
        {
            TableAppearance before = ReadAppearance(table);
            var rows = table.RowGroups.SelectMany(g => g.Rows).ToArray();
            if (rows.Length == 0) return;
            if (headerRow)
            {
                rows[0].FontWeight = FontWeights.Bold;
                rows[0].Background = HeaderBrush();
            }
            else if (before.HeaderRow)
            {
                rows[0].ClearValue(TextElement.FontWeightProperty);
                rows[0].ClearValue(TextElement.BackgroundProperty);
            }
            foreach (TableRow row in rows)
            {
                if (row.Cells.Count == 0) continue;
                TableCell cell = row.Cells[0];
                if (headerColumn)
                {
                    cell.FontWeight = FontWeights.Bold;
                    cell.Background = HeaderBrush();
                }
                else if (before.HeaderColumn)
                {
                    cell.ClearValue(TextElement.FontWeightProperty);
                    cell.ClearValue(TextElement.BackgroundProperty);
                }
            }
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Cells.Count; c++)
                    if ((headerRow && r == 0) || (headerColumn && c == 0))
                    {
                        TableCell cell = rows[r].Cells[c];
                        if (cell.ReadLocalValue(TextElement.FontWeightProperty) != DependencyProperty.UnsetValue &&
                            cell.FontWeight != FontWeights.Bold)
                            cell.ClearValue(TextElement.FontWeightProperty);
                        for (TextPointer? at = cell.ContentStart; at != null && at.CompareTo(cell.ContentEnd) < 0;
                             at = at.GetNextContextPosition(LogicalDirection.Forward))
                            if (at.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                                at.GetAdjacentElement(LogicalDirection.Forward) is TextElement element &&
                                element.ReadLocalValue(TextElement.FontWeightProperty) != DependencyProperty.UnsetValue &&
                                element.FontWeight != FontWeights.Bold)
                                element.ClearValue(TextElement.FontWeightProperty);
                    }
        }

        internal static void RestoreGridBrushes(Table table)
        {
            TableAppearance appearance = ReadAppearance(table);
            ApplyPattern(table, ReadPattern(table.BorderBrush));
            if (appearance.HeaderRow || appearance.HeaderColumn)
                ApplyHeaders(table, appearance.HeaderRow, appearance.HeaderColumn);
        }

        private static TableLinePattern ReadPattern(Brush brush)
        {
            if (brush is not DrawingBrush { TileMode: TileMode.Tile, ViewportUnits: BrushMappingMode.Absolute,
                Drawing: DrawingGroup group } drawing || group.Children.Count != 2) return TableLinePattern.Solid;
            return drawing.Viewport.Width == 8 && drawing.Viewport.Height == 8 ? TableLinePattern.Dashed
                : drawing.Viewport.Width == 2 && drawing.Viewport.Height == 2 ? TableLinePattern.Dotted
                : TableLinePattern.Solid;
        }

        private static Brush PatternBrush(TableLinePattern pattern)
        {
            // A native tiled brush survives rich-note serialization without custom metadata.
            // Alternating squares give either edge direction the same repeating dash and gap.
            int run = pattern == TableLinePattern.Dotted ? 1 : 4;
            int period = run * 2;
            string markup = FormattableString.Invariant($@"<DrawingBrush xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                TileMode='Tile' Viewport='0,0,{period},{period}' ViewportUnits='Absolute' Viewbox='0,0,{period},{period}' ViewboxUnits='Absolute'>
                <DrawingBrush.Drawing><DrawingGroup>
                <GeometryDrawing Brush='{{DynamicResource TextBrush}}' Geometry='M0,0 H{run} V{run} H0 Z'/>
                <GeometryDrawing Brush='{{DynamicResource TextBrush}}' Geometry='M{run},{run} H{period} V{period} H{run} Z'/>
                </DrawingGroup></DrawingBrush.Drawing></DrawingBrush>");
            return (Brush)XamlReader.Parse(markup);
        }

        private static void ApplyPattern(Table table, TableLinePattern pattern)
        {
            if (pattern == TableLinePattern.Solid)
                table.SetResourceReference(Table.BorderBrushProperty, "TextBrush");
            else table.BorderBrush = PatternBrush(pattern);
            foreach (TableCell cell in Cells(table))
                if (pattern == TableLinePattern.Solid)
                    cell.SetResourceReference(TableCell.BorderBrushProperty, "TextBrush");
                else cell.BorderBrush = PatternBrush(pattern);
        }
    }
}

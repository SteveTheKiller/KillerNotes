using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Shell;

namespace KillerNotes.Services
{
    /// <summary>A horizontal rule's line: solid, dotted or dashed, its weight in pixels, and its
    /// color. The pattern reuses Table Style's choices so both dialogs speak the same words.</summary>
    internal sealed record RuleAppearance(TableLinePattern Pattern, double Thickness, Color Color)
    {
        /// <summary>Thin, medium and thick.</summary>
        internal static readonly double[] Weights = { 1, 2, 4 };

        /// <summary>The rule KillerNotes has always drawn: one pixel of solid #5A5A5A.</summary>
        internal static readonly RuleAppearance Default = new(TableLinePattern.Solid, 1, Color.FromRgb(0x5a, 0x5a, 0x5a));

        internal string ToSetting() => FormattableString.Invariant($"{Pattern}|{Thickness}|{Color}");

        internal static RuleAppearance FromSetting(string? value)
        {
            string[] parts = (value ?? "").Split('|');
            if (parts.Length != 3 ||
                !Enum.TryParse(parts[0], out TableLinePattern pattern) || !Enum.IsDefined(typeof(TableLinePattern), pattern) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double thickness) ||
                !Weights.Contains(thickness))
                return Default;
            try
            {
                return ColorConverter.ConvertFromString(parts[2]) is Color color
                    ? new RuleAppearance(pattern, thickness, color) : Default;
            }
            catch (FormatException) { return Default; }
        }
    }

    /// <summary>
    /// Draws a rule's style onto its paragraph and reads it back. A rule is an empty paragraph with
    /// a bottom border (Editor.Rules.cs). WPF borders are always solid, so dotted and dashed rules
    /// paint the border with a tiled brush of upright bars: any horizontal slice of it is the same
    /// run of dashes, whatever the border's height. The tile is plain brush data with a concrete
    /// color, so a rule keeps its look through a save, a copy and a reload with no extra metadata.
    /// </summary>
    internal static class RuleStyle
    {
        internal static Paragraph Create(RuleAppearance appearance)
        {
            var rule = new Paragraph { FontSize = 2, Margin = new Thickness(0, 8, 0, 8) };
            Apply(rule, appearance);
            return rule;
        }

        internal static void Apply(Paragraph rule, RuleAppearance appearance)
        {
            rule.BorderThickness = new Thickness(0, 0, 0, appearance.Thickness);
            rule.BorderBrush = Brush(appearance);
        }

        internal static RuleAppearance Read(Paragraph rule)
        {
            double thickness = rule.BorderThickness.Bottom;
            if (!RuleAppearance.Weights.Contains(thickness)) thickness = RuleAppearance.Default.Thickness;
            switch (rule.BorderBrush)
            {
                case SolidColorBrush solid:
                    return new RuleAppearance(TableLinePattern.Solid, thickness, solid.Color);
                case DrawingBrush { TileMode: TileMode.Tile, ViewportUnits: BrushMappingMode.Absolute,
                                    Drawing: GeometryDrawing { Brush: SolidColorBrush ink } } tiled:
                    double period = tiled.Viewport.Width;
                    var pattern = period == Period(TableLinePattern.Dotted, thickness) ? TableLinePattern.Dotted
                        : period == Period(TableLinePattern.Dashed, thickness) ? TableLinePattern.Dashed
                        : TableLinePattern.Solid;
                    return new RuleAppearance(pattern, thickness, ink.Color);
                default:
                    return RuleAppearance.Default with { Thickness = thickness };
            }
        }

        /// <summary>Gives every rule inside the selection the new style as one undo step, and
        /// returns how many it changed.</summary>
        internal static int Restyle(RichTextBox editor, TextRange selection, RuleAppearance appearance)
        {
            var rules = RulesIn(editor.Document.Blocks, selection).ToList();
            if (rules.Count == 0) return 0;
            editor.BeginChange();
            try { foreach (Paragraph rule in rules) Apply(rule, appearance); }
            finally { editor.EndChange(); }
            return rules.Count;
        }

        internal static IEnumerable<Paragraph> RulesIn(BlockCollection blocks, TextRange selection)
        {
            foreach (Block block in blocks)
            {
                if (block.ContentEnd.CompareTo(selection.Start) < 0 || block.ContentStart.CompareTo(selection.End) > 0) continue;
                switch (block)
                {
                    case Paragraph p when MainWindow.IsRule(p):
                        yield return p;
                        break;
                    case Section section:
                        foreach (Paragraph p in RulesIn(section.Blocks, selection)) yield return p;
                        break;
                    case List list:
                        foreach (ListItem item in list.ListItems)
                            foreach (Paragraph p in RulesIn(item.Blocks, selection)) yield return p;
                        break;
                    case Table table:
                        foreach (TableRowGroup group in table.RowGroups)
                            foreach (TableRow row in group.Rows)
                                foreach (TableCell cell in row.Cells)
                                    foreach (Paragraph p in RulesIn(cell.Blocks, selection)) yield return p;
                        break;
                }
            }
        }

        // Dots are as long as the line is thick with an equal gap; dashes are four times as long
        // with a gap twice the thickness.
        private static double Run(TableLinePattern pattern, double thickness) =>
            pattern == TableLinePattern.Dotted ? thickness : thickness * 4;

        private static double Period(TableLinePattern pattern, double thickness) =>
            Run(pattern, thickness) + (pattern == TableLinePattern.Dotted ? thickness : thickness * 2);

        internal static Brush Brush(RuleAppearance appearance)
        {
            if (appearance.Pattern == TableLinePattern.Solid) return new SolidColorBrush(appearance.Color);
            double run = Run(appearance.Pattern, appearance.Thickness);
            double period = Period(appearance.Pattern, appearance.Thickness);
            return new DrawingBrush(new GeometryDrawing(new SolidColorBrush(appearance.Color), null,
                    new RectangleGeometry(new Rect(0, 0, run, 1))))
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, period, 1), ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, period, 1), ViewboxUnits = BrushMappingMode.Absolute,
            };
        }
    }
}

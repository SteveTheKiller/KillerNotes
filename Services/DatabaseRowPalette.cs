using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace KillerNotes.Services
{
    internal static class DatabaseRowPalette
    {
        internal static void Complete(ResourceDictionary palette)
        {
            Color pane = ((SolidColorBrush)palette["PaneBrush"]).Color;
            Color hover = Over(((SolidColorBrush)palette["RowHoverBrush"]).Color, pane);
            Color selected = Over(((SolidColorBrush)palette["RowSelectedBrush"]).Color, pane);
            bool flat = palette["PaneShadowOpacity"] is double opacity && opacity == 0;
            Color top = flat ? selected : Color.FromRgb(
                (byte)(selected.R + (255 - selected.R) * .04),
                (byte)(selected.G + (255 - selected.G) * .04),
                (byte)(selected.B + (255 - selected.B) * .04));
            Brush fill = flat ? new SolidColorBrush(selected) : new LinearGradientBrush(top, selected, 90);
            fill.Freeze();
            palette["DatabaseSelectionBrush"] = fill;
            palette["DatabaseSelectionTextBrush"] = Readable((SolidColorBrush)palette["TextBrush"], top, selected);
            palette["DatabaseActiveTextBrush"] = Readable((SolidColorBrush)palette["PrimaryBrush"], pane, hover);
            palette["DatabaseMetadataBrush"] = Readable((SolidColorBrush)palette["MutedTextBrush"], pane, hover);
        }

        private static SolidColorBrush Readable(SolidColorBrush preferred, params Color[] backgrounds)
        {
            if (backgrounds.All(bg => Contrast(Over(preferred.Color, bg), bg) >= 4.5)) return preferred;
            Color light = Colors.White, dark = Colors.Black;
            double Score(Color fg) => backgrounds.Min(bg => Contrast(fg, bg));
            var brush = new SolidColorBrush(Score(light) >= Score(dark) ? light : dark);
            brush.Freeze();
            return brush;
        }

        internal static Color Over(Color foreground, Color background)
        {
            double alpha = foreground.A / 255.0;
            return Color.FromRgb((byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
                (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
                (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
        }

        internal static double Contrast(Color a, Color b)
        {
            double Channel(byte c) { double n = c / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            double Lum(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
            double first = Lum(a), second = Lum(b);
            return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
        }
    }
}

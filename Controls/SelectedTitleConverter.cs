using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using KillerNotes.Services;

namespace KillerNotes.Controls
{
    internal sealed class SelectedTitleConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var preferred = values[0] as SolidColorBrush ?? Brushes.White;
            Color[] ColorsOf(object value) => value switch
            {
                GradientBrush gradient => gradient.GradientStops.Select(s => s.Color).ToArray(),
                SolidColorBrush solid => [solid.Color],
                _ => []
            };
            Color[] backgrounds = ColorsOf(values[1]);
            if (values.Length > 3)
            {
                Color window = ColorsOf(values[3]).FirstOrDefault();
                Color pane = ColorsOf(values[2]).FirstOrDefault();
                Color surface = DatabaseRowPalette.Over(pane, window);
                backgrounds = backgrounds.Length == 0 ? [surface]
                    : backgrounds.Select(bg => DatabaseRowPalette.Over(bg, surface)).ToArray();
            }
            if (backgrounds.Length == 0) return preferred;
            double Score(Color color) => backgrounds.Min(bg => DatabaseRowPalette.Contrast(
                DatabaseRowPalette.Over(color, bg), bg));
            if (Score(preferred.Color) >= 4.5) return preferred;
            Color Adjust(Color end)
            {
                for (int step = 1; step <= 255; step++)
                {
                    double amount = step / 255.0;
                    Color candidate = Color.FromRgb(
                        (byte)Math.Round(preferred.Color.R + (end.R - preferred.Color.R) * amount),
                        (byte)Math.Round(preferred.Color.G + (end.G - preferred.Color.G) * amount),
                        (byte)Math.Round(preferred.Color.B + (end.B - preferred.Color.B) * amount));
                    if (Score(candidate) >= 4.5) return candidate;
                }
                return end;
            }
            Color target = Score(Colors.White) >= Score(Colors.Black) ? Colors.White : Colors.Black;
            var readable = new SolidColorBrush(Adjust(target));
            readable.Freeze();
            return readable;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

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
            Color[] backgrounds = values[1] switch
            {
                GradientBrush gradient => gradient.GradientStops.Select(s => s.Color).ToArray(),
                SolidColorBrush solid => [solid.Color],
                _ => []
            };
            if (backgrounds.Length == 0) return preferred;
            double Score(Color color) => backgrounds.Min(bg => DatabaseRowPalette.Contrast(
                DatabaseRowPalette.Over(color, bg), bg));
            if (Score(preferred.Color) >= 4.5) return preferred;
            return Score(Colors.White) >= Score(Colors.Black) ? Brushes.White : Brushes.Black;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

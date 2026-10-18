#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace KillerFamily.Layout
{
    /// <summary>
    /// Centers a glyph's ink instead of its advance box. Icon fonts draw many glyphs off-center
    /// inside their box (a chevron hugs one side, a "?" sits high), so a TextBlock centered in a
    /// rail button leaves the visible shape a pixel or two nearer one edge. With Center set, the
    /// TextBlock's margin moves it by the distance between its box center and its ink center, on
    /// both axes, so the glyph has the same space on every side. The margin is bound to the text
    /// and font, so it follows a glyph swap (the collapse chevron) or a font change by itself.
    /// </summary>
    public static class GlyphInk
    {
        public static readonly DependencyProperty CenterProperty = DependencyProperty.RegisterAttached(
            "Center", typeof(bool), typeof(GlyphInk), new PropertyMetadata(false, OnCenterChanged));

        public static bool GetCenter(DependencyObject d) => (bool)d.GetValue(CenterProperty);
        public static void SetCenter(DependencyObject d, bool value) => d.SetValue(CenterProperty, value);

        private static void OnCenterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock text) return;
            if (!(bool)e.NewValue)
            {
                BindingOperations.ClearBinding(text, FrameworkElement.MarginProperty);
                return;
            }
            var binding = new MultiBinding { Converter = InkOffset.Instance };
            foreach (var dp in new[] { TextBlock.TextProperty, TextBlock.FontFamilyProperty, TextBlock.FontSizeProperty,
                                       TextBlock.FontStyleProperty, TextBlock.FontWeightProperty, TextBlock.FontStretchProperty })
                binding.Bindings.Add(new Binding { Path = new PropertyPath(dp), RelativeSource = RelativeSource.Self });
            BindingOperations.SetBinding(text, FrameworkElement.MarginProperty, binding);
        }

        /// <summary>The margin that moves the glyph's ink center onto its box center.</summary>
        internal static Thickness Offset(string text, FontFamily family, double size, FontStyle style, FontWeight weight, FontStretch stretch)
        {
            if (string.IsNullOrEmpty(text) || family == null || size <= 0) return new Thickness(0);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, style, weight, stretch), size, Brushes.Black, 1.0);
            Rect ink = formatted.BuildGeometry(new Point(0, 0)).Bounds;
            if (ink.IsEmpty) return new Thickness(0);
            double dx = formatted.WidthIncludingTrailingWhitespace / 2 - (ink.X + ink.Width / 2);
            double dy = formatted.Height / 2 - (ink.Y + ink.Height / 2);
            // A centered element moves by half the difference between its two margins.
            return new Thickness(Math.Max(0, 2 * dx), Math.Max(0, 2 * dy), Math.Max(0, -2 * dx), Math.Max(0, -2 * dy));
        }

        private sealed class InkOffset : IMultiValueConverter
        {
            internal static readonly InkOffset Instance = new();

            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values.Length < 6 || values[0] is not string text || values[1] is not FontFamily family ||
                    values[2] is not double size || values[3] is not FontStyle style ||
                    values[4] is not FontWeight weight || values[5] is not FontStretch stretch)
                    return new Thickness(0);
                return Offset(text, family, size, style, weight, stretch);
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
                => throw new NotSupportedException();
        }
    }
}

#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace KillerFamily.Layout
{
    /// <summary>
    /// Centers a radio ring or checkbox square on its label's capital letters, for whatever font
    /// the label is drawn in. Bind the indicator's Margin to the row's FontFamily and FontSize;
    /// ConverterParameter is the margin it would have with no correction ("0,0,8,0").
    /// </summary>
    /// <remarks>
    /// Centering the indicator in the row puts it on the middle of the text's LINE BOX, and where
    /// the capitals sit inside that box is a property of the font. Killer Sans UI sits low (the
    /// ring has to come down a pixel at 12px); Microsoft Sans Serif, 98SE's face, sits in the
    /// middle. A fixed pixel nudge is right for one face at one size and wrong for the other,
    /// which is how this kept drifting from one fix to the next. The offset here comes from the
    /// font's own baseline, cap height and line spacing, so it holds for every theme, every
    /// language fallback and every app size.
    /// </remarks>
    public sealed class CapCenterMargin : IMultiValueConverter
    {
        public static readonly CapCenterMargin Instance = new();

        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            Thickness margin = ParseMargin(parameter as string);
            if (values.Length < 2 || values[0] is not FontFamily family || values[1] is not double size)
                return margin;

            double shift = Offset(family, size);
            return new Thickness(margin.Left, margin.Top + shift, margin.Right, margin.Bottom - shift);
        }

        /// <summary>How far below the line box's middle the middle of the capitals sits, in px.</summary>
        public static double Offset(FontFamily family, double size)
        {
            try
            {
                var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                if (!typeface.TryGetGlyphTypeface(out GlyphTypeface glyphs)) return 0;
                double shift = (family.Baseline - glyphs.CapsHeight / 2 - family.LineSpacing / 2) * size;
                return double.IsNaN(shift) || double.IsInfinity(shift) ? 0 : shift;
            }
            catch { return 0; }   // an unreadable font keeps the plain margin rather than failing layout
        }

        private static Thickness ParseMargin(string? text)
        {
            if (string.IsNullOrEmpty(text)) return new Thickness(0, 0, 8, 0);
            try { return (Thickness)new ThicknessConverter().ConvertFromInvariantString(text!)!; }
            catch { return new Thickness(0, 0, 8, 0); }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

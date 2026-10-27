using System;
using System.Windows.Media;
using KillerNotes.Services;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class ControlVisibilityTests
    {
        [Fact]
        public void EveryPaletteHasAnOpaqueVisibleSliderTrack() => Sta.RunApplication(() =>
        {
            // The track comes from the theme file or the family contract (SliderTrack follows the
            // input edge), so the finished palette is what is checked.
            foreach (Theme theme in Enum.GetValues(typeof(Theme)))
            {
                var palette = ThemeHarness.Create(theme, ThemeHarness.FirstRunAccent(theme));
                var track = Assert.IsType<SolidColorBrush>(palette["SliderTrack"]).Color;
                var menu = Assert.IsType<SolidColorBrush>(palette["MenuBackgroundBrush"]).Color;
                Assert.True(track.A == 255, theme + " has a translucent slider track.");
                double a = Luminance(track), b = Luminance(menu);
                Assert.True((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 2, theme + " has an indistinct slider track.");
            }
        });

        private static double Luminance(Color color)
        {
            static double Channel(byte channel)
            {
                double value = channel / 255.0;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
    }
}

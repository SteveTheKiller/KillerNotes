using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class CardContrastReleaseTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ClassicCardTextIsReadableOnItsActualFill(bool segment) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            var app = Application.Current;
            app.Resources.MergedDictionaries[0] = ThemeHarness.Create(Theme.SE98, Accent.Blue);
            ToggleButton control = segment ? new RadioButton { Content = "Rendered", IsChecked = false }
                : new ToggleButton { Content = "Wrap", IsChecked = true };
            control.Style = (Style)app.FindResource(segment ? "CardSegment" : "CardToggle");
            var surround = new Border { Background = (Brush)app.FindResource("MenuHoverBrush"), Child = control };
            surround.Measure(new Size(200, 40));
            surround.Arrange(new Rect(0, 0, 200, 40));
            surround.UpdateLayout();
            var bd = (Border)control.Template.FindName("Bd", control);
            var foreground = ((SolidColorBrush)control.Foreground).Color;
            var fill = ((SolidColorBrush)(segment ? surround.Background : bd.Background)).Color;
            double ratio = ColorContrast.Contrast(foreground, fill);
            Assert.True(ratio >= 4.5, $"Actual contrast {ratio:0.00}:1, text {foreground}, fill {fill}");
        });
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class PreviewPickerAppearanceTests
    {
        [Theory]
        [InlineData(Theme.Black)]
        [InlineData(Theme.Light)]
        [InlineData(Theme.Sepulchre)]
        [InlineData(Theme.TrashPolka)]
        public void ViewSelectorsUseTheKitDropdownField(Theme theme) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            // Steve, 2026-11-21: back to the normal KillerUI dropdown, the darker field, not see-through.
            var previous = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(theme);
                foreach (string name in new[] { "PreviewModeBtn", "PreviewTitleBtn" })
                {
                    var picker = (ComboBox)window.FindName(name);
                    picker.ApplyTemplate();
                    var face = (Border)picker.Template.FindName("border", picker);
                    var field = (SolidColorBrush)picker.FindResource("ComboFieldBrush");
                    // Trash Polka may lay the window's ink over the field; anywhere else it is the plain field color.
                    if (face.Background is SolidColorBrush solid) Assert.Equal(field.Color, solid.Color);
                    else Assert.Equal(Theme.TrashPolka, theme);
                }
            }
            finally { ThemeManager.Apply(previous); }
        });

        [Fact]
        public void ViewPickerFitsItsLabelsAndGrowsWhenTheLanguageNeedsMoreRoom() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            MarkdownAuthoringTests.Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            var picker = (ComboBox)window.FindName("PreviewModeBtn");
            var locale = LocaleManager.Current;
            try
            {
                LocaleManager.Apply(Locale.EnUS);
                picker.SelectedValue = "Rendered";
                Layout();
                double english = picker.ActualWidth;
                Assert.InRange(english, 70, 125);
                foreach (string mode in new[] { "Source", "Rendered", "Split" })
                {
                    picker.SelectedValue = mode;
                    Layout();
                    Assert.Equal(english, picker.ActualWidth, 1);
                }
                LocaleManager.Apply(Locale.HuHU);
                Layout();
                Assert.True(picker.ActualWidth > english + 1, "Longer translated labels need a wider selector.");
                LocaleManager.Apply(Locale.EnUS);
                Layout();
                Assert.Equal(english, picker.ActualWidth, 1);
            }
            finally { LocaleManager.Apply(locale); }

            void Layout()
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                window.UpdateLayout();
            }
        });
    }
}

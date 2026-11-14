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
        public void ViewSelectorsLetTheirSurroundingSurfaceShowThrough(Theme theme) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var previous = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(theme);
                foreach (string name in new[] { "PreviewModeBtn", "PreviewTitleBtn" })
                {
                    var picker = (ComboBox)window.FindName(name);
                    picker.ApplyTemplate();
                    var face = (Border)picker.Template.FindName("border", picker);
                    var brush = Assert.IsType<SolidColorBrush>(face.Background);
                    Assert.Equal(0, brush.Color.A);
                }
            }
            finally { ThemeManager.Apply(previous); }
        });

        [Fact]
        public void ViewPickerFitsItsLabelsAndGrowsWhenTheLanguageNeedsMoreRoom() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            MarkdownAuthoringTests.Ready(window);
            ((Button)window.FindName("NewMarkdownQuickBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

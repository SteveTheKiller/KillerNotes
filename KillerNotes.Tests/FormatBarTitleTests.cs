using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The expanded format bar takes room from the title. A note with no format bar, such as
    /// a markdown note, gets that room back, or a long title stops short of an empty stretch.</summary>
    public sealed class FormatBarTitleTests
    {
        private static void Layout(FrameworkElement host)
        {
            host.Measure(new Size(800, 100));
            host.Arrange(new Rect(0, 0, 800, 100));
            host.UpdateLayout();
        }

        [Fact]
        public void AHiddenFormatBarGivesTheTitleItsRoomBack() => Sta.Run(() =>
        {
            var (shell, _) = EditorShell.Make();
            var title = new TextBox { Margin = new Thickness(12, 10, 12, 4) };
            var bar = new Border
            {
                Width = 600, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0),
            };
            var host = new Grid();
            host.Children.Add(title);
            host.Children.Add(bar);
            EditorShell.Set(shell, "TitleBox", title);
            EditorShell.Set(shell, "FormatBar", bar);

            Layout(host);
            EditorShell.Invoke(shell, "ClearTitleOfFormatBar");
            Assert.True(title.Margin.Right > 600, "The title should make room for the bar.");

            bar.Visibility = Visibility.Collapsed;
            Layout(host);
            EditorShell.Invoke(shell, "ClearTitleOfFormatBar");

            Assert.Equal(12, title.Margin.Right);
        });
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class FormatBarTitleTests
    {
        [Fact]
        public void MinimizingTheFormatBarGivesTheTitleItsRoomBack() => CardColorBehaviorTests.WithShell((window,_) =>
        {
            MarkdownAuthoringTests.Ready(window);
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            var title=(TextBox)window.FindName("TitleBox");
            Assert.True(title.Margin.Left > 12 || title.Margin.Right > 12,"The title should make room for the expanded bar.");
            ((WrapPanel)window.FindName("FmtButtons")).Children.OfType<Button>().Single(b=>b.Content as string=="\uE70E")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(12,title.Margin.Left);
            Assert.Equal(12, title.Margin.Right);
        });
    }
}

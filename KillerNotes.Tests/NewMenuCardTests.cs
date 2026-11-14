using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class NewMenuCardTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CreationCardClosesAndCreatesTheSelectedNoteKind(bool markdown) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            int count = NoteStore.List().Count;
            ((Button)window.FindName("NewMenuBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var menu = (ContextMenu)window.FindName("NewMenu");
            try
            {
                Assert.True(menu.IsOpen);
                Assert.Same(Application.Current.FindResource("CardMenu"), menu.Style);
                var action = Assert.IsType<Button>(window.FindName(markdown ? "NewMarkdownQuickBtn" : "NewRichQuickBtn"));
                Assert.Same(Application.Current.FindResource("CardQuickButton"), action.Style);
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(menu.IsOpen);
                Assert.Equal(count+1, NoteStore.List().Count);
                Assert.Equal(markdown ? 1 : 0, NoteStore.List().OrderByDescending(n => n.Id).First().Format);
            }
            finally { menu.IsOpen = false; }
        });
    }
}

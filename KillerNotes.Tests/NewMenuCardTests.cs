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
            var menu = CardTileHelper.OpenNewMenu(window);
            try
            {
                Assert.True(menu.IsOpen);
                Assert.Same(Application.Current.FindResource("CardMenu"), menu.Style);
                // 1.7.1: the card is a KillerUI tile grid; each kind is a tile found by its Id.
                var action = CardTileHelper.Tile(menu, markdown ? "markdown" : "rich");
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(menu.IsOpen);
                Assert.Equal(count+1, NoteStore.List().Count);
                Assert.Equal(markdown ? 1 : 0, NoteStore.List().OrderByDescending(n => n.Id).First().Format);
            }
            finally { menu.IsOpen = false; }
        });
    }
}

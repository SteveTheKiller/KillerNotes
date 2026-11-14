using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class ReadOnlyTagCardTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReadOnlyCardAssignmentReportsTheRefusalWithoutChangingTags(bool multiple) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long first = NoteStore.Create("First");
            long second = NoteStore.Create("Second");
            using var readOnly = new TagAssignmentTests.HeldReadOnlyStore();
            Assert.True(NoteStore.IsReadOnly);
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            owner.Show();
            var menu = (ContextMenu)window.FindName("NoteCard");
            try
            {
                host.ShowNoteCard(multiple ? new[] { first, second } : new[] { first }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var tags = (MenuItem)window.FindName("TagsMenu");
                var assign = tags.Items.OfType<MenuItem>().First();
                assign.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Contains("Read-only", ((TextBlock)window.FindName("StatusText")).Text);
                Assert.False(assign.IsEnabled);
                Assert.Equal("", NoteStore.GetNote(first)!.Tags);
                Assert.Equal("", NoteStore.GetNote(second)!.Tags);
            }
            finally { menu.IsOpen = false; owner.Close(); }
        });
    }
}

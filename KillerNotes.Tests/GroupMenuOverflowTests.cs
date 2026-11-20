using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class GroupMenuOverflowTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(120)]
        public void GroupSubmenuScrollsOnlyWhenNeededAndReachesItsLastAction(int groups) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            for (int i = 0; i < groups; i++) NoteStore.AddGroup($"Group {i:000}");
            long id = NoteStore.Create("Overflow sample");
            var target = new Button { Content = "Group card target" };
            var owner = new Window { Content = target, Width = 240, Height = 120, ShowInTaskbar = false };
            var card = (ContextMenu)window.FindName("NoteCard");
            ContextMenu? menu = null;
            try
            {
                owner.Show();
                host.ShowNoteCard(new[] { id }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                // 1.7.1: the group list is the Group tile's flyout beside the note card.
                menu = CardTileHelper.OpenFlyout(card, "group");
                FrameworkElement root = menu;
                root.UpdateLayout();
                var scroll = Elements(root).OfType<ScrollViewer>().Single();
                Assert.True(root.ActualHeight <= SystemParameters.WorkArea.Height,
                    "The group list must stay within the work area.");
                Assert.Equal(groups > 20 ? Visibility.Visible : Visibility.Collapsed,
                    scroll.ComputedVerticalScrollBarVisibility);
                var last = (MenuItem)menu.Items[menu.Items.Count - 1];
                Assert.True(last.Focus(), "The final action must accept keyboard focus.");
                last.BringIntoView();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                if (groups > 20) Assert.True(scroll.VerticalOffset > 0);
                var bounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
                Assert.True(bounds.Bottom <= scroll.ActualHeight + 1 && bounds.Top >= -1,
                    "The final action must scroll into the visible list.");
            }
            finally { if (menu != null) menu.IsOpen = false; card.IsOpen = false; owner.Close(); }
        });

        private static IEnumerable<DependencyObject> Elements(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}

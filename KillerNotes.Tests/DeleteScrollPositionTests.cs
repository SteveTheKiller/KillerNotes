using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class DeleteScrollPositionTests
    {
        [Theory]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        public void DeleteAndUndoKeepTheScrolledSidebarInPlace(int count, bool confirm) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            MarkdownAuthoringTests.Ready(window);
            for (int i = 0; i < 60; i++) NoteStore.Create($"Scroll sample {i:00}");
            ((TextBox)window.FindName("SearchBox")).Text = "Scroll sample";
            Drain(window);
            var list = (ListBox)window.FindName("NotesList");
            var selected = list.Items.OfType<Note>().Skip(25).Take(count).ToArray();
            Assert.Equal(count, selected.Length);
            list.SelectedItems.Clear();
            foreach (var note in selected) list.SelectedItems.Add(note);
            list.ScrollIntoView(selected[0]);
            Drain(window);
            var scroll = Descendant<ScrollViewer>(list)!;
            Assert.NotNull(scroll);
            double before = scroll.VerticalOffset;
            Assert.True(before > 100, "The selected notes must be well below the top before deletion.");

            bool prompted = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmDialog>().FirstOrDefault(d => d.IsVisible);
                if (dialog == null) return;
                timer.Stop();
                prompted = true;
                if (confirm) ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else dialog.Close();
            };
            try
            {
                timer.Start();
                ((MenuItem)window.FindName("CardDeleteItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Drain(window);
                Assert.True(prompted);
                Assert.Equal(confirm ? count : 0, NoteStore.ListTrash().Count);
                Assert.InRange(Math.Abs(scroll.VerticalOffset - Math.Min(before, scroll.ScrollableHeight)), 0, 1);
                if (confirm)
                {
                    double afterDelete = scroll.VerticalOffset;
                    Assert.True(host.Undo());
                    Drain(window);
                    Assert.Empty(NoteStore.ListTrash());
                    Assert.InRange(Math.Abs(scroll.VerticalOffset - Math.Min(afterDelete, scroll.ScrollableHeight)), 0, 1);
                }
            }
            finally { timer.Stop(); }
        });

        private static void Drain(Window window)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                if (Descendant<T>(child) is T nested) return nested;
            }
            return null;
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Input;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class SurfaceFollowupTests
    {
        [Fact]
        public void GraphGripBackingAppearsOnlyWithTheClassicFrame() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var previous = ThemeManager.Current;
            var graph = new GraphWindow(_ => { }, _ => { }, -1, "Graph");
            try
            {
                graph.Show();
                ThemeManager.Apply(Theme.Dark);
                graph.UpdateLayout();
                var patch = Descendants(graph).OfType<Border>().Single(b => b.Width == 21 && b.Height == 21);
                Assert.False(patch.IsVisible);
                ThemeManager.Apply(Theme.SE98);
                graph.UpdateLayout();
                Assert.True(patch.IsVisible);
                ThemeManager.Apply(Theme.Dark);
                graph.UpdateLayout();
                Assert.False(patch.IsVisible);
            }
            finally { graph.Opacity = 0; graph.Close(); ThemeManager.Apply(previous); }
        });

        [Fact]
        public void ManageTagsKeepsTheAddAndDoneActionsOnOneCompactRow() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var dialog = new TagsDialog();
            try
            {
                dialog.Show();
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var name = (TextBox)dialog.FindName("NewNameBox");
                var done = (Button)dialog.FindName("DoneBtn");
                var list = (ListBox)dialog.FindName("TagList");
                var root = (UIElement)dialog.Content;
                Assert.InRange(System.Math.Abs(name.TranslatePoint(new Point(0, name.ActualHeight / 2), root).Y
                    - done.TranslatePoint(new Point(0, done.ActualHeight / 2), root).Y), 0, 1);
                Assert.False(((TextBlock)dialog.FindName("DlgStatus")).IsVisible);
                var body = (StackPanel)((Grid)((Border)list.Parent).Parent).Parent;
                Assert.InRange(body.Margin.Left, 8, 16);
                Assert.InRange(body.Margin.Right, 8, 16);
                Assert.InRange(body.Margin.Top, 4, 10);
            }
            finally { dialog.Opacity = 0; dialog.Close(); }
        });

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var next in Descendants(child)) yield return next;
            }
        }
    }
}

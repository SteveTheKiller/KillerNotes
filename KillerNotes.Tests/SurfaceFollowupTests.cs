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

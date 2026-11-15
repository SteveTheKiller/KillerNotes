using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    // The format bar floats over the top of the note by default, so showing it never moves the
    // title or the text. Docked, it takes its own row and the note starts below it.
    public sealed class FormatBarDockTests
    {
        private static (FormatBarDock Dock, Border Bar, Border Title) Laid(bool docked)
        {
            var bar = new Border { Width = 200, Height = 30, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 8, 0), Child = new Button() };
            var title = new Border { Height = 46 };
            var dock = new FormatBarDock { Docked = docked, Content = title, Bar = bar };
            Relayout(dock, 600);
            return (dock, bar, title);
        }

        private static void Relayout(FrameworkElement dock, double width)
        {
            dock.InvalidateMeasure();
            dock.InvalidateArrange();
            dock.Measure(new Size(width, double.PositiveInfinity));
            dock.Arrange(new Rect(0, 0, width, dock.DesiredSize.Height));
        }

        private static Rect Bounds(UIElement child, Visual parent) =>
            child.TransformToAncestor(parent).TransformBounds(new Rect(child.RenderSize));

        [Fact]
        public void FloatingTheTitleKeepsItsPlaceWithOrWithoutTheBar() => Sta.Run(() =>
        {
            var (dock, _, title) = Laid(docked: false);
            Assert.Equal(new Rect(0, 0, 600, 46), Bounds(title, dock));
            Assert.Equal(46, dock.DesiredSize.Height);
            dock.Bar = null;
            Relayout(dock, 600);
            Assert.Equal(new Rect(0, 0, 600, 46), Bounds(title, dock));
        });

        [Fact]
        public void FloatingTheBarKeepsItsOwnPlacementAndIsDrawnOverTheTitle() => Sta.Run(() =>
        {
            var (dock, bar, title) = Laid(docked: false);
            Assert.Equal(new Rect(600 - 200 - 8, 0, 200, 30), Bounds(bar, dock));
            var order = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(dock)).Select(i => VisualTreeHelper.GetChild(dock, i)).ToList();
            Assert.True(order.IndexOf(bar) > order.IndexOf(title));
        });

        [Fact]
        public void DockedTheBarSpansTheWidthAndTheTitleStartsBelowIt() => Sta.Run(() =>
        {
            var (dock, bar, title) = Laid(docked: true);
            bar.Width = double.NaN;
            bar.HorizontalAlignment = HorizontalAlignment.Stretch;
            bar.Margin = new Thickness(0);
            Relayout(dock, 600);
            Assert.Equal(new Rect(0, 0, 600, 30), Bounds(bar, dock));
            Assert.Equal(new Rect(0, 30, 600, 46), Bounds(title, dock));
            Assert.Equal(76, dock.DesiredSize.Height);
        });

        [Fact]
        public void ACollapsedBarTakesNoRowWhenDocked() => Sta.Run(() =>
        {
            var (dock, bar, title) = Laid(docked: true);
            bar.Visibility = Visibility.Collapsed;
            Relayout(dock, 600);
            Assert.Equal(0, Bounds(title, dock).Top);
        });

        [Fact]
        public void SwitchingModesMovesTheTitleWithoutReplacingTheBar() => Sta.Run(() =>
        {
            var (dock, bar, title) = Laid(docked: false);
            dock.Docked = true;
            Relayout(dock, 600);
            Assert.Same(bar, dock.Bar);
            Assert.Equal(30, Bounds(title, dock).Top);
            dock.Docked = false;
            Relayout(dock, 600);
            Assert.Equal(0, Bounds(title, dock).Top);
        });
    }
}

using System.Windows;
using System.Windows.Controls;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class FlyoutPlacementTests
    {
        [Theory]
        [InlineData(false, 0.7)]
        [InlineData(false, 1.0)]
        [InlineData(false, 1.5)]
        [InlineData(true, 1.0)]
        [InlineData(true, 2.5)]
        public void SpeechMenuKeepsSideInsetAndCentersOnItsButton(bool right, double scale) => Sta.Run(() =>
        {
            var root = new Canvas { Width = 600, Height = 400 };
            var pane = new Border { Width = 500, Height = 300 };
            var button = new Button { Width = 20, Height = 20 };
            Canvas.SetLeft(pane, 50);
            Canvas.SetTop(pane, 40);
            Canvas.SetTop(button, 140);
            root.Children.Add(pane);
            root.Children.Add(button);
            root.Measure(new Size(600, 400));
            root.Arrange(new Rect(0, 0, 600, 400));
            var halo = new Border { Style = Application.Current?.TryFindResource("FlyoutCard") as Style }.Margin;
            var menu = new ContextMenu();
            bool previousSide = FlyoutPlacement.RailOnRight;
            try
            {
                FlyoutPlacement.RailOnRight = right;
                FlyoutPlacement.UsePane(pane);
                FlyoutPlacement.AttachBesideButton(menu, button);
                Assert.Same(pane, menu.PlacementTarget);
                var popup = new Size(200 + halo.Left + halo.Right, 40 + halo.Top + halo.Bottom);
                var target = new Size(500 * scale, 300 * scale);
                var point = Assert.Single(menu.CustomPopupPlacementCallback(popup, target, new Point())).Point;
                Assert.Equal(right ? target.Width - 8 : 8,
                    right ? point.X + popup.Width - halo.Right : point.X + halo.Left, 5);
                Assert.Equal(110 * scale, point.Y + halo.Top + 20, 5);

                // Reordered buttons near either edge keep the visible card inside the pane.
                foreach (double top in new[] { 30d, 330d })
                {
                    Canvas.SetTop(button, top);
                    root.Arrange(new Rect(0, 0, 600, 400));
                    point = Assert.Single(menu.CustomPopupPlacementCallback(popup, target, new Point())).Point;
                    Assert.True(point.Y + halo.Top >= 8);
                    Assert.True(point.Y + popup.Height - halo.Bottom <= target.Height - 8);
                }
            }
            finally { FlyoutPlacement.RailOnRight = previousSide; }
        });
    }
}

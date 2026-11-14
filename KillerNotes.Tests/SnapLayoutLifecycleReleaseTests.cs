using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using KillerFamily.Windowing;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class SnapLayoutLifecycleReleaseTests
    {
        [Fact]
        public void DisposedCompositionTargetIsNotAMaximizeHit() => Sta.Run(() =>
        {
            using var source = new HwndSource(new HwndSourceParameters("Snap layout lifecycle")
            {
                Width = 200,
                Height = 100,
                WindowStyle = unchecked((int)0x90000000),
            });
            var button = new Button { Width = 80, Height = 30 };
            var root = new Grid { Width = 200, Height = 100 };
            root.Children.Add(button);
            source.RootVisual = root;
            root.Measure(new Size(200, 100));
            root.Arrange(new Rect(0, 0, 200, 100));
            root.UpdateLayout();
            Point screen = button.PointToScreen(new Point(10, 10));
            var packed = new IntPtr(((int)(short)screen.Y << 16) | (ushort)(short)screen.X);
            Assert.True(SnapLayouts.HitTest(button, packed));

            source.CompositionTarget.Dispose();

            Assert.Same(source, PresentationSource.FromVisual(button));
            Assert.Null(source.CompositionTarget);
            Assert.False(SnapLayouts.HitTest(button, packed));
        });
    }
}

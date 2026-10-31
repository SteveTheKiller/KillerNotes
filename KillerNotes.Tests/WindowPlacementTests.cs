using System.Windows;
using KillerNotes.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    public class WindowPlacementTests
    {
        // A tray app is often closed while minimized. A window minimized from maximized restores to
        // maximized, so that is the state to save, not the minimized one.
        [Fact]
        public void AWindowMinimizedFromMaximizedStillCountsAsMaximized() => Sta.Run(() =>
        {
            var window = new Window { Left = 40, Top = 50, Width = 640, Height = 480 };
            WindowPlacement.Restore(window, _ => null);
            window.WindowState = WindowState.Maximized;
            window.WindowState = WindowState.Minimized;

            Assert.True(WindowPlacement.IsMaximized(window));
        });

        [Fact]
        public void AWindowMinimizedFromNormalDoesNotCountAsMaximized() => Sta.Run(() =>
        {
            var window = new Window { Left = 40, Top = 50, Width = 640, Height = 480 };
            WindowPlacement.Restore(window, _ => null);
            window.WindowState = WindowState.Minimized;

            Assert.False(WindowPlacement.IsMaximized(window));
        });

        [Fact]
        public void AMaximizedThenRestoredWindowDoesNotCountAsMaximized() => Sta.Run(() =>
        {
            var window = new Window { Left = 40, Top = 50, Width = 640, Height = 480 };
            WindowPlacement.Restore(window, _ => null);
            window.WindowState = WindowState.Maximized;
            window.WindowState = WindowState.Normal;
            window.WindowState = WindowState.Minimized;

            Assert.False(WindowPlacement.IsMaximized(window));
        });
    }
}

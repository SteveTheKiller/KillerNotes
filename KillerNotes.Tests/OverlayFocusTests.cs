using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KillerFamily.Accessibility;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class OverlayFocusTests
    {
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.ApplicationIdle);
            Dispatcher.PushFrame(frame);
        }

        [Fact]
        public void AnOpenCardTakesFocusKeepsItInsideAndReturnsItWhenItCloses() => Sta.Run(() =>
        {
            var behind = new Button { Content = "Behind" };
            var first = new Button { Content = "First" };
            var second = new Button { Content = "Second" };
            var card = new StackPanel { Visibility = Visibility.Collapsed };
            card.Children.Add(first);
            card.Children.Add(second);
            var root = new Grid();
            root.Children.Add(behind);
            root.Children.Add(card);
            var window = new Window
            {
                Content = root, Width = 400, Height = 300, Left = -32000, Top = -32000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            try
            {
                new OverlayFocus(window).Track(card);
                window.Show();
                Pump();
                Keyboard.Focus(behind);
                Assert.Same(behind, Keyboard.FocusedElement);

                card.Visibility = Visibility.Visible;
                Pump();
                Assert.Same(first, Keyboard.FocusedElement);
                Keyboard.Focus(second);
                Assert.Same(second, Keyboard.FocusedElement);
                Keyboard.Focus(behind);
                Pump();
                Assert.True(card.IsKeyboardFocusWithin);
                Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(card));
                Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(card));

                card.Visibility = Visibility.Collapsed;
                Pump();
                Assert.Same(behind, Keyboard.FocusedElement);
            }
            finally { window.Close(); }
        });
    }
}

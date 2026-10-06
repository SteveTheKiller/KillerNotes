using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class DialogShortcutTests
    {
        private static bool Invoke(Button button, ModifierKeys actual, Action action, Func<bool>? allowed = null)
            => KillerNotes.Controls.DialogShortcuts.TryInvoke(button, Key.C, actual, Key.C, ModifierKeys.Alt, action, allowed);

        [Fact]
        public void AltGrAndTextCopyDoNotInvokeDialogAction() => Sta.Run(() =>
        {
            var button = new Button();
            int calls = 0;
            Assert.False(Invoke(button, ModifierKeys.Control | ModifierKeys.Alt, () => calls++));
            Assert.False(Invoke(button, ModifierKeys.Control, () => calls++));
            Assert.True(Invoke(button, ModifierKeys.Alt, () => calls++));
            Assert.Equal(1, calls);
        });

        [Fact]
        public void DisabledHiddenAndUnavailableActionsCannotExecute() => Sta.Run(() =>
        {
            var button = new Button { IsEnabled = false };
            int calls = 0;
            Assert.False(Invoke(button, ModifierKeys.Alt, () => calls++));
            button.IsEnabled = true;
            button.Visibility = Visibility.Collapsed;
            Assert.False(Invoke(button, ModifierKeys.Alt, () => calls++));
            button.Visibility = Visibility.Visible;
            Assert.False(Invoke(button, ModifierKeys.Alt, () => calls++, () => false));
            Assert.Equal(0, calls);
        });

        [Fact]
        public void MenuAndAssistiveTechnologyReceiveSameGesture() => Sta.Run(() =>
        {
            var menu = new MenuItem { Header = "Copy file" };
            KillerNotes.Controls.DialogShortcuts.Describe(menu, "Alt+C");
            menu.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Equal("Alt+C", menu.InputGestureText);
            Assert.Equal("Alt+C", AutomationProperties.GetAcceleratorKey(menu));
            Assert.Equal("Copy file (Alt+C)", menu.ToolTip);
        });
    }
}

using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class DialogShortcutTests
    {
        private sealed class TestPresentationSource : PresentationSource
        {
            public override Visual RootVisual { get; set; } = new DrawingVisual();
            public override bool IsDisposed => false;
            protected override CompositionTarget GetCompositionTargetCore() => null!;
        }
        [Fact]
        public void DatabasePopupRoutesRenameWithoutTouchingDatabaseFiles() => Sta.Run(() =>
        {
            var type = typeof(KillerNotes.Controls.DatabasesDialog);
            var dialog = (KillerNotes.Controls.DatabasesDialog)FormatterServices.GetUninitializedObject(type);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var list = new ListBox();
            list.Items.Add(new ListBoxItem { Tag = "unused.db" });
            list.SelectedIndex = 0;
            type.GetField("DbList", fields)!.SetValue(dialog, list);
            int calls = 0;
            foreach (string name in new[] { "RenameItem", "DeleteItem", "CopyItem", "ExportItem", "RevealItem" })
            {
                var item = new MenuItem();
                if (name == "RenameItem") item.Click += (_, _) => calls++;
                type.GetField(name, fields)!.SetValue(dialog, item);
            }
            // The modifiers are passed in: reading the real keyboard made this fail now and then when
            // a key was down on the machine during a full test run.
            Assert.True(dialog.RouteMenuKey(new ContextMenu(), Key.F2, ModifierKeys.None, isRepeat: false));
            Assert.Equal(1, calls);
            Assert.False(dialog.RouteMenuKey(new ContextMenu(), Key.F2, ModifierKeys.Shift, isRepeat: false));
            Assert.Equal(1, calls);
        });

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

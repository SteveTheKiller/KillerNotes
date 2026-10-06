using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KillerNotes.Controls
{
    internal static class DialogShortcuts
    {
        private static readonly DependencyProperty GestureProperty = DependencyProperty.RegisterAttached(
            "Gesture", typeof(string), typeof(DialogShortcuts));

        internal static void Describe(FrameworkElement control, string gesture)
        {
            AutomationProperties.SetAcceleratorKey(control, gesture);
            if (control.GetValue(GestureProperty) == null)
            {
                object? originalTip = control.ToolTip;
                void Refresh()
                {
                    object? label = originalTip ?? (control as ContentControl)?.Content ?? (control as MenuItem)?.Header;
                    string keys = (string)control.GetValue(GestureProperty);
                    control.ToolTip = label is string text ? $"{text} ({keys})" : keys;
                }
                control.Loaded += (_, _) => Refresh();
                control.ToolTipOpening += (_, _) => Refresh();
                if (control is DialogTitleBar)
                    control.Loaded += (_, _) => DescribeTitleButtons(control, (string)control.GetValue(GestureProperty));
            }
            control.SetValue(GestureProperty, gesture);
            if (control is MenuItem menu) menu.InputGestureText = gesture;
        }

        private static void DescribeTitleButtons(DependencyObject parent, string gesture)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is ButtonBase button) Describe(button, gesture);
                else DescribeTitleButtons(child, gesture);
            }
        }

        internal static void Bind(Window window, FrameworkElement control, string gesture,
                                  Key key, ModifierKeys modifiers, Action action, Func<bool>? canExecute = null)
        {
            Describe(control, gesture);
            window.KeyDown += (_, e) =>
            {
                Key actual = e.Key == Key.System ? e.SystemKey : e.Key;
                if (!e.Handled && TryInvoke(control, actual, Keyboard.Modifiers, key, modifiers, action, canExecute))
                    e.Handled = true;
            };
        }

        internal static bool TryInvoke(FrameworkElement control, Key actual, ModifierKeys actualModifiers,
                                       Key key, ModifierKeys modifiers, Action action, Func<bool>? canExecute = null)
        {
            if (actual != key || actualModifiers != modifiers || !control.IsEnabled ||
                control.Visibility != Visibility.Visible || canExecute?.Invoke() == false) return false;
            action();
            return true;
        }

        internal static void Button(Window window, ButtonBase button, string gesture,
                                    Key key, ModifierKeys modifiers = ModifierKeys.None)
            => Bind(window, button, gesture, key, modifiers,
                    () => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button)));
    }
}

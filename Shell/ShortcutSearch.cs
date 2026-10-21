// ═══════════════════════════════════════════════════════════
//  SHORTCUT SEARCH  -  the F1 list as a command search
// ═══════════════════════════════════════════════════════════
//
// 1.6.0, KN-P09. F1 opens on a search box: typing filters the list by name or keys
// (Services/CommandSearch.cs), Down moves into the rows, and Enter runs the first match or the
// chosen row. Running closes the list and replays the row's shortcut into the window, so a row
// does exactly what its keys do, with the same checks, and no action needs a second entry point.
// Rows for the SketchPad and dialogs are listed but not run: their keys belong to other windows.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private void ShortcutSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // The keyboard picture cannot be filtered; a search shows the list.
            if (ShortcutSearchBox.Text.Length > 0 && ShortcutKeyboardHost.Visibility == Visibility.Visible)
                ApplyShortcutView(keyboard: false);
            BuildShortcutRows();
        }

        /// <summary>Enter and Down in the search box, Enter on a row. Returns true when the key
        /// belongs to the search (typing included, which the box itself then takes).</summary>
        private bool HandleShortcutSearchKey(KeyEventArgs e)
        {
            if (ShortcutSearchBox == null) return false;
            if (Keyboard.Modifiers != ModifierKeys.None) return ShortcutSearchBox.IsKeyboardFocused;
            if (ShortcutSearchBox.IsKeyboardFocused)
            {
                if (e.Key == Key.Enter)
                {
                    var first = ShortcutRows().FirstOrDefault(i => i.Tag is string);
                    if (first != null) RunShortcutRow((string)first.Tag);
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    ShortcutRows().FirstOrDefault(i => i.Focusable && i.IsEnabled)?.Focus();
                    e.Handled = true;
                }
                return true;
            }
            if (e.Key == Key.Enter && Keyboard.FocusedElement is ListBoxItem item && item.Tag is string keys)
            {
                RunShortcutRow(keys);
                e.Handled = true;
                return true;
            }
            return false;
        }

        private System.Collections.Generic.IEnumerable<ListBoxItem> ShortcutRows() =>
            ShortcutColLeft.Items.OfType<ListBoxItem>().Concat(ShortcutColRight.Items.OfType<ListBoxItem>());

        /// <summary>Closes the list and presses the row's keys into the window.</summary>
        private void RunShortcutRow(string keys)
        {
            if (!CommandSearch.TryParseGesture(keys, out var modifiers, out var key))
            {
                StatusText.Text = string.Format(Loc("Str_KS_NotRunnable"), keys);
                return;
            }
            HideShortcutsOverlay();
            ShortcutSearchBox.Text = "";
            // After the overlay has closed and focus is back where it was, so the keys land where
            // they would have if pressed by hand.
            Dispatcher.BeginInvoke(new Action(() => SendGesture(modifiers, key)), DispatcherPriority.Input);
        }

        private static void SendGesture(ModifierKeys modifiers, Key key)
        {
            var mods = new (ModifierKeys Flag, ushort Vk)[]
            {
                (ModifierKeys.Control, 0x11), (ModifierKeys.Shift, 0x10), (ModifierKeys.Alt, 0x12), (ModifierKeys.Windows, 0x5B),
            }.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Vk).ToList();
            ushort vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
            var inputs = mods.Select(m => Input(m, up: false))
                .Append(Input(vk, up: false)).Append(Input(vk, up: true))
                .Concat(Enumerable.Reverse(mods).Select(m => Input(m, up: true))).ToArray();
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        private static INPUT Input(ushort vk, bool up) => new()
        {
            type = 1,   // INPUT_KEYBOARD
            ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 0x0002u : 0u },   // KEYEVENTF_KEYUP
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // The union's largest member is MOUSEINPUT (32 bytes on x64); padding keeps INPUT the size
        // SendInput checks against.
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
            [FieldOffset(8)] private MOUSEPAD pad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEPAD
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}

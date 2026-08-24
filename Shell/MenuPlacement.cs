// ═══════════════════════════════════════════════════════════
//  CONTEXT MENU PLACEMENT UNDER THE APP SCALE
// ═══════════════════════════════════════════════════════════
//
// A ContextMenu defaults to PlacementMode.MousePoint, and WPF resolves that by taking the mouse
// position relative to the PLACEMENT TARGET and pushing it out through that element's ancestor
// transforms. ScaleHost (AppScale.cs) carries a ScaleTransform as its LayoutTransform, and every
// context menu in the app hangs off something inside it - the notes list, the editor, the tag
// rows. So the offset is multiplied by the app scale, and the menu opens further from the cursor
// the further the cursor is from ScaleHost's origin. At 1.2 and a click in the middle of the
// editor that is comfortably an inch (reported 2026-08-23).
//
// The scale is not the bug and must not be "fixed" by scaling the menu back: the menu renders in
// its own popup HWND at the right size already. Only the placement ARITHMETIC is wrong.
//
// So place it in screen space instead, where no transform applies. AbsolutePoint offsets are
// device-independent units on the screen, which is what the mouse position converts to cleanly.
//
// Doing it on Opened rather than ContextMenuOpening is deliberate: the menu has to exist and have
// its own PresentationSource before it can be re-placed, and setting Placement/offsets on an open
// Popup repositions it. Doing it in ContextMenuOpening means guessing at which ContextMenu the
// event belongs to, which is exactly the fiddly part.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        /// <summary>Registered once, for every ContextMenu in the app rather than menu by menu -
        /// there are a dozen of them and a new one must not have to remember this.</summary>
        private static void HookContextMenuPlacement()
        {
            EventManager.RegisterClassHandler(
                typeof(ContextMenu), ContextMenu.OpenedEvent,
                new RoutedEventHandler(ContextMenuOpenedPlacement));
        }

        private static void ContextMenuOpenedPlacement(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            // Only the ones left on the default. A menu that asks for a specific placement - the
            // font size and color popups open Bottom against their button, per the family rule
            // that a flyout anchors to the control that opened it - is deliberate and is left
            // alone.
            if (menu.Placement != PlacementMode.MousePoint) return;

            try
            {
                // Screen position of the cursor, in device-independent units. Mouse.GetPosition
                // needs a visual, and the menu's own popup root is the one visual guaranteed to
                // exist here; its PresentationSource carries the DPI transform to undo.
                var src = PresentationSource.FromVisual(menu);
                if (src?.CompositionTarget is not CompositionTarget ct) return;

                var device = ct.TransformToDevice;
                if (device.M11 == 0 || device.M22 == 0) return;

                // The halo has to come off. AbsolutePoint positions the POPUP's top-left, and the
                // visible card is inset from that by MenuRoot's shadow margin, so placing the
                // popup at the cursor puts the card down and to the right of it by the whole
                // halo. The style's own negative offsets do the same subtraction for menus this
                // handler leaves alone - one number in Controls.xaml, read by both.
                var halo = Application.Current?.TryFindResource("MenuHaloMargin") as Thickness?
                           ?? new Thickness(0);

                Point screen = GetCursorScreenPoint();
                menu.Placement = PlacementMode.AbsolutePoint;
                menu.PlacementTarget = null;
                menu.HorizontalOffset = screen.X / device.M11 - halo.Left;
                menu.VerticalOffset = screen.Y / device.M22 - halo.Top;
            }
            catch (InvalidOperationException)
            {
                // The menu closed underneath us, or has no source yet. A menu in its old place is
                // a great deal better than a menu that threw on the dispatcher.
            }
        }

        /// <summary>The cursor in raw screen pixels. Win32 rather than Mouse.GetPosition against a
        /// visual, because every visual in this window sits under the very transform being worked
        /// around - asking one of them for the mouse position reintroduces the error.</summary>
        private static Point GetCursorScreenPoint()
        {
            return NativeMethods.GetCursorPos(out var p)
                ? new Point(p.X, p.Y)
                : new Point(0, 0);
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.StructLayout(
                System.Runtime.InteropServices.LayoutKind.Sequential)]
            internal struct POINT { public int X; public int Y; }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.Bool)]
            internal static extern bool GetCursorPos(out POINT lpPoint);
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace KillerNotes
{
    // Tilt-wheel horizontal scroll (1.1.3, issue #9). WPF on .NET Framework never
    // surfaces the horizontal wheel: WM_MOUSEHWHEEL reaches the window proc and is
    // dropped without ever becoming a routed event. Hook the proc and hand the delta
    // to the scroller under the mouse - with word wrap off, that's how wide tables
    // and images are reached without touching the scrollbar.
    //
    // Shift+wheel does the same thing for mice without a tilt wheel (the common
    // convention everywhere else); Ctrl+wheel stays editor zoom (Editor.cs).
    public partial class MainWindow
    {
        private const int WM_MOUSEHWHEEL = 0x020E;

        private void InitTiltWheel()
        {
            SourceInitialized += (_, _) =>
            {
                if (PresentationSource.FromVisual(this) is HwndSource src)
                    src.AddHook(TiltWheelHook);
            };
            // Shift+vertical wheel = horizontal scroll, for tilt-less mice.
            Editor.PreviewMouseWheel += (_, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Shift) return;
                if (HorizontalScrollerUnder(Editor) is not ScrollViewer sv) return;
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta * 0.4);
                e.Handled = true;
            };
        }

        private IntPtr TiltWheelHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_MOUSEHWHEEL) return IntPtr.Zero;
            // HIWORD of wParam, signed: positive = tilt right. 120 per notch, but
            // held tilts stream smaller deltas - scale instead of stepping.
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            var sv = HorizontalScrollerUnder(Mouse.DirectlyOver as DependencyObject)
                     ?? (Editor.IsMouseOver ? HorizontalScrollerUnder(Editor) : null);
            if (sv is null) return IntPtr.Zero;
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset + delta * 0.4);
            handled = true;
            return IntPtr.Zero;
        }

        /// <summary>Nearest ScrollViewer at-or-above the element that actually has
        /// horizontal content to scroll. Walks visual parents, crossing content
        /// elements (a Run has no visual parent) via their logical parent.</summary>
        private static ScrollViewer? HorizontalScrollerUnder(DependencyObject? d)
        {
            // The editor itself: dig DOWN to its template ScrollViewer first, since
            // the RichTextBox is what hit-testing usually reports over text.
            if (d is RichTextBox rtb) d = FindScroller(rtb);
            while (d != null)
            {
                if (d is ScrollViewer s && s.ScrollableWidth > 0) return s;
                d = d is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : d is FrameworkContentElement fce ? fce.Parent : null;
            }
            return null;
        }
    }
}

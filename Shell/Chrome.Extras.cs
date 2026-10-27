using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    /// <summary>
    /// APP-OWNED: KillerNotes' side of the family window chrome (Shell/Chrome.cs): the corner
    /// state that squares the window when maximized, snapped or on 98SE and keeps the client edge
    /// in step, the themed frame border (WindowEdgeBrush first), the themed system menu, the
    /// maximize tooltip, F11 fullscreen over the taskbar, and the close fade's flag.
    /// </summary>
    public partial class MainWindow
    {
        // Read and set by the close fade (Notes.EmptyState.cs).
        private bool _closeFadeComplete;

        /// <summary>Call from the constructor right after SourceInitialized is wired to
        /// MainWindow_SourceInitialized, so this runs after the kit's handler.</summary>
        private void InitChromeExtras()
        {
            SourceInitialized += (_, _) =>
            {
                ApplyCornerState();
                ApplyThemeBorder(this);
                // Aero-snap leaves WindowState at Normal, so corner squaring cannot ride
                // OnStateChanged alone - track moves and resizes too. ApplyCornerState caches the
                // last applied value, so the per-pixel storm of a drag costs a bool compare.
                LocationChanged += (_, _) => ApplyCornerState();
                SizeChanged += (_, _) => ApplyCornerState();
                // Added after the kit's WndProc, so it sees the system menu messages first.
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(SystemMenuHook);
            };
        }

        // Swallow the caption right-click and Alt+Space so Windows cannot raise its own stock
        // white HMENU, and show the themed one instead (SystemMenu.cs).
        private IntPtr SystemMenuHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (TryHandleSystemMenu(msg, wParam, lParam)) handled = true;
            return IntPtr.Zero;
        }

        // The same answer ApplyCornerState gives, so the kit's corner call never disagrees with it.
        partial void AdjustRoundedCorners(ref bool rounded)
        {
            if (IsSnappedFlush() || ThemeManager.Current == Theme.SE98) rounded = false;
        }

        partial void AfterWindowStateChanged()
        {
            // Left Maximized by some other route - the caption button, a double-click on the title
            // bar, the system menu - so the window is no longer fullscreen whatever the flag says.
            // Without this the flag survives, and the NEXT ordinary maximize would silently cover
            // the taskbar because WmGetMinMaxInfo is still handing back the monitor rect.
            if (_fullscreen && !_fsSwitching && WindowState != WindowState.Maximized) _fullscreen = false;
            // Square the corners when flush to screen edges, round when floating.
            ApplyCornerState();
            MaximizeBtn?.ToolTip = FindResource(WindowState == WindowState.Maximized ? "Str_Sys_Restore" : "Str_Sys_Maximize");
        }

        // Fullscreen (F11) maximizes over the WHOLE monitor instead of the work area, so the
        // window covers the taskbar. The kit's WmGetMinMaxInfo is the only thing that decides how
        // large a maximized window gets, which is why fullscreen is a change to the clamp rather
        // than a manual Left/Top/Width/Height set - setting the bounds by hand fights that message
        // every time Windows re-asks, which it does on every DPI change, monitor change and restore.
        partial void CoverWholeMonitor(ref bool whole) => whole = _fullscreen;

        // ---- Windows 11 rounded corners (DWMWA_WINDOW_CORNER_PREFERENCE = 33) ----
        // No-op on Windows 10 and earlier; the OS draws the drop shadow for us either way.

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND      = 2;
        private const int DWMWA_BORDER_COLOR = 34;

        /// <summary>Tints the Win11 DWM frame border to the theme's PaneBorderBrush so the
        /// 1px window outline follows the palette instead of staying system gray. Define an
        /// AppBorderBrush in a theme to override the tone (e.g. Black themes, whose pane
        /// borders are intentionally near-invisible). Call at SourceInitialized and after
        /// every theme change. (Family standard.)</summary>
        internal static void ApplyThemeBorder(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                // WindowEdgeBrush FIRST - the same key RootBorder uses, so the OS-drawn frame and
                // the WPF one always agree. This is the outermost thing on screen: DWM paints it
                // outside everything WPF draws, so a theme that hides the WPF edge but not this one
                // still shows a line round the whole window. A fully transparent brush (alpha 0)
                // means "no border", which is how 98SE drops it - its bevel is the window edge.
                if ((Application.Current.TryFindResource("WindowEdgeBrush")
                     ?? Application.Current.TryFindResource("AppBorderBrush")
                     ?? Application.Current.TryFindResource("PaneBorderBrush"))
                    is System.Windows.Media.SolidColorBrush b)
                {
                    // COLORREF is 0x00BBGGRR; DWMWA_COLOR_NONE (0xFFFFFFFE) removes the border.
                    int colorref = b.Color.A == 0
                        ? unchecked((int)0xFFFFFFFE)
                        : b.Color.R | (b.Color.G << 8) | (b.Color.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorref, sizeof(int));
                }
                // NO corner preference here. ApplyCornerState is the ONE owner of the DWM
                // corner: this method runs on every theme change and used to re-assert ROUND
                // with no WindowState check, so switching themes while maximized brought the
                // rounded corners back on a flush window (2026-08-08).
            }
            catch { /* pre-Win11: attribute unsupported */ }
        }

        // ---- Corner state: the ONE owner of the client edge that follows the DWM corner ----
        //
        // Square when the window sits FLUSH with the screen - maximized, or Aero-SNAPPED,
        // which is WindowState.Normal at full work-area height (the same not-Maximized trap
        // KillerShell's DualPane documents) - rounded when floating on a rounded theme.
        // Cached, because LocationChanged fires per pixel of a window drag.
        private bool? _cornersRounded;

        private void ApplyCornerState()
        {
            bool flush = WindowState == WindowState.Maximized || IsSnappedFlush();
            bool rounded = !flush && WindowState == WindowState.Normal && ThemeManager.Current != Theme.SE98;
            if (_cornersRounded == rounded) return;
            _cornersRounded = rounded;
            bool nativeCorners = ApplyWindowCorners(rounded);
            // Keep the client square and let DWM draw the only curved window edge.
            if (RootBorder != null)
            {
                RootBorder.CornerRadius = new CornerRadius(0);
                if (nativeCorners) RootBorder.BorderBrush = Brushes.Transparent;
                else RootBorder.SetResourceReference(
                    System.Windows.Controls.Border.BorderBrushProperty, "WindowEdgeBrush");
            }
            Resources["CaptionCloseCornerRadius"] = new CornerRadius(0);
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        /// <summary>Aero-snapped: the window fills the work area's height flush top and bottom
        /// against one side, while WindowState stays Normal. Compared in device pixels via
        /// GetWindowRect, so DPI never enters into it.</summary>
        private bool IsSnappedFlush()
        {
            if (WindowState != WindowState.Normal) return false;
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return false;
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return false;
                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(monitor, ref info)) return false;
                if (!GetWindowRect(hwnd, out RECT wr)) return false;
                return Math.Abs(wr.top - info.rcWork.top) <= 2
                    && Math.Abs(wr.bottom - info.rcWork.bottom) <= 2
                    && (Math.Abs(wr.left - info.rcWork.left) <= 2
                        || Math.Abs(wr.right - info.rcWork.right) <= 2);
            }
            catch { return false; }
        }

        private bool ApplyWindowCorners(bool rounded)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return false;
                int pref = rounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
                return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0;
            }
            catch { return false; } // pre-Win11: no rounded-corner API
        }

        // ---- Fullscreen (F11) ----
        //
        // PLAIN fullscreen: the chrome stays exactly where it is, the window simply grows to cover
        // the whole monitor including the taskbar. Nothing is hidden, so there is no way to get
        // lost in it - the title bar, the caption buttons and the footer are all still there.

        private bool _fullscreen;
        private WindowState _preFullscreen = WindowState.Normal;

        /// <summary>Set while this file is driving WindowState itself, so AfterWindowStateChanged
        /// can tell a deliberate transition from the user maximizing or restoring by some other
        /// route.</summary>
        private bool _fsSwitching;

        private void ToggleFullscreen()
        {
            if (_fullscreen) LeaveFullscreen();
            else EnterFullscreen();
        }

        private void EnterFullscreen()
        {
            if (_fullscreen) return;
            // Minimized is not a state worth coming back to, so a fullscreen taken from the taskbar
            // restores to Normal.
            _preFullscreen = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            _fullscreen = true;
            // Windows caches the maximized rect, so an already-maximized window would keep its old
            // work-area size and never ask WM_GETMINMAXINFO again. Dropping to Normal first is what
            // forces a fresh transition into Maximized, which is what re-runs the clamp.
            SetStateQuietly(WindowState.Normal);
            SetStateQuietly(WindowState.Maximized);
        }

        private void LeaveFullscreen()
        {
            if (!_fullscreen) return;
            _fullscreen = false;   // cleared FIRST, so the clamp reads the work area again
            SetStateQuietly(WindowState.Normal);
            if (_preFullscreen == WindowState.Maximized) SetStateQuietly(WindowState.Maximized);
        }

        /// <summary>Sets WindowState without AfterWindowStateChanged reading the transition as the
        /// user having left fullscreen by some other route.</summary>
        private void SetStateQuietly(WindowState state)
        {
            _fsSwitching = true;
            try { WindowState = state; }
            finally { _fsSwitching = false; }
        }

        /// <summary>
        /// Publishes the grain tile as the app-level GrainTileBrush. App startup calls this too, so
        /// dialogs shown before any main window exists (install, uninstall, install repair) are
        /// textured like the rest of the app.
        /// </summary>
        internal static BitmapSource PublishGrainTile() => Controls.GrainTexture.Publish();
    }
}

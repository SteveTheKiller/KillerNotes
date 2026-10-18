using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

// ============================================================
// KEEP A FLOATING MAIN WINDOW INSIDE ITS MONITOR'S WORK AREA
//
// A window that opens at its XAML size, or at a size saved on a bigger monitor, can be larger
// than the screen it lands on. A 1920 x 1080 laptop at 150% scaling has about 1280 x 690 DIPs of
// work area, so a 760 DIP tall window centered there loses its footer off the bottom and part of
// its title bar off the top. Fit shrinks such a window to the work area of the monitor it opens
// on and moves it back onto that monitor before it is ever shown.
//
// Rules:
//   - Only a Normal window is touched. A maximized window already fills the work area.
//   - With keepMargin (the default for a window opening CenterScreen, which means no saved
//     position), each side is capped at 90% of the work area so the window opens with a margin.
//     Without it (a restored size and position), each side is capped at the full work area.
//     A window that already fits is not resized.
//   - The window is moved inside the work area. A centered window that had to shrink is
//     re-centered there.
//   - Never smaller than MinWidth / MinHeight, unless the work area itself is smaller. Then the
//     minimum is lowered to the work area so the window can fit at all.
//   - The work area is the monitor's own, not SystemParameters.WorkArea (the primary monitor
//     only): the monitor under the pointer for CenterScreen (where WPF centers a window),
//     otherwise the monitor nearest the window's rectangle. Its size is converted with that
//     monitor's DPI, so a per-monitor aware app sizes for the screen it ends up on.
//
// ---- Wiring it up -------------------------------------------------------------------------
//   1. Copy this file unchanged. The namespace is shared on purpose, like SnapLayouts.
//   2. Call WorkAreaFit.Fit(this) from MainWindow_SourceInitialized. The constructor has
//      restored any saved placement by then, the window has its HWND, and it is not yet shown.
//   3. A window that restores a saved size but still opens CenterScreen passes
//      keepMargin: false when a size was restored, so the user's size is not trimmed to 90%.
//   4. Code that grows the window later (an app scale raising MinWidth) calls
//      Fit(this, keepMargin: false) once the window has its HWND, instead of clamping to
//      SystemParameters.WorkArea, which would drag a window on another monitor to the primary.
// ============================================================
namespace KillerFamily.Windowing
{
    public static class WorkAreaFit
    {
        /// <summary>Share of the work area, each way, that a window opening with a margin may use.</summary>
        public const double MarginFill = 0.9;

        /// <summary>Fits a window, keeping a margin when it opens CenterScreen.</summary>
        public static void Fit(Window window)
            => Fit(window, keepMargin: window.WindowStartupLocation != WindowStartupLocation.Manual);

        /// <summary>Fits a Normal window to the work area of the monitor it opens on.</summary>
        public static void Fit(Window window, bool keepMargin)
        {
            if (window.WindowState != WindowState.Normal) return;
            try
            {
                var source = PresentationSource.FromVisual(window);
                if (source?.CompositionTarget is null) return;
                double sx = source.CompositionTarget.TransformToDevice.M11;
                double sy = source.CompositionTarget.TransformToDevice.M22;
                if (sx <= 0 || sy <= 0) return;

                // CenterScreen only describes where the window first opens. Once it has loaded it
                // sits where the user put it, so a later call (an app scale change) keeps it there.
                bool centered = window.WindowStartupLocation == WindowStartupLocation.CenterScreen
                                && !window.IsLoaded;
                double width = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
                double height = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
                double left = double.IsNaN(window.Left) ? 0 : window.Left;
                double top = double.IsNaN(window.Top) ? 0 : window.Top;
                // Before the first show the properties are the size the window is about to open
                // at. Once it is loaded the HWND is the truth: a window that changed DPI while
                // opening on another monitor can still report its old-DPI Width and Height there.
                if (window.IsLoaded && source is HwndSource hwndSource &&
                    GetWindowRect(hwndSource.Handle, out RECT now) && now.right > now.left && now.bottom > now.top)
                {
                    left = now.left / sx;
                    top = now.top / sy;
                    width = (now.right - now.left) / sx;
                    height = (now.bottom - now.top) / sy;
                }
                if (width <= 0 || height <= 0) return;

                IntPtr monitor;
                if (centered && GetCursorPos(out POINT cursor))
                {
                    monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
                }
                else
                {
                    var r = new RECT
                    {
                        left = (int)Math.Round(left * sx),
                        top = (int)Math.Round(top * sy),
                        right = (int)Math.Round((left + width) * sx),
                        bottom = (int)Math.Round((top + height) * sy),
                    };
                    monitor = MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST);
                }
                if (monitor == IntPtr.Zero) return;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(monitor, ref info)) return;
                RECT work = info.rcWork;

                // Window coordinates follow the window's own DPI; the work area's size follows the
                // monitor's, which differs only when a per-monitor aware window opens elsewhere.
                double dipX = 1 / sx, dipY = 1 / sy;
                if (MonitorDpi(monitor, out double dpiX, out double dpiY))
                {
                    dipX = 96.0 / dpiX;
                    dipY = 96.0 / dpiY;
                }
                var area = new Rect(work.left / sx, work.top / sy,
                                    (work.right - work.left) * dipX, (work.bottom - work.top) * dipY);
                if (area.Width <= 0 || area.Height <= 0) return;

                Rect fitted = FitRect(new Rect(left, top, width, height), area,
                                      window.MinWidth, window.MinHeight, keepMargin, centered);

                if (window.MinWidth > area.Width) window.MinWidth = Math.Floor(area.Width);
                if (window.MinHeight > area.Height) window.MinHeight = Math.Floor(area.Height);

                bool resized = Math.Abs(fitted.Width - width) > 0.5 || Math.Abs(fitted.Height - height) > 0.5;
                if (resized)
                {
                    window.Width = fitted.Width;
                    window.Height = fitted.Height;
                }
                // A centered window that still fits is left to WPF's own centering.
                if (!centered || resized)
                {
                    if (Math.Abs(fitted.X - left) > 0.5 || double.IsNaN(window.Left)) window.Left = fitted.X;
                    if (Math.Abs(fitted.Y - top) > 0.5 || double.IsNaN(window.Top)) window.Top = fitted.Y;
                }
            }
            catch { /* best-effort: a window size is never worth failing startup over */ }
        }

        /// <summary>
        /// The fitted rectangle for <paramref name="window"/> inside <paramref name="area"/>, both
        /// in DIPs. Pure, so the rules can be checked without a monitor of a particular size.
        /// </summary>
        public static Rect FitRect(Rect window, Rect area, double minWidth, double minHeight,
                                   bool keepMargin, bool centered)
        {
            double fill = keepMargin ? MarginFill : 1.0;
            double minW = Math.Min(minWidth, area.Width);
            double minH = Math.Min(minHeight, area.Height);
            double capW = Math.Floor(area.Width * fill);
            double capH = Math.Floor(area.Height * fill);

            double width = window.Width > capW ? Math.Max(minW, capW) : window.Width;
            double height = window.Height > capH ? Math.Max(minH, capH) : window.Height;

            double left, top;
            if (centered)
            {
                left = area.X + (area.Width - width) / 2;
                top = area.Y + (area.Height - height) / 2;
            }
            else
            {
                left = Math.Max(area.X, Math.Min(window.X, area.Right - width));
                top = Math.Max(area.Y, Math.Min(window.Y, area.Bottom - height));
            }
            return new Rect(left, top, width, height);
        }

        private static bool MonitorDpi(IntPtr monitor, out double dpiX, out double dpiY)
        {
            dpiX = dpiY = 0;
            try
            {
                // shcore.dll is Windows 8.1 and later; without it the window's own DPI is used.
                if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint x, out uint y) != 0 || x == 0 || y == 0)
                    return false;
                dpiX = x;
                dpiY = y;
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    }
}

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

// KillerUI kit.
//
// Custom window chrome for a WindowStyle=None window: taskbar-aware maximize,
// caption buttons, Win11 rounded corners, content fade-in, and the procedural
// film-grain texture. This is a partial of your MainWindow.
//
// Your MainWindow.xaml is expected to name these elements (only those you use):
//   RootGrid        - the root Grid, set Opacity="0" so FadeInContent can reveal it
//   MinimizeBtn / MaximizeBtn / CloseBtn - caption buttons (Click= the handlers below)
//   ResizeGrip      - a bottom-right grip element (MouseDown=ResizeGrip_MouseDown)
//   GrainBrush / TitleGrainBrush / ToolbarGrainBrush / StatusGrainBrush / FlyoutGrainBrush
//                   - any ImageBrush layers you want the shared grain painted into
//
// In your MainWindow constructor, call (after InitializeComponent):
//   RestoreWindowPlacement();
//   SourceInitialized += MainWindow_SourceInitialized;
//   ApplyGrainTexture();
//   Loaded += (_, _) => FadeInContent();
//
// Optional: give a toolbar row MouseDown="Toolbar_MouseDown" (plus a non-null Background)
// and its empty space drags the window like the title bar.
namespace KillerNotes
{
    public partial class MainWindow
    {
        // ---- Maximize-respects-taskbar (WindowStyle=None needs WM_GETMINMAXINFO) ----

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            ApplyWindowCorners(rounded: WindowState == WindowState.Normal);
            ApplyThemeBorder(this);
        }

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
                if ((Application.Current.TryFindResource("AppBorderBrush")
                     ?? Application.Current.TryFindResource("PaneBorderBrush"))
                    is System.Windows.Media.SolidColorBrush b)
                {
                    // COLORREF is 0x00BBGGRR
                    int colorref = b.Color.R | (b.Color.G << 8) | (b.Color.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorref, sizeof(int));
                }
            }
            catch { /* pre-Win11: attribute unsupported */ }
        }

        private void ApplyWindowCorners(bool rounded)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int pref = rounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { /* pre-Win11: no rounded-corner API */ }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // Square the corners when maximized (flush to screen edges), round when floating.
            ApplyWindowCorners(rounded: WindowState == WindowState.Normal);
            // Maximize glyph (Segoe MDL2) toggles to a restore glyph when maximized.
            if (MaximizeBtn != null)
                MaximizeBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        }

        // ---- Content fade-in on open (RootGrid starts at Opacity=0 in XAML) ----

        private void FadeInContent() => Anim.FadeIn(RootGrid);

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_ERASEBKGND    = 0x0014;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_ERASEBKGND)
            {
                // KillerPDF's anti-flash trick: WPF paints the whole client area itself, so
                // let nothing erase the background to a flat fill during a resize - that
                // erase is the white flash. Claim the message and report success.
                handled = true;
                return new IntPtr(1);
            }
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                GetMonitorInfo(monitor, ref info);
                RECT work = info.rcWork;
                RECT mon = info.rcMonitor;
                mmi.ptMaxPosition.x = Math.Abs(work.left - mon.left);
                mmi.ptMaxPosition.y = Math.Abs(work.top - mon.top);
                mmi.ptMaxSize.x = Math.Abs(work.right - work.left);
                mmi.ptMaxSize.y = Math.Abs(work.bottom - work.top);
                mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
                mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;
                Marshal.StructureToPtr(mmi, lParam, true);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        // ---- Custom bottom-right resize grip (the WPF CanResizeWithGrip dots fall out in the
        // transparent shadow margin, so we draw our own at the content corner and forward the
        // resize to Windows). ----
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTBOTTOMRIGHT = 17;
        private const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (WindowState != WindowState.Normal) return;
            e.Handled = true;
            var hwnd = new WindowInteropHelper(this).Handle;
            SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTBOTTOMRIGHT), IntPtr.Zero);
        }

        // Lets a toolbar act like the title bar for dragging: interactive controls handle their
        // own clicks, so only clicks on the bar's empty space and passive labels bubble up here
        // and forward a native caption drag. Native HTCAPTION also gives correct
        // restore-from-maximized-and-drag behavior for free. Wire via MouseDown="Toolbar_MouseDown"
        // on the bar (it needs a non-null Background to hit-test).
        private void Toolbar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            var hwnd = new WindowInteropHelper(this).Handle;
            SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // ---- Window placement persistence ("WindowPlacement" = left,top,w,h,max) ----
        // Persists through the same pluggable settings seam as the theme (Services.ThemeManager
        // GetSetting/SetSetting), so it works as soon as those are wired. A maximized (or
        // minimized) close saves RestoreBounds, so the pre-maximize size comes back. Restore
        // sanity-checks that the saved rect still lands on the current virtual desktop
        // (monitors change). Call RestoreWindowPlacement() in the constructor, before Show.

        private void SaveWindowPlacement()
        {
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                bool max = WindowState == WindowState.Maximized;
                Rect r = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;
                if (r.IsEmpty || r.Width < 1 || r.Height < 1 ||
                    double.IsNaN(r.X) || double.IsNaN(r.Y)) return;
                Services.ThemeManager.SetSetting("WindowPlacement", string.Join(",",
                    r.X.ToString("0.##", inv), r.Y.ToString("0.##", inv),
                    r.Width.ToString("0.##", inv), r.Height.ToString("0.##", inv),
                    max ? "1" : "0"));
            }
            catch { /* best-effort */ }
        }

        private void RestoreWindowPlacement()
        {
            string? s = Services.ThemeManager.GetSetting("WindowPlacement");
            if (string.IsNullOrWhiteSpace(s)) return;
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string[] f = s!.Split(',');
                if (f.Length != 5) return;
                if (!double.TryParse(f[0], System.Globalization.NumberStyles.Float, inv, out double left) ||
                    !double.TryParse(f[1], System.Globalization.NumberStyles.Float, inv, out double top)  ||
                    !double.TryParse(f[2], System.Globalization.NumberStyles.Float, inv, out double w)    ||
                    !double.TryParse(f[3], System.Globalization.NumberStyles.Float, inv, out double h))
                    return;
                w = Math.Max(MinWidth, w);
                h = Math.Max(MinHeight, h);

                // Keep at least a grabbable sliver of the title bar on-screen.
                double vl = SystemParameters.VirtualScreenLeft;
                double vt = SystemParameters.VirtualScreenTop;
                double vr = vl + SystemParameters.VirtualScreenWidth;
                double vb = vt + SystemParameters.VirtualScreenHeight;
                if (left + w < vl + 40 || left > vr - 40 || top < vt - 8 || top > vb - 40) return;

                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left; Top = top; Width = w; Height = h;
                if (f[4] == "1") WindowState = WindowState.Maximized;
            }
            catch { /* best-effort */ }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveWindowPlacement();
            base.OnClosed(e);
        }

        // ---- Caption buttons (drag + double-click-maximize are native via WindowChrome) ----

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
            => Close();

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        // ---- Film grain ----
        // A mix of bright AND dark specks (~33% density) so the texture reads on both dark and
        // light themes. One bitmap, shared across every named grain brush and the keyed
        // GrainTileBrush resource (used by the context menus). Same seed = identical pattern.

        private void ApplyGrainTexture()
        {
            const int size = 256;
            var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[size * size * 4]; // start fully transparent
            var rng = new Random(1337);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (rng.Next(3) != 0) continue;        // ~33% pixel density
                bool bright = rng.Next(2) == 0;         // half bright, half dark
                byte v = bright ? (byte)rng.Next(190, 255) : (byte)rng.Next(0, 50);
                byte a = (byte)rng.Next(35, 95);        // alpha for subtlety
                pixels[i]     = v;
                pixels[i + 1] = v;
                pixels[i + 2] = v;
                pixels[i + 3] = a;
            }
            bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);

            // Paint into whichever named grain brushes exist in your XAML (all optional).
            foreach (var name in new[] { "GrainBrush", "TitleGrainBrush", "ToolbarGrainBrush", "StatusGrainBrush", "FlyoutGrainBrush" })
                if (FindName(name) is ImageBrush ib) ib.ImageSource = bmp;

            // The keyed resource brush is auto-frozen, so its ImageSource can't be set in place.
            // Swap in a fresh frozen brush - DynamicResource consumers re-resolve automatically.
            var grainTile = new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new System.Windows.Rect(0, 0, size, size),
                Stretch = Stretch.None
            };
            grainTile.Freeze();
            Application.Current.Resources["GrainTileBrush"] = grainTile;
        }
    }
}

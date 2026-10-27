using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using KillerFamily.Windowing;
using KillerNotes.Controls;

// Window-message plumbing for a WindowStyle="None" window, and the caption buttons. This stays a
// partial of MainWindow on purpose: every method here needs the window's own HWND, its WindowState
// or its named caption buttons, so there is nothing to inject and nothing to test in isolation.
//
// The pieces that were NOT window-specific now live as real classes:
//   Controls/DwmChrome        - rounded corners and the themed frame border
//   Controls/WindowPlacement  - size/position persistence
//   Controls/GrainTexture     - the shared film-grain tile
//   Controls/SnapLayouts      - Windows 11 snap layouts on the maximize button
//   Controls/WorkAreaFit      - keeps the opening window inside its monitor's work area
//
// MainWindow.xaml is expected to name: RootGrid (Opacity="0" so FadeInContent can reveal it),
// MinimizeBtn / MaximizeBtn / CloseBtn, ResizeGrip, and any *GrainBrush layers.
namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        // Grain brush layers in MainWindow.xaml. All optional - Apply skips the ones absent.
        private static readonly string[] GrainBrushNames =
            { "GrainBrush", "TitleGrainBrush", "ToolbarGrainBrush", "StatusGrainBrush", "FlyoutGrainBrush" };

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            // Windows 11 snap layouts flyout on the maximize button. Added after WindowChrome's
            // hook, so it answers WM_NCHITTEST first.
            SnapLayouts.Attach(this, MaximizeBtn);
            BeforeWorkAreaFit();
            // After RestoreWindowPlacement (constructor) and before the first show: a default or
            // saved size larger than this monitor's work area is shrunk and moved onto it.
            WorkAreaFit.Fit(this);
            DwmChrome.SetRoundedCorners(this, rounded: RoundedCornersWanted());
            DwmChrome.SetThemeBorder(this);
        }

        private void ApplyGrainTexture() => GrainTexture.Apply(this, GrainBrushNames);

        /// <summary>Content fade-in on open; RootGrid starts at Opacity="0" in XAML.</summary>
        private void FadeInContent() => Anim.FadeIn(RootGrid);

        private void RestoreWindowPlacement()
            => WindowPlacement.Restore(this, Services.ThemeManager.GetSetting);

        protected override void OnClosed(EventArgs e)
        {
            bool savePlacement = true;
            AdjustSaveWindowPlacement(ref savePlacement);
            if (savePlacement) WindowPlacement.Save(this, Services.ThemeManager.SetSetting);
            base.OnClosed(e);
        }

        /// <summary>App hook: runs in SourceInitialized just before WorkAreaFit, for an app that
        /// restores its own saved bounds once the window has a handle.</summary>
        partial void BeforeWorkAreaFit();

        /// <summary>App hook: set <paramref name="save"/> to false for an app that keeps its own
        /// window bounds instead of the kit's WindowPlacement setting.</summary>
        partial void AdjustSaveWindowPlacement(ref bool save);

        /// <summary>App hook: set <paramref name="whole"/> to true while the window should maximize
        /// over the whole monitor, taskbar included (a fullscreen mode), instead of the work area.</summary>
        partial void CoverWholeMonitor(ref bool whole);

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // Square the corners when maximized (flush to the screen edges), round when floating.
            DwmChrome.SetRoundedCorners(this, rounded: RoundedCornersWanted());
            // Maximize glyph (Segoe MDL2) toggles to a restore glyph when maximized.
            if (MaximizeBtn != null)
                MaximizeBtn.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            AfterWindowStateChanged();
        }

        private bool RoundedCornersWanted()
        {
            bool rounded = WindowState == WindowState.Normal;
            AdjustRoundedCorners(ref rounded);
            return rounded;
        }

        /// <summary>App hook: change whether the window asks DWM for rounded corners (square on a
        /// flat-caption theme such as 98SE).</summary>
        partial void AdjustRoundedCorners(ref bool rounded);

        /// <summary>App hook: runs after the kit's own state-change work, for example to restore
        /// to maximized after a minimize.</summary>
        partial void AfterWindowStateChanged();

        // ---- window messages ----

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_ERASEBKGND    = 0x0014;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const int HTBOTTOMRIGHT = 17;
        private const int HTCAPTION = 2;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_ERASEBKGND)
            {
                // WPF paints the whole client area itself, so nothing should erase the background to
                // a flat fill during a resize - that erase IS the white flash. Claim the message.
                handled = true;
                return new IntPtr(1);
            }
            if (msg == WM_GETMINMAXINFO)
            {
                // A WindowStyle="None" window maximizes over the taskbar unless we clamp it to the
                // monitor's work area ourselves.
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(monitor, ref info);
            bool whole = false;
            CoverWholeMonitor(ref whole);
            RECT mon = info.rcMonitor;
            RECT work = whole ? mon : info.rcWork;

            mmi.ptMaxPosition.x = Math.Abs(work.left - mon.left);
            mmi.ptMaxPosition.y = Math.Abs(work.top - mon.top);
            mmi.ptMaxSize.x = Math.Abs(work.right - work.left);
            mmi.ptMaxSize.y = Math.Abs(work.bottom - work.top);
            mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
            mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;

            // ptMinTrackSize MUST be set here too. This handler marks the message handled, which
            // takes WM_GETMINMAXINFO away from WPF - and that message is exactly how WPF enforces
            // MinWidth/MinHeight during a drag-resize. Leave these at the OS default and the window
            // can be dragged far below its minimum, at which point fixed chrome no longer fits and
            // the content is CLIPPED rather than resized. (2026-07-30) KillerShell always had
            // this; the kit did not, so KillerNotes, KillerScan, KillerPDF and Killendar all
            // inherited the bug.
            //
            // MINMAXINFO is in DEVICE pixels and MinWidth/MinHeight are DIPs, so go through the
            // window's own transform or the limit is wrong on any display not at 100%.
            var src = PresentationSource.FromVisual(this);
            double sx = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double sy = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            if (!double.IsNaN(MinWidth) && MinWidth > 0)
                mmi.ptMinTrackSize.x = Math.Min(mmi.ptMaxSize.x, (int)Math.Ceiling(MinWidth * sx));
            if (!double.IsNaN(MinHeight) && MinHeight > 0)
                mmi.ptMinTrackSize.y = Math.Min(mmi.ptMaxSize.y, (int)Math.Ceiling(MinHeight * sy));

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Our own grip: WPF's CanResizeWithGrip dots land out in the transparent shadow
        /// margin, so we draw one at the content corner and forward the resize to Windows.</summary>
        private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (WindowState != WindowState.Normal) return;
            e.Handled = true;
            SendMessage(new WindowInteropHelper(this).Handle,
                        WM_NCLBUTTONDOWN, new IntPtr(HTBOTTOMRIGHT), IntPtr.Zero);
        }

        /// <summary>Lets the toolbar drag the window like the title bar. Interactive controls handle
        /// their own clicks, so only the bar's empty space bubbles up here. Native HTCAPTION also
        /// gives correct restore-from-maximized-and-drag behavior for free.</summary>
        private void Toolbar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            SendMessage(new WindowInteropHelper(this).Handle,
                        WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
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

        // ---- caption buttons (drag and double-click-maximize are native via WindowChrome) ----

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}

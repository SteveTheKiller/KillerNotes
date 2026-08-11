using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerNotes.Shell
{
    // App-wide accessibility size: a LayoutTransform scale on ScaleHost (the sidebar + editor
    // row) grows or shrinks the app content crisply - LayoutTransform reflows and re-rasterizes
    // text rather than bitmap-stretching it. The title bar and footer stay fixed, so the
    // KillerNotes logo you scroll to drive this (MainWindow.xaml, LogoBar) never moves. Driven
    // by rolling the wheel over that logo, in fine steps. Persisted app-wide ("AppScale").
    // Separate from the per-note Ctrl+wheel editor zoom (Editor.cs), which only scales the note body.
    public partial class MainWindow
    {
        private double _appScale = 1.0;
        private const double AppScaleMin = 0.7, AppScaleMax = 2.5, AppScaleStep = 0.02;

        private void InitAppScale()
        {
            if (double.TryParse(App.GetSetting("AppScale"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double s))
                ApplyAppScale(s);
        }

        // Roll the wheel over the logo: one small step per notch (fine-grained, no big jumps).
        private void AppSizeBtn_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ApplyAppScale(_appScale + (e.Delta > 0 ? AppScaleStep : -AppScaleStep), persist: true);
            e.Handled = true;
        }

        // The logo is marked IsHitTestVisibleInChrome (MainWindow.xaml) so the scroll wheel
        // reaches it for the zoom above - but that also takes it out of WindowChrome's native
        // caption, so window drag and double-click-maximize are restored here by hand.
        private void LogoBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeBtn_Click(this, new RoutedEventArgs());   // Chrome.cs
                e.Handled = true;
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed && WindowState != WindowState.Maximized)
                DragMove();
        }

        private void ApplyAppScale(double scale, bool persist = false)
        {
            scale = Math.Round(Math.Max(AppScaleMin, Math.Min(AppScaleMax, scale)), 3);
            // Capture the sidebar's current on-screen width before the scale changes (this
            // respects any splitter drag the user made), so RefreshSidebarWidth can hold that
            // same on-screen width at the new scale (Sidebar.cs).
            if (!_sidebarCollapsed && SidebarCol.ActualWidth > 0)
                _sidebarBaseWidth = SidebarCol.ActualWidth * _appScale;
            _appScale = scale;
            ScaleHost.LayoutTransform = scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);
            // Layout rounding OFF while the scale is fractional - the window sets
            // UseLayoutRounding=True, and rounded child positions multiplied by a fractional
            // scale land SHORT of the row at some scales and not others: a backdrop stripe
            // between the editor and the footer, the last text line clipped, and the footer
            // cast swallowed, all varying with the zoom step - at 108% a stripe along the
            // bottom cut off words (2026-08-08). Sub-pixel layout under a
            // transform is exactly what rounding-off is for; at 1.0 it comes back on.
            ScaleHost.UseLayoutRounding = scale == 1.0;
            RefreshSidebarWidth();   // Sidebar.cs: panel keeps its on-screen width; the rail scales with the app
            RebuildLineNumbers();    // LineNumbers.cs: gutter numbers track the app zoom
            if (persist)
            {
                App.SetSetting("AppScale", scale.ToString("0.###", CultureInfo.InvariantCulture));
                ShowScaleReadout(scale);
            }
        }

        // The readout is transient, on the same five second hold the rest of the family uses.
        // It runs on its own timer rather than FlashStatus, whose _statusTimer is six seconds
        // and is shared by every other confirmation in the app (drag-ready, tag toggled,
        // shared...) - those keep their six seconds. Landing back on DefaultStatus is the same
        // ending FlashStatus gives, so nothing else about the status line changes. Any pending
        // flash is canceled on the way in, since its message is being overwritten regardless.
        //
        // Normal priority rather than the DispatcherTimer default of Background, so a busy
        // moment cannot leave the readout parked on the footer.
        private System.Windows.Threading.DispatcherTimer? _appScaleHide;

        private void ShowScaleReadout(double scale)
        {
            if (_appScaleHide is null)
            {
                _appScaleHide = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Normal)
                    { Interval = TimeSpan.FromSeconds(5) };
                _appScaleHide.Tick += (_, _) =>
                {
                    _appScaleHide!.Stop();
                    if (Services.NoteStore.IsOpen) StatusText.Text = DefaultStatus();
                };
            }

            _statusTimer.Stop();
            _appScaleHide.Stop();
            StatusText.Text = string.Format(Loc("Str_St_AppSize"), (int)Math.Round(scale * 100));
            _appScaleHide.Start();
        }
    }
}

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerNotes
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
            RefreshSidebarWidth();   // Sidebar.cs: keep the sidebar + rail on-screen width fixed
            RebuildLineNumbers();    // LineNumbers.cs: gutter numbers track the app zoom
            if (persist)
            {
                App.SetSetting("AppScale", scale.ToString("0.###", CultureInfo.InvariantCulture));
                FlashStatus(string.Format(Loc("Str_St_AppSize"), (int)Math.Round(scale * 100)));
            }
        }
    }
}

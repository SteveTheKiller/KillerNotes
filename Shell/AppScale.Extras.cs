using System;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerNotes.Shell
{
    // APP-OWNED: KillerNotes' side of the family app size (Shell/AppScale.cs). The kit keeps the
    // range, step, smooth text and wheel handling; this file adds what KillerNotes scales with it.
    // Separate from the per-note Ctrl+wheel editor zoom (Editor.cs), which only scales the note body.
    public partial class MainWindow
    {
        // Registered with the scale because the transform is what displaces MousePoint
        // placement. MenuPlacement.cs has the detail.
        private static bool _menuPlacementHooked;

        // The scale the content was last laid out at. The kit has already stored the new scale in
        // _appScale when ApplyAppScaleTo runs, so the sidebar's old on-screen width is read with this.
        private double _appScaleApplied = 1.0;

        partial void ConfigureAppScale(ref double min, ref double max, ref double step, ref bool smoothText)
        {
            if (!_menuPlacementHooked) { _menuPlacementHooked = true; HookContextMenuPlacement(); }
        }

        // Layout rounding OFF while the scale is fractional: rounded child positions multiplied
        // by a fractional scale land short of the row at some steps, leaving a backdrop stripe
        // between the editor and the footer (108%, 2026-08-08). At 1.0 it comes back on.
        partial void ConfigureAppScaleLayout(ref bool unroundedLayout) => unroundedLayout = true;

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Wheel events can target the focused editor or list instead of the hovered
            // control. Resolve logo zoom and sidebar density before those controls scroll.
            if (LogoBar.IsMouseOver)
            {
                if (e.Delta != 0) LogoBar_MouseWheel(sender, e);
            }
            else if (SidebarHost.IsMouseOver && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                Density_MouseWheel(sender, e);
        }

        partial void ApplyAppScaleTo(double scale, ref bool handled)
        {
            handled = true;
            // Capture the sidebar's current on-screen width before the scale changes (this
            // respects any splitter drag the user made), so RefreshSidebarWidth can hold that
            // same on-screen width at the new scale (Sidebar.cs).
            if (!_sidebarCollapsed && SidebarCol.ActualWidth > 0)
                _sidebarBaseWidth = SidebarCol.ActualWidth * _appScaleApplied;
            _appScaleApplied = scale;
            ScaleHost.LayoutTransform = scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);
            RefreshSidebarWidth();   // Sidebar.cs: panel keeps its on-screen width; the rail scales with the app
            RebuildLineNumbers();    // LineNumbers.cs: gutter numbers track the app zoom
        }

        // The readout holds five seconds on its own timer rather than FlashStatus, whose six
        // second _statusTimer is shared by every other confirmation, and lands back on
        // DefaultStatus. Normal priority, so a busy moment cannot park it on the footer.
        private System.Windows.Threading.DispatcherTimer? _appScaleReadoutHide;

        partial void ShowAppScaleReadout(double scale, ref bool handled)
        {
            handled = true;
            if (_appScaleReadoutHide is null)
            {
                _appScaleReadoutHide = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Normal)
                    { Interval = TimeSpan.FromSeconds(5) };
                _appScaleReadoutHide.Tick += (_, _) =>
                {
                    _appScaleReadoutHide!.Stop();
                    if (Services.NoteStore.IsOpen) StatusText.Text = DefaultStatus();
                };
            }

            _statusTimer.Stop();
            _appScaleReadoutHide.Stop();
            StatusText.Text = string.Format(Loc("Str_St_AppSize"), (int)Math.Round(scale * 100));
            _appScaleReadoutHide.Start();
        }
    }
}

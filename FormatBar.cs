using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace KillerNotes
{
    // Floating format bar, ported from KillerPDF's annotation bars (AnnotationBars.cs /
    // WindowChrome.cs / SettingsPanel.cs): pops out of the top of the editor pane on
    // first show, slides horizontally by its grip (edge-anchored or center-parked,
    // placement persisted), and minimizes to a grip-dots peek strip with the same
    // 120ms height animation. Double-click the grip or peek, the chevron button, or
    // F6 all toggle minimized.
    public partial class MainWindow
    {
        private bool _fmtMinimized;
        private double _fmtFullHeight;
        private double? _fmtGap;                 // px from the anchored edge
        private bool _fmtAnchorRight = true;     // which edge _fmtGap measures from
        private double? _fmtCenterFrac;          // parked mid-pane: fraction of the free width
        private bool _fmtShownOnce;              // pop-in runs once per launch
        private (double StartX, double OrigLeft)? _fmtDrag;

        private const double FmtPeekHeight = 13;
        private const double FmtEdgeGapDefault = 8;
        private const double FmtEdgeThreshold = 40;   // release this close to an edge = anchor to it

        private void InitFormatBar()
        {
            var inv = CultureInfo.InvariantCulture;
            if (double.TryParse(App.GetSetting("FmtBarFrac"), NumberStyles.Float, inv, out double f) &&
                f >= 0 && f <= 1)
                _fmtCenterFrac = f;
            if (int.TryParse(App.GetSetting("FmtBarGap"), out int g) && g >= 0) _fmtGap = g;
            _fmtAnchorRight = App.GetSetting("FmtBarRightSide") != "0";

            EnableFmtBarSlide(FmtGrip);
            EnableFmtBarSlide(FmtPeek);
            // The bar floats inside the editor pane, so reposition on pane resize
            // (covers both window resizes and sidebar-splitter drags).
            if (FormatBar.Parent is FrameworkElement hostEl)
                hostEl.SizeChanged += (_, _) => PositionFormatBar();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                PositionFormatBar();
                if (App.GetSetting("FmtBarMin") == "1") ToggleFormatBar(animate: false);
            }), DispatcherPriority.Loaded);
        }

        // ---- Pop-in (KillerPDF: the bar drops out of the top edge when a note opens) ----

        private void PopInFormatBar()
        {
            if (_fmtShownOnce) return;
            _fmtShownOnce = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                double h = FormatBar.ActualHeight;
                if (h <= 0) h = 40;
                var tt = new TranslateTransform(0, -(h + 10));
                FormatBar.RenderTransform = tt;
                var a = new DoubleAnimation(-(h + 10), 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                a.Completed += (_, _) => FormatBar.RenderTransform = null;
                tt.BeginAnimation(TranslateTransform.YProperty, a);
            }), DispatcherPriority.Loaded);
        }

        // ---- Horizontal slide (grip drag; KillerPDF EnableBarSlide) ----

        private void EnableFmtBarSlide(FrameworkElement grip)
        {
            grip.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { ToggleFormatBar(); e.Handled = true; return; }
                if (FormatBar.Parent is not FrameworkElement host) return;
                double left = FormatBar.HorizontalAlignment == HorizontalAlignment.Right
                    ? host.ActualWidth - FormatBar.ActualWidth - FormatBar.Margin.Right
                    : FormatBar.Margin.Left;
                _fmtDrag = (e.GetPosition(host).X, left);
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (s, e) =>
            {
                if (_fmtDrag == null || !grip.IsMouseCaptured) return;
                if (FormatBar.Parent is not FrameworkElement host) return;
                var (startX, origLeft) = _fmtDrag.Value;
                double free = Math.Max(0, host.ActualWidth - FormatBar.ActualWidth);
                double left = Math.Max(0, Math.Min(free, origLeft + e.GetPosition(host).X - startX));
                FormatBar.HorizontalAlignment = HorizontalAlignment.Left;
                FormatBar.Margin = new Thickness(left, FormatBar.Margin.Top, 0, 0);
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (_fmtDrag == null) return;
                _fmtDrag = null;
                grip.ReleaseMouseCapture();
                if (FormatBar.Parent is not FrameworkElement host) return;
                double free = Math.Max(0, host.ActualWidth - FormatBar.ActualWidth);
                double left = Math.Max(0, Math.Min(free, FormatBar.Margin.Left));
                if (left <= FmtEdgeThreshold)
                { _fmtAnchorRight = false; _fmtGap = left; _fmtCenterFrac = null; }
                else if (free - left <= FmtEdgeThreshold)
                { _fmtAnchorRight = true; _fmtGap = free - left; _fmtCenterFrac = null; }
                else
                { _fmtCenterFrac = free > 0 ? left / free : 0.5; }
                SaveFmtBarPlacement();
                PositionFormatBar();
                e.Handled = true;
            };
        }

        private void SaveFmtBarPlacement()
        {
            var inv = CultureInfo.InvariantCulture;
            if (_fmtCenterFrac is double f)
                App.SetSetting("FmtBarFrac", f.ToString("0.####", inv));
            else
            {
                App.RemoveSetting("FmtBarFrac");
                App.SetSetting("FmtBarGap",
                    ((int)Math.Round(_fmtGap ?? FmtEdgeGapDefault)).ToString(inv));
                App.SetSetting("FmtBarRightSide", _fmtAnchorRight ? "1" : "0");
            }
        }

        /// <summary>Applies the persisted placement: edge-anchored (gap px off the left or
        /// right edge) or center-parked (fraction of the free width). Called on init, on
        /// pane resize, and after a drag settles.</summary>
        private void PositionFormatBar()
        {
            if (FormatBar.Parent is not FrameworkElement host) return;
            double bw = FormatBar.ActualWidth, hw = host.ActualWidth;
            double top = FormatBar.Margin.Top;
            if (_fmtCenterFrac is double cf && bw > 0 && hw > bw)
            {
                double left = Math.Max(0, Math.Min(hw - bw, cf * (hw - bw)));
                FormatBar.HorizontalAlignment = HorizontalAlignment.Left;
                FormatBar.Margin = new Thickness(left, top, 0, 0);
            }
            else
            {
                double gap = _fmtGap ?? FmtEdgeGapDefault;
                if (bw > 0 && hw > bw) gap = Math.Max(0, Math.Min(gap, hw - bw));
                FormatBar.HorizontalAlignment =
                    _fmtAnchorRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                FormatBar.Margin = _fmtAnchorRight
                    ? new Thickness(0, top, gap, 0)
                    : new Thickness(gap, top, 0, 0);
            }
        }

        // ---- Minimize / restore (KillerPDF ToggleAnnotBarMinimized, 120ms) ----

        private void ToggleFormatBar(bool animate = true)
        {
            double minH = FmtPeekHeight
                + FormatBar.Padding.Top + FormatBar.Padding.Bottom
                + FormatBar.BorderThickness.Top + FormatBar.BorderThickness.Bottom;
            _fmtMinimized = !_fmtMinimized;

            if (_fmtMinimized)
            {
                _fmtFullHeight = FormatBar.ActualHeight;
                if (FormatBar.ActualWidth > 0) FormatBar.Width = FormatBar.ActualWidth;  // keep footprint
                FormatBar.ClipToBounds = true;
                FormatBar.Effect = null;                       // shadow off while minimized
                FmtButtons.Visibility = Visibility.Collapsed;
                FmtPeek.Visibility = Visibility.Visible;
                if (animate && _fmtFullHeight > 0)
                {
                    var a = new DoubleAnimation(_fmtFullHeight, minH, TimeSpan.FromMilliseconds(120))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                    FormatBar.BeginAnimation(HeightProperty, a);
                }
                else FormatBar.Height = minH;
            }
            else
            {
                FmtPeek.Visibility = Visibility.Collapsed;
                FmtButtons.Visibility = Visibility.Visible;
                FormatBar.Effect = TryFindResource("ShadowBar") as Effect;
                void Restore()
                {
                    FormatBar.BeginAnimation(HeightProperty, null);
                    FormatBar.Height = double.NaN;
                    FormatBar.Width = double.NaN;
                    FormatBar.ClipToBounds = false;
                    PositionFormatBar();
                }
                if (animate && _fmtFullHeight > minH)
                {
                    var a = new DoubleAnimation(minH, _fmtFullHeight, TimeSpan.FromMilliseconds(120))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    a.Completed += (_, _) => Restore();
                    FormatBar.BeginAnimation(HeightProperty, a);
                }
                else Restore();
            }
            App.SetSetting("FmtBarMin", _fmtMinimized ? "1" : "0");
        }

        private void FormatBarToggle_Click(object sender, RoutedEventArgs e) => ToggleFormatBar();
    }
}

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
        private DateTime _fmtPeekRestoredAt = DateTime.MinValue;   // swallows the double-click tail

        private const double FmtPeekHeight = 9;
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
            EnableFmtBarSlide(FmtPeekHalo);   // the grab pixels above the strip act as the strip
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
            bool dragging = false;   // true only after real movement (per-grip closure state)
            grip.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    // Swallow the tail of a double-click on the peek: the first click
                    // already restored the bar, so this second press must not minimize
                    // it right back.
                    if ((DateTime.Now - _fmtPeekRestoredAt).TotalMilliseconds > 500)
                        ToggleFormatBar();
                    e.Handled = true; return;
                }
                if (FormatBar.Parent is not FrameworkElement host) return;
                double left = FormatBar.HorizontalAlignment == HorizontalAlignment.Right
                    ? host.ActualWidth - FormatBar.ActualWidth - FormatBar.Margin.Right
                    : FormatBar.Margin.Left;
                dragging = false;
                _fmtDrag = (e.GetPosition(host).X, left);
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (s, e) =>
            {
                if (_fmtDrag == null || !grip.IsMouseCaptured) return;
                if (FormatBar.Parent is not FrameworkElement host) return;
                var (startX, origLeft) = _fmtDrag.Value;
                double dx = e.GetPosition(host).X - startX;
                // A plain click must never reposition the bar (a pixel of press wiggle used
                // to re-anchor a right-docked bar to the LEFT edge): moving starts only
                // past the drag threshold.
                if (!dragging && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance) return;
                dragging = true;
                double free = Math.Max(0, host.ActualWidth - FormatBar.ActualWidth);
                double left = Math.Max(0, Math.Min(free, origLeft + dx));
                FormatBar.HorizontalAlignment = HorizontalAlignment.Left;
                FormatBar.Margin = new Thickness(left, FormatBar.Margin.Top, 0, 0);
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (_fmtDrag == null) return;
                _fmtDrag = null;
                grip.ReleaseMouseCapture();
                e.Handled = true;
                if (!dragging)
                {
                    // Plain click, no drag: on the slim peek strip (or its halo) that
                    // restores the bar (a 9px double-click target is too fiddly); on the
                    // expanded grip it does nothing. F6 and the chevron still toggle.
                    if ((grip == FmtPeek || grip == FmtPeekHalo) && _fmtMinimized)
                    {
                        ToggleFormatBar();
                        _fmtPeekRestoredAt = DateTime.Now;
                    }
                    return;
                }
                dragging = false;
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
            // Minimized, the whole strip must fit inside the title's 10px top margin so it
            // never overlaps the title text: 9px peek + 1px bottom border, vertical padding
            // dropped for the duration.
            double minH = FmtPeekHeight
                + FormatBar.BorderThickness.Top + FormatBar.BorderThickness.Bottom;
            _fmtMinimized = !_fmtMinimized;

            if (_fmtMinimized)
            {
                _fmtFullHeight = FormatBar.ActualHeight;
                if (FormatBar.ActualWidth > 0) FormatBar.Width = FormatBar.ActualWidth;  // keep footprint
                FormatBar.ClipToBounds = true;
                FormatBar.Effect = null;                       // shadow off while minimized
                FormatBar.Padding = new Thickness(4, 0, 4, 0);
                FmtButtons.Visibility = Visibility.Collapsed;
                FmtPeek.Visibility = Visibility.Visible;
                // Once settled, the clip comes OFF so the negative-margin halo above the
                // strip is hit-testable (it sits outside the bar's bounds).
                void Settle()
                {
                    if (!_fmtMinimized) return;   // user re-expanded mid-animation
                    FormatBar.ClipToBounds = false;
                    FmtPeekHalo.Visibility = Visibility.Visible;
                }
                if (animate && _fmtFullHeight > 0)
                {
                    var a = new DoubleAnimation(_fmtFullHeight, minH, TimeSpan.FromMilliseconds(120))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                    a.Completed += (_, _) => Settle();
                    FormatBar.BeginAnimation(HeightProperty, a);
                }
                else { FormatBar.Height = minH; Settle(); }
            }
            else
            {
                FmtPeekHalo.Visibility = Visibility.Collapsed;
                FormatBar.ClipToBounds = true;   // clip the buttons while the expand animates
                FmtPeek.Visibility = Visibility.Collapsed;
                FmtButtons.Visibility = Visibility.Visible;
                FormatBar.Effect = TryFindResource("ShadowBar") as Effect;
                FormatBar.Padding = new Thickness(4, 2, 4, 2);   // restore before the expand animates
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

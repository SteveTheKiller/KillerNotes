using System;
using System.Collections.Generic;
using KillerFamily.Controls;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Floating format bar implementing the KillerUI annotation-bar contract: pops out of the top of the editor pane on
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
        private Thickness? _titleMargin;          // the title's own margin, before it makes room for the bar

        private const double FmtPeekHeight = 9;
        private const double FmtEdgeGapDefault = 8;
        private const double FmtEdgeThreshold = 40;   // release this close to an edge = anchor to it
        private const double FmtMinimizedCornerRadius = 3;   // the minimized strip's bottom corners

        // Floating is the default; docking, which gives the bar a row of its own above the title,
        // is the choice that gets saved. Docked, the bar never minimizes or slides.
        private const string FmtDockedSetting = "FmtBarDocked";
        internal static bool FormatBarDocked => App.GetSetting(FmtDockedSetting) == "1";
        internal bool FormatBarMinimized => _fmtMinimized;

        private void InitFormatBar()
        {
            InitializeFormatBarMenus();
            ApplyFormatBarPlacement(restoreMinimized: false);
            var inv = CultureInfo.InvariantCulture;
            if (double.TryParse(App.GetSetting("FmtBarFrac"), NumberStyles.Float, inv, out double f) &&
                f >= 0 && f <= 1)
                _fmtCenterFrac = f;
            if (int.TryParse(App.GetSetting("FmtBarGap"), out int g) && g >= 0) _fmtGap = g;
            _fmtAnchorRight = App.GetSetting("FmtBarRightSide") != "0";

            EnableFmtBarSlide(FmtGrip);
            EnableFmtBarSlide(FmtPeek);
            EnableFmtBarSlide(FmtPeekHalo);   // the grab pixels above the strip act as the strip
            EnableFmtBarSlide(FormatBar, emptySpaceOnly: true);   // empty bar space acts as the grip
            // The bar floats inside the editor pane, so reposition on pane resize
            // (covers both window resizes and sidebar-splitter drags).
            if (FormatBar.Parent is FrameworkElement hostEl)
                hostEl.SizeChanged += (_, _) => PositionFormatBar();
            ThemeManager.ThemeChanged += UpdateFormatBarCorners;
            // The bar also changes width on its own (the preview chip appears on a markdown
            // note), and the title has to move with its edge.
            FormatBar.SizeChanged += (_, _) => ClearTitleOfFormatBar();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                PositionFormatBar();
                if (App.GetSetting("FmtBarMin") == "1" && !_fmtMinimized) ToggleFormatBar(animate: false);
            }), DispatcherPriority.Loaded);
        }

        // ---- Pop-in (KillerPDF: the bar drops out of the top edge when a note opens) ----

        private void PopInFormatBar()
        {
            if (_fmtShownOnce) return;
            _fmtShownOnce = true;
            if (FormatBarDocked) return;   // a docked row is part of the layout, not a drop-in
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

        private void EnableFmtBarSlide(FrameworkElement grip, bool emptySpaceOnly = false)
        {
            bool dragging = false;   // true only after real movement (per-grip closure state)
            // No hand over the bar or its grips (Steve, 2026-11-21): the arrow stays until a drag starts.
            // A capture lost to an alt-tab or a dialog never reaches the button-up handler, which
            // would strand the closed hand on screen for the rest of the session.
            grip.LostMouseCapture += (_, _) => DragCursors.EndDrag();
            grip.MouseLeftButtonDown += (s, e) =>
            {
                if (FormatBarDocked) return;
                if (emptySpaceOnly && !IsEmptyFormatBarSpace(e.OriginalSource)) return;
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
                DragCursors.BeginDrag();
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
                ClearTitleOfFormatBar();
                UpdateFormatBarCorners();
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (_fmtDrag == null) return;
                var (startX, origLeft) = _fmtDrag.Value;
                _fmtDrag = null;
                grip.ReleaseMouseCapture();
                DragCursors.EndDrag();
                e.Handled = true;
                // A quick drag can end before any move is reported; the release still lands the
                // bar where the pointer let go.
                if (!dragging && FormatBar.Parent is FrameworkElement releaseHost)
                {
                    double dx = e.GetPosition(releaseHost).X - startX;
                    if (Math.Abs(dx) >= SystemParameters.MinimumHorizontalDragDistance)
                    {
                        double room = Math.Max(0, releaseHost.ActualWidth - FormatBar.ActualWidth);
                        FormatBar.HorizontalAlignment = HorizontalAlignment.Left;
                        FormatBar.Margin = new Thickness(Math.Max(0, Math.Min(room, origLeft + dx)), FormatBar.Margin.Top, 0, 0);
                        dragging = true;
                    }
                }
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

        // Only empty bar space starts a drag or a double-click from the bar itself; a press on a
        // button, a combo box or a field belongs to that control.
        private bool IsEmptyFormatBarSpace(object source)
        {
            for (var d = source as DependencyObject; d != null;
                 d = (d is Visual ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, FormatBar)) return true;
                if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.Selector
                    or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.Thumb) return false;
            }
            return false;
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
            double hw = host.ActualWidth;
            UpdateFormatBarDensity(hw);
            if (FormatBarDocked)
            {
                FormatBar.HorizontalAlignment = HorizontalAlignment.Stretch;
                FormatBar.Margin = new Thickness(0);
                FormatBar.MaxWidth = double.PositiveInfinity;
                ClearTitleOfFormatBar();
                UpdateFormatBarCorners();
                return;
            }
            FormatBar.MaxWidth = Math.Max(88, hw - 4);
            FormatBar.UpdateLayout();
            double bw = FormatBar.ActualWidth;
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
            ClearTitleOfFormatBar();
            UpdateFormatBarCorners();
        }

        /// <summary>The bar hangs from the pane's top edge, so its top corners are square where they
        /// meet that edge. A top corner rounds only when the bar sits flush against the pane's side,
        /// where it fills the pane's own rounded corner (ControlCornerRadius, as the pane's grain
        /// layer uses). The bottom corners follow the theme's flyout radius.</summary>
        private void UpdateFormatBarCorners()
        {
            if (FormatBar.Parent is not FrameworkElement host) return;
            var flyout = TryFindResource("FlyoutCornerRadius") is CornerRadius fr ? fr : new CornerRadius(6);
            var pane = TryFindResource("ControlCornerRadius") is CornerRadius pr ? pr : new CornerRadius(0);
            if (FormatBarDocked)
            {
                // A row across the pane's top: it fills the pane's rounded top corners and meets the
                // title square along its bottom edge.
                var row = new CornerRadius(pane.TopLeft, pane.TopRight, 0, 0);
                if (FormatBar.CornerRadius != row) FormatBar.CornerRadius = row;
                return;
            }
            // The minimized strip keeps the tab shape with smaller bottom corners.
            if (_fmtMinimized)
                flyout = new CornerRadius(0, 0, Math.Min(FmtMinimizedCornerRadius, flyout.BottomRight),
                    Math.Min(FmtMinimizedCornerRadius, flyout.BottomLeft));
            double hw = host.ActualWidth, bw = FormatBar.ActualWidth;
            double left = FormatBar.HorizontalAlignment == HorizontalAlignment.Right
                ? hw - bw - FormatBar.Margin.Right
                : FormatBar.Margin.Left;
            bool atLeft = bw > 0 && left <= 0.5;
            bool atRight = bw > 0 && hw - (left + bw) <= 0.5;
            var radius = new CornerRadius(atLeft ? pane.TopLeft : 0, atRight ? pane.TopRight : 0,
                flyout.BottomRight, flyout.BottomLeft);
            if (FormatBar.CornerRadius != radius) FormatBar.CornerRadius = radius;
        }

        /// <summary>The expanded bar sits beside the title, so the title gives up the side the bar
        /// is on: docked left or parked mid-pane it used to cover the title outright, and docked
        /// right it hid the end of a long one. The title keeps whichever side of the bar has more
        /// room. The minimized strip fits inside the title's top margin and needs none, and a collapsed
        /// bar none either, though it keeps reporting the width it last had.</summary>
        private void ClearTitleOfFormatBar()
        {
            _titleMargin ??= TitleBox.Margin;
            var own = _titleMargin.Value;
            if (_fmtMinimized || FormatBarDocked || FormatBar.Visibility != Visibility.Visible
                || FormatBar.Parent is not FrameworkElement host || FormatBar.ActualWidth <= 0)
            {
                TitleBox.Margin = own;
                return;
            }
            const double Gap = 8;
            double hw = host.ActualWidth, bw = FormatBar.ActualWidth;
            double left = FormatBar.HorizontalAlignment == HorizontalAlignment.Right
                ? hw - bw - FormatBar.Margin.Right
                : FormatBar.Margin.Left;
            double before = left - own.Left;           // room between the pane edge and the bar
            double after = hw - own.Right - (left + bw);   // room between the bar and the far edge
            var room = before >= after
                ? new Thickness(own.Left, own.Top, Math.Max(own.Right, hw - left + Gap), own.Bottom)
                : new Thickness(Math.Max(own.Left, left + bw + Gap), own.Top, own.Right, own.Bottom);
            if (room != TitleBox.Margin) TitleBox.Margin = room;
        }

        /// <summary>The canonical KillerPDF annotation-bar host wraps rather than discarding
        /// commands. Keep every formatting command available and let the WrapPanel reflow.</summary>
        // The width is discarded on purpose - see the note above: nothing here sheds with it, so
        // the parameter is named _ rather than left looking like an input somebody forgot to read.
        private void UpdateFormatBarDensity(double _)
        {
            bool md = UsesMarkdownTools;
            bool editing = !md || _previewMode != PreviewMode.Rendered;
            SetVisible(FmtBoldBtn, editing);
            SetVisible(FmtItalicBtn, editing);
            SetVisible(FmtUnderlineBtn, editing && !md);
            SetVisible(FmtStrikeBtn, editing);
            SetVisible(FmtMonoBtn, editing);
            SetVisible(FmtFontSeparator, editing && !md);
            SetVisible(FontSizeBtn, editing && !md);
            SetVisible(FmtColorSeparator, editing && !md);
            SetVisible(TextColorBtn, editing && !md);
            SetVisible(FmtListSeparator, editing);
            SetVisible(FmtBulletsBtn, editing);
            SetVisible(FmtNumberingBtn, editing);
            SetVisible(FmtCheckBtn, editing);
            SetVisible(FmtHeadingBtn, editing);
            SetVisible(FmtInsertSeparator, editing);
            SetVisible(FmtRuleBtn, editing);
            SetVisible(TableBtn, editing);
            SetVisible(FmtLinkBtn, editing && md);
            SetVisible(FmtImageBtn, editing && !md);
            SetVisible(FmtEndSeparator, true);
        }

        private static void SetVisible(UIElement element, bool visible) =>
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        // ---- Minimize / restore (KillerPDF ToggleAnnotBarMinimized, 120ms) ----

        internal void ToggleFormatBar(bool animate = true) => ToggleFormatBar(animate, persist: true);

        private void ToggleFormatBar(bool animate, bool persist)
        {
            if (FormatBarDocked && !_fmtMinimized) return;   // a docked bar is never minimized
            // Minimized, the whole strip must fit inside the title's 10px top margin so it
            // never overlaps the title text: 9px peek + 1px bottom border, vertical padding
            // dropped for the duration.
            double minH = FmtPeekHeight
                + FormatBar.BorderThickness.Top + FormatBar.BorderThickness.Bottom;
            _fmtMinimized = !_fmtMinimized;
            UpdatePreviewPickerVisibility();
            if (_fmtMinimized) ClearTitleOfFormatBar();

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
                FormatBar.Padding = new Thickness(2);   // compact annotation-bar chrome
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
            if (persist) App.SetSetting("FmtBarMin", _fmtMinimized ? "1" : "0");
            UpdateFormatBarCorners();
        }

        // ---- Docked or floating ----

        /// <summary>Switches the bar between floating over the title and docked in a row above it.</summary>
        internal void ToggleFormatBarPlacement()
        {
            App.SetSetting(FmtDockedSetting, FormatBarDocked ? "0" : "1");
            ApplyFormatBarPlacement();
        }

        /// <summary>Lays the bar out for the saved placement. Docking shows a minimized bar in full
        /// without forgetting the choice, so it comes back minimized when it floats again.</summary>
        private void ApplyFormatBarPlacement(bool restoreMinimized = true)
        {
            bool docked = FormatBarDocked;
            if (docked && _fmtMinimized) ToggleFormatBar(animate: false, persist: false);
            FormatBarHost.Docked = docked;
            var floatingOnly = docked ? Visibility.Collapsed : Visibility.Visible;
            FmtGrip.Visibility = floatingOnly;
            FmtHideBtn.Visibility = floatingOnly;
            if (docked)
            {
                FormatBar.BeginAnimation(HeightProperty, null);
                FormatBar.Height = double.NaN;
                FormatBar.Width = double.NaN;
                FormatBar.RenderTransform = null;
                FormatBar.Effect = null;
                FormatBar.SetResourceReference(Border.BorderThicknessProperty, "DockedBarEdgeThickness");
            }
            else
            {
                FormatBar.SetResourceReference(Border.BorderThicknessProperty, "BarEdgeThickness");
                if (!_fmtMinimized) FormatBar.Effect = TryFindResource("ShadowBar") as Effect;
            }
            PositionFormatBar();
            // At startup the Loaded pass restores a minimized bar once the bar has its real width.
            if (restoreMinimized && !docked && App.GetSetting("FmtBarMin") == "1" && !_fmtMinimized)
                ToggleFormatBar(animate: false, persist: false);
        }

        /// <summary>The row names the placement a click switches to and shows a small window of it.</summary>
        private MenuItem FormatBarPlacementItem()
        {
            bool docked = FormatBarDocked;
            var item = new MenuItem
            {
                Header = Loc(docked ? "Str_ToolMenu_Floating" : "Str_ToolMenu_Docked"),
                Tag = "toolmenu",
                Icon = FormatBarIcons.For(floating: docked),
                InputGestureText = "Alt+B",
            };
            item.Click += (_, _) => ToggleFormatBarPlacement();
            return item;
        }

        /// <summary>True while the bar card shows the heading tiles for a writable note (Alt+0..3 reach them).</summary>
        private bool _fmtCardHeadings;

        private void InitializeFormatBarMenus()
        {
            // The bar's own right-click is a KillerUI tile card (NoteCards): the heading levels when it lands on
            // the heading button, then Docked or Floating and Minimize. The picker buttons keep their own lists.
            var menu = NoteCards.Menu();
            menu.PreviewKeyDown += FormatBarMenu_KeyDown;
            FormatBar.ContextMenu = menu;
            FormatBar.ContextMenuOpening += (_, e) =>
            {
                var source = e.OriginalSource as DependencyObject;
                while (source != null && source is not Button)
                    source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
                if (source is Button { ContextMenu: not null }) return;
                bool writable = NoteStore.IsOpen && !NoteStore.IsReadOnly && !_currentInTrash;
                _fmtCardHeadings = source == FmtHeadingBtn && writable;
                var tiles = new List<CardTile>();
                if (source == FmtHeadingBtn)
                {
                    for (int level = 1; level <= 3; level++)
                    {
                        int chosen = level;
                        tiles.Add(new CardTile("", string.Format(Loc("Str_St_Heading"), level), "Alt+" + level,
                            () => SetHeadingLevel(chosen), false, writable) { Id = "heading" + level });
                    }
                    tiles.Add(new CardTile("", Loc("Str_St_HeadingOff"), "Alt+0", () => SetHeadingLevel(0), false, writable) { Id = "heading0" });
                }
                bool docked = FormatBarDocked;
                tiles.Add(new CardTile(docked ? "" : "", Loc(docked ? "Str_ToolMenu_Floating" : "Str_ToolMenu_Docked"), "Alt+B",
                    ToggleFormatBarPlacement) { Id = "toolmenu" });
                if (!docked)
                    tiles.Add(new CardTile(_fmtMinimized ? "" : "", Loc(_fmtMinimized ? "Str_Ctx_RestoreToolbar" : "Str_Ctx_MinimizeToolbar"), "F6",
                        () => ToggleFormatBar()) { Id = "minimize" });
                NoteCards.Fill(menu, null, tiles);
            };
            foreach (var button in FmtButtons.Children.OfType<Button>().Where(b => b.ContextMenu != null))
            {
                var owned = button.ContextMenu!;
                owned.PreviewKeyDown += FormatBarMenu_KeyDown;
                var minimize = FormatBarMinimizeItem();
                var placement = FormatBarPlacementItem();
                owned.Items.Add(new Separator());
                owned.Items.Add(placement);
                owned.Items.Add(minimize);
                owned.Opened += (_, _) =>
                {
                    bool docked = FormatBarDocked;
                    placement.Header = Loc(docked ? "Str_ToolMenu_Floating" : "Str_ToolMenu_Docked");
                    placement.Icon = FormatBarIcons.For(floating: docked);
                    minimize.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
                    minimize.SetResourceReference(HeaderedItemsControl.HeaderProperty,
                        _fmtMinimized ? "Str_Ctx_RestoreToolbar" : "Str_Ctx_MinimizeToolbar");
                    minimize.Icon = _fmtMinimized ? "\uE70D" : "\uE70E";
                };
            }
        }

        private void FormatBarMenu_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var mods = System.Windows.Input.Keyboard.Modifiers;
            if (mods == System.Windows.Input.ModifierKeys.None && key == System.Windows.Input.Key.F6)
            {
                e.Handled = true;
                if (!e.IsRepeat) { menu.IsOpen = false; ToggleFormatBar(); }
            }
            else if (mods == System.Windows.Input.ModifierKeys.Alt && key == System.Windows.Input.Key.B)
            {
                e.Handled = true;
                if (!e.IsRepeat) { menu.IsOpen = false; ToggleFormatBarPlacement(); }
            }
            else if (ReferenceEquals(menu, FormatBar.ContextMenu) && mods == System.Windows.Input.ModifierKeys.Alt &&
                     key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D3)
            {
                if (!_fmtCardHeadings) return;
                int level = key - System.Windows.Input.Key.D0;
                e.Handled = true;
                if (!e.IsRepeat) { menu.IsOpen = false; SetHeadingLevel(level); }
            }
        }

        private MenuItem FormatBarMinimizeItem()
        {
            var item = new MenuItem { InputGestureText = "F6", Icon = _fmtMinimized ? "\uE70D" : "\uE70E" };
            item.SetResourceReference(HeaderedItemsControl.HeaderProperty,
                _fmtMinimized ? "Str_Ctx_RestoreToolbar" : "Str_Ctx_MinimizeToolbar");
            item.Click += FormatBarToggle_Click;
            return item;
        }

        private void FormatBarToggle_Click(object sender, RoutedEventArgs e) => ToggleFormatBar();
    }
}

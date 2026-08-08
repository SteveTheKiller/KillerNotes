using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;   // DragDeltaEventArgs / DragCompletedEventArgs (Thumb)
using System.Windows.Media;
using System.Windows.Media.Animation;
using KillerNotes.Controls;

namespace KillerNotes.Shell
{
    // Sidebar collapse (chevron button / F5), KillerPDF-style: collapsing shrinks the
    // column to a slim strip with just the expand chevron; the chosen width comes back
    // on expand. State persists across runs.
    public partial class MainWindow
    {
        private bool _sidebarCollapsed;
        // The user's chosen sidebar width in SCREEN px (scale-independent). The column's
        // logical width is this divided by the app scale (AppScale.cs), so the sidebar keeps
        // the same on-screen width when the whole UI is zoomed instead of widening and sliding
        // the icon rail out from under the cursor.
        private double _sidebarBaseWidth = 280;

        // Rail column width, LOGICAL units (scales with the app zoom). Kept tight to the
        // 20-logical RailButtons + their 2px inset so the strip hugs the icons instead of
        // scaling empty air at high zoom - at 176% every spare logical unit is visible.
        private const double RailW = 24;

        // The panel's floor in LOGICAL units. The toolbar WRAPS when narrow (the sort
        // trio drops under the New-note button, SidebarToolbar_SizeChanged), so this
        // only needs to cover the widest single row of the wrapped layout - past it
        // the sidebar grows with the zoom instead of cutting anything off.
        private const double PanelMinLogical = 160;

        /// <summary>Expanded sidebar column width in logical units for scale s: the
        /// remembered on-screen width, floored so the toolbar always fits.</summary>
        private double ExpandedLogicalWidth(double s) => Math.Max(_sidebarBaseWidth / s, PanelMinLogical);

        /// <summary>Wrap decision ONLY - the wording is UpdateNewNoteLabel's job (it
        /// steps "+ New note" -> "+ New" -> "+" against its own column, and its column
        /// reflects whatever this handler decides). The sorts drop to their own row
        /// exactly when even the bare "+" cannot share a row with them, and snap back
        /// the moment it can. One writer per concern, so the two can't fight.</summary>
        private void SidebarToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (SortBtns.ActualWidth <= 0) return;
            bool wrap = e.NewSize.Width < MeasureNewNoteWidth("+") + SortBtns.ActualWidth + 12;
            if (wrap == (Grid.GetRow(SortBtns) == 1)) return;
            if (wrap)
            {
                Grid.SetRow(SortBtns, 1);
                Grid.SetColumn(SortBtns, 0);
                Grid.SetColumnSpan(SortBtns, 2);
                SortBtns.HorizontalAlignment = HorizontalAlignment.Left;
                SortBtns.Margin = new Thickness(-8, 8, 0, 0);   // cancels SortTimeBtn's 8px lead-in
            }
            else
            {
                Grid.SetRow(SortBtns, 0);
                Grid.SetColumn(SortBtns, 1);
                Grid.SetColumnSpan(SortBtns, 1);
                SortBtns.HorizontalAlignment = HorizontalAlignment.Right;
                SortBtns.Margin = new Thickness(0);
            }
            UpdateNewNoteLabel();   // the button's column just changed shape - re-pick now
        }

        private void SidebarToggle_Click(object sender, RoutedEventArgs e) => ToggleSidebar();

        private void ToggleSidebar()
        {
            _kalcAutoExpanded = false;   // a manual toggle owns the state; don't auto-restore later
            _sidebarCollapsed = !_sidebarCollapsed;
            ApplySidebarState(animate: true);
            App.SetSetting("SidebarCollapsed", _sidebarCollapsed ? "1" : "0");
        }

        /// <summary>Restores the persisted collapsed state; call once from the constructor.</summary>
        private void InitSidebar()
        {
            _sidebarCollapsed = App.GetSetting("SidebarCollapsed") == "1";
            if (_sidebarCollapsed) ApplySidebarState();
            InitNotesFade();
            // "New note" label steps down as its column narrows; SizeChanged fires on the
            // first layout too, so the initial wording is set without an extra call.
            SidebarPanel.SizeChanged += (_, _) => UpdateNewNoteLabel();
        }

        // ---- Sidebar resize grip (MainWindow.xaml SidebarResizeGrip) ----
        // A 1px line on the notes panel's inner edge, so it reads as the seam immediately LEFT
        // of the icon rail: invisible at rest, visible on hover, accent-lit while dragging.
        // Family standard, matching KillerShell and KillerPDF.

        /// <summary>Drives SidebarCol directly. A GridSplitter would resize this column against
        /// the 24px rail beside it, which must never move; driving the outer column by hand lets
        /// the content column absorb the change instead.</summary>
        private void SidebarResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_sidebarCollapsed) return;

            double s = _appScale <= 0 ? 1 : _appScale;
            double min = Math.Max(230 / s, PanelMinLogical);
            double max = Math.Max(480 / s, min);
            double next = Math.Max(min, Math.Min(max, SidebarCol.ActualWidth + e.HorizontalChange));
            if (Math.Abs(next - SidebarCol.ActualWidth) < 0.5) return;

            // Straight to the column, no animation: a tween would lag the pointer. The bounds
            // have to move with it, or a column pinned at its old MinWidth ignores the new Width.
            SidebarCol.BeginAnimation(ColumnDefinition.WidthProperty, null);
            SidebarCol.MinWidth = min;
            SidebarCol.MaxWidth = max;
            SidebarCol.Width = new GridLength(next);
        }

        /// <summary>Records the drag result as the new base width. Without this, the next width
        /// re-apply - a language switch refreshing the collapse tooltip, or a zoom change - snapped
        /// the sidebar back to the stale remembered width.</summary>
        private void SidebarResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            double s = _appScale <= 0 ? 1 : _appScale;
            if (!_sidebarCollapsed && SidebarCol.ActualWidth > 0)
                _sidebarBaseWidth = SidebarCol.ActualWidth * s;
        }

        // ---- Responsive "New note" button ----
        // The button shares its row with the sort buttons; as the sidebar narrows its column
        // shrinks, so the label steps down "+ New note" -> "+ New" -> "+" instead of clipping,
        // taking the widest wording that still fits the column. The tooltip keeps the full text.
        private void UpdateNewNoteLabel()
        {
            if (NewNoteBtn == null || NewNoteCol == null) return;
            double avail = NewNoteCol.ActualWidth;
            if (avail <= 0) return;
            string label = "+";
            foreach (var key in new[] { "Str_Btn_NewNote", "Str_Btn_NewNoteShort" })
            {
                string s = Loc(key);
                if (MeasureNewNoteWidth(s) <= avail) { label = s; break; }
            }
            if (!Equals(NewNoteBtn.Content, label)) NewNoteBtn.Content = label;
        }

        private double MeasureNewNoteWidth(string text)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(NewNoteBtn.FontFamily, NewNoteBtn.FontStyle, NewNoteBtn.FontWeight, NewNoteBtn.FontStretch),
                NewNoteBtn.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(NewNoteBtn).PixelsPerDip);
            return ft.Width
                 + NewNoteBtn.Padding.Left + NewNoteBtn.Padding.Right
                 + NewNoteBtn.BorderThickness.Left + NewNoteBtn.BorderThickness.Right + 2;
        }

        // ---- Notes-list edge fades ----
        // Each overlay appears only while more content exists in its direction.

        private ScrollViewer? _notesScroll;

        private void InitNotesFade()
        {
            // ScrollChanged bubbles from the list's own ScrollViewer and fires on scroll, on
            // extent changes (list (re)populated), and on viewport changes (resize) - the one
            // hook that covers every case. Resolve the ScrollViewer from the event the first
            // time it fires: it is guaranteed present then, unlike at Loaded. The overlay's look
            // (chrome + grain + fade mask) lives in the XAML and follows the theme itself.
            NotesList.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(NotesScroll_Changed));
            NotesList.Loaded += (_, _) =>
                Dispatcher.BeginInvoke(new System.Action(ResolveAndUpdateNotesFade),
                                       System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void NotesScroll_Changed(object sender, ScrollChangedEventArgs e)
        {
            if (_notesScroll == null && e.OriginalSource is ScrollViewer sv) _notesScroll = sv;
            UpdateNotesFade();
        }

        /// <summary>Resolve the list's ScrollViewer (once) and refresh the fade. Also called
        /// after a list rebuild, when no ScrollChanged is guaranteed.</summary>
        internal void ResolveAndUpdateNotesFade()
        {
            _notesScroll ??= FindDescendant<ScrollViewer>(NotesList);
            UpdateNotesFade();
        }

        private void UpdateNotesFade()
        {
            if (_notesScroll == null || NotesFade == null || NotesTopFade == null) return;
            bool overflow = _notesScroll.ScrollableHeight > 0.5;
            bool atTop = _notesScroll.VerticalOffset <= 0.5;
            bool atBottom = _notesScroll.VerticalOffset >= _notesScroll.ScrollableHeight - 0.5;

            // Fade the list pixels themselves to transparent. An overlay can only match a flat
            // sidebar; on horizontal chrome gradients it becomes a visibly different rectangle.
            // Transparency reveals the exact grain and gradient already behind the list.
            // A theme can switch the scroll fades off entirely (EdgeFadeOpacity 0) - a soft gradient
            // hint is a modern idiom and wrong on a retro one, where a list ends at a hard edge.
            // It has to be handled HERE rather than on the NotesTopFade/NotesFade borders: those two
            // are dead (see the bottom of this method), and the fade is really an OpacityMask on the
            // list itself, so a Border-level opacity key never reaches it.
            if (Application.Current?.TryFindResource("EdgeFadeOpacity") is double fadeOp && fadeOp <= 0)
            {
                NotesList.OpacityMask = null;
                NotesTopFade.Visibility = Visibility.Collapsed;
                NotesFade.Visibility = Visibility.Collapsed;
                return;
            }

            bool fadeTop = overflow && !atTop;
            bool fadeBottom = overflow && !atBottom;
            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            if (fadeTop)
            {
                mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
                mask.GradientStops.Add(new GradientStop(Colors.Black, .045));
            }
            else mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
            if (fadeBottom)
            {
                mask.GradientStops.Add(new GradientStop(Colors.Black, .955));
                mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            }
            else mask.GradientStops.Add(new GradientStop(Colors.Black, 1));
            NotesList.OpacityMask = mask;

            // Retained as named layout elements for compatibility, but no longer painted.
            NotesTopFade.Visibility = Visibility.Collapsed;
            NotesFade.Visibility = Visibility.Collapsed;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var deeper = FindDescendant<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        // The icon rail stays put in both states (KillerPDF pattern); collapsing just
        // hides the panel next to it and narrows the column to the rail width.
        private void ApplySidebarState(bool animate = false)
        {
            double s = _appScale <= 0 ? 1 : _appScale;
            RailCol.Width = new GridLength(RailW);   // logical: the rail scales with the app zoom

            if (_sidebarCollapsed)
            {
                // Remember the expanded on-screen width (respects a splitter drag) before
                // collapsing. ActualWidth is in ScaleHost's pre-scale space, so scale it up.
                if (SidebarCol.ActualWidth > 0) _sidebarBaseWidth = SidebarCol.ActualWidth * s;
                SidebarResizeGrip.IsEnabled = false;   // nothing to resize down to the bare rail
            }
            else
            {
                SidebarPanel.Visibility = Visibility.Visible;   // reveal before the expand slide
                SidebarResizeGrip.IsEnabled = true;
            }

            // Chevron points toward where the panel goes (char casts: literal PUA glyphs
            // do not survive tooling).
            SidebarToggleBtn.Content = ((char)(_sidebarCollapsed ? 0xE76C : 0xE76B)).ToString();
            SidebarToggleBtn.ToolTip = Loc(_sidebarCollapsed ? "Str_TT_ExpandSidebar" : "Str_TT_CollapseSidebar");

            // Collapsed = just the rail (RailW logical, scales with the app); expanded = the
            // remembered on-screen width converted to logical.
            double targetPx = _sidebarCollapsed ? RailW : ExpandedLogicalWidth(s);

            if (!animate)
            {
                SidebarCol.BeginAnimation(ColumnDefinition.WidthProperty, null);
                RefreshSidebarWidth();
                if (_sidebarCollapsed) SidebarPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Freeze the panel at its full width and left-align it so it does NOT reflow while the
            // column moves - the XAML clip host wipes it in/out instead. When expanding, the panel
            // was just re-shown so its ActualWidth is ~0; fall back to the computed expanded width
            // (* cell = SidebarCol - rail, minus the 8px left margin).
            double panelW = SidebarPanel.ActualWidth > 8
                ? SidebarPanel.ActualWidth
                : Math.Max(0, ExpandedLogicalWidth(s) - RailW - 8);   // total logical minus the logical rail and left margin
            SidebarPanel.HorizontalAlignment = HorizontalAlignment.Left;
            SidebarPanel.Width = panelW;

            // Slide the column width (WPF has no built-in GridLength animation - GridLengthAnimation.cs).
            // Min/Max are opened for the tween and settled by RefreshSidebarWidth when it lands.
            double fromPx = SidebarCol.ActualWidth > 0 ? SidebarCol.ActualWidth : targetPx;
            SidebarCol.MinWidth = 0;
            SidebarCol.MaxWidth = double.PositiveInfinity;
            var anim = new GridLengthAnimation
            {
                From = fromPx,
                To = targetPx,
                Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                EasingFunction = new QuadraticEase { EasingMode = _sidebarCollapsed ? EasingMode.EaseIn : EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                SidebarCol.BeginAnimation(ColumnDefinition.WidthProperty, null);
                // Un-freeze the panel so a later splitter resize reflows normally.
                SidebarPanel.ClearValue(FrameworkElement.WidthProperty);
                SidebarPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                RefreshSidebarWidth();
                if (_sidebarCollapsed) SidebarPanel.Visibility = Visibility.Collapsed;
            };
            SidebarCol.BeginAnimation(ColumnDefinition.WidthProperty, anim);
        }

        /// <summary>Sets the sidebar column and icon-rail widths for the current collapsed
        /// state. The PANEL divides by the app scale so it keeps a fixed ON-SCREEN width while
        /// the UI zooms (AppScale.cs); the RAIL stays a constant RailW LOGICAL so it scales
        /// with the app and its (scaling) icons never clip - bigger zoom, bigger targets. At
        /// scale 1.0 these are the original 280 / 230-480 / RailW values, so nothing changes
        /// until the app is zoomed. Called on collapse/expand and on every scale change.</summary>
        internal void RefreshSidebarWidth()
        {
            double s = _appScale <= 0 ? 1 : _appScale;
            RailCol.Width = new GridLength(RailW);
            if (_sidebarCollapsed)
            {
                SidebarCol.MinWidth = 0;
                SidebarCol.MaxWidth = RailW;
                SidebarCol.Width = new GridLength(RailW);
            }
            else
            {
                // Screen-constant bounds, floored at the logical minimum the toolbar
                // needs (Max keeps Min <= Max when the zoom pushes 480/s under it).
                SidebarCol.MinWidth = Math.Max(230 / s, PanelMinLogical);
                SidebarCol.MaxWidth = Math.Max(480 / s, SidebarCol.MinWidth);
                SidebarCol.Width = new GridLength(ExpandedLogicalWidth(s));
            }
        }

        // Every theme-button entry point opens the same flyout at the same fixed spot
        // (OpenThemeMenu, ThemeFlyout.cs - a ContextMenu sharing the locale menu's
        // placement settings and themed chrome).
        private void SidebarThemeBtn_Click(object sender, RoutedEventArgs e) => OpenThemeMenu();

        /// <summary>Expands first if collapsed, then focuses the search box (F3 / Ctrl+F).</summary>
        private void FocusSearch()
        {
            if (_sidebarCollapsed) ToggleSidebar();
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }
}

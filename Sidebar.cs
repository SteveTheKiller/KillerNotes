using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KillerNotes
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
            // A splitter drag resizes the column directly and nothing recorded it, so
            // the next width re-apply (language switch refreshing the collapse tooltip,
            // or a zoom change) snapped the sidebar back to the stale remembered width.
            // Record the drag result as the new base and every re-apply keeps it.
            SidebarSplitter.DragCompleted += (_, _) =>
            {
                double s = _appScale <= 0 ? 1 : _appScale;
                if (!_sidebarCollapsed && SidebarCol.ActualWidth > 0)
                    _sidebarBaseWidth = SidebarCol.ActualWidth * s;
            };
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

        // ---- Notes-list bottom fade ----
        // NotesFade (a Border overlaid on the bottom of the list in the XAML, inset from the
        // scrollbar) fades the last rows into the chrome, but only while the list actually
        // overflows AND is not scrolled to the very bottom - so it reads as a "more below" hint,
        // not a permanent vignette. The overlay's look (chrome + grain under a fade mask) lives
        // in the XAML and follows the theme on its own; here we only toggle its visibility.

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
            if (_notesScroll == null || NotesFade == null) return;
            bool overflow = _notesScroll.ScrollableHeight > 0.5;
            bool atBottom = _notesScroll.VerticalOffset >= _notesScroll.ScrollableHeight - 0.5;
            NotesFade.Visibility = overflow && !atBottom ? Visibility.Visible : Visibility.Collapsed;
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
                SidebarSplitter.IsEnabled = false;
            }
            else
            {
                SidebarPanel.Visibility = Visibility.Visible;   // reveal before the expand slide
                SidebarSplitter.IsEnabled = true;
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

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KillerNotes
{
    // Sidebar collapse (chevron button / F5), KillerPDF-style: collapsing shrinks the
    // column to a slim strip with just the expand chevron; the chosen width comes back
    // on expand. State persists across runs.
    public partial class MainWindow
    {
        private bool _sidebarCollapsed;
        private GridLength _sidebarWidth = new(280);

        private void SidebarToggle_Click(object sender, RoutedEventArgs e) => ToggleSidebar();

        private void ToggleSidebar()
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            ApplySidebarState();
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
        private void ApplySidebarState()
        {
            if (_sidebarCollapsed)
            {
                _sidebarWidth = SidebarCol.Width;   // remember the user's width
                SidebarCol.MinWidth = 0;
                SidebarCol.MaxWidth = 30;
                SidebarCol.Width = new GridLength(30);
                SidebarPanel.Visibility = Visibility.Collapsed;
                SidebarSplitter.IsEnabled = false;
            }
            else
            {
                SidebarCol.MinWidth = 230;
                SidebarCol.MaxWidth = 480;
                SidebarCol.Width = _sidebarWidth;
                SidebarPanel.Visibility = Visibility.Visible;
                SidebarSplitter.IsEnabled = true;
            }
            // Chevron points toward where the panel goes (char casts: literal PUA glyphs
            // do not survive tooling).
            SidebarToggleBtn.Content = ((char)(_sidebarCollapsed ? 0xE76C : 0xE76B)).ToString();
            SidebarToggleBtn.ToolTip = Loc(_sidebarCollapsed ? "Str_TT_ExpandSidebar" : "Str_TT_CollapseSidebar");
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

using System.Windows;
using System.Windows.Controls;

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
                SidebarCol.MinWidth = 200;
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

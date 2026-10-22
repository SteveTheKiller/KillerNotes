using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private bool _titleSearchExpanded = true;

        private void InitTitleSearch() =>
            SetTitleSearchExpanded(App.GetSetting("TitleSearchCollapsed") != "1", persist: false);

        private void SetTitleSearchExpanded(bool expanded, bool persist = true)
        {
            bool moveFocus = !expanded && (SearchBox.IsKeyboardFocusWithin || SidebarReplaceBox.IsKeyboardFocusWithin
                || SearchFilterBtn.IsKeyboardFocusWithin || SidebarReplaceBtn.IsKeyboardFocusWithin);
            _titleSearchExpanded = expanded;
            SearchFieldContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            SearchFilterBtn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            SidebarReplaceBtn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            TitleNoteSearch.Width = expanded ? 308 : 30;
            if (!expanded) SetSidebarReplaceOpen(false);
            if (moveFocus) SearchIconButton.Focus();
            if (persist) App.SetSetting("TitleSearchCollapsed", expanded ? "0" : "1");
        }

        private void SearchIcon_Click(object sender, RoutedEventArgs e) => FocusSearch();

        private void SearchIcon_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            SearchBarVisibilityMenuItem.SetResourceReference(HeaderedItemsControl.HeaderProperty,
                _titleSearchExpanded ? "Str_Search_HideBar" : "Str_Search_ShowBar");
            SearchBarVisibilityMenuItem.Icon = _titleSearchExpanded ? "\uE70E" : "\uE70D";
        }

        private void SearchBarVisibility_Click(object sender, RoutedEventArgs e)
        {
            SetTitleSearchExpanded(!_titleSearchExpanded);
            if (_titleSearchExpanded)
                Dispatcher.BeginInvoke(new Action(FocusSearch), DispatcherPriority.Input);
            else
                SearchIconButton.Focus();
        }
    }
}

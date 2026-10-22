using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private bool _titleSearchExpanded = true;
        private bool _titleSearchAutoHide;

        private void InitTitleSearch()
        {
            _titleSearchAutoHide = App.GetSetting("TitleSearchCollapsed") == "1";
            SetTitleSearchExpanded(!_titleSearchAutoHide);
            AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
            {
                if (!IsTitleSearchTarget(e.OriginalSource as DependencyObject)) CollapseEmptyTitleSearch();
            }), handledEventsToo: true);
            AddHandler(Keyboard.LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler((_, _) => QueueTitleSearchAutoHide()), handledEventsToo: true);
            SearchBox.TextChanged += (_, _) => QueueTitleSearchAutoHide();
            Deactivated += (_, _) => CollapseEmptyTitleSearch();
        }

        private void SetTitleSearchExpanded(bool expanded, bool persist = false)
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
            if (persist)
            {
                _titleSearchAutoHide = !expanded;
                App.SetSetting("TitleSearchCollapsed", expanded ? "0" : "1");
            }
        }

        private void CollapseEmptyTitleSearch()
        {
            if (_titleSearchAutoHide && _titleSearchExpanded && SearchBox.Text.Length == 0)
                SetTitleSearchExpanded(false);
        }

        private void QueueTitleSearchAutoHide()
        {
            if (!_titleSearchAutoHide || !_titleSearchExpanded || SearchBox.Text.Length != 0) return;
            // Wait for focus to reach another search control or its popup before deciding.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsTitleSearchTarget(Keyboard.FocusedElement as DependencyObject)) CollapseEmptyTitleSearch();
            }), DispatcherPriority.Input);
        }

        private bool IsTitleSearchTarget(DependencyObject? target)
        {
            for (var current = target; current != null;
                current = current is Visual ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current))
            {
                if (current == TitleNoteSearch || current == SidebarReplaceRow || current == TitleSearchFlyout) return true;
                if (current is ContextMenu menu) return IsTitleSearchTarget(menu.PlacementTarget);
            }
            return false;
        }

        private void SearchIcon_Click(object sender, RoutedEventArgs e) => FocusSearch();

        private void SearchIcon_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            SearchBarVisibilityMenuItem.SetResourceReference(HeaderedItemsControl.HeaderProperty,
                _titleSearchAutoHide ? "Str_Search_ShowBar" : "Str_Search_HideBar");
            SearchBarVisibilityMenuItem.Icon = _titleSearchAutoHide ? "\uE70D" : "\uE70E";
        }

        private void SearchBarVisibility_Click(object sender, RoutedEventArgs e)
        {
            SetTitleSearchExpanded(_titleSearchAutoHide, persist: true);
            if (_titleSearchExpanded)
                Dispatcher.BeginInvoke(new Action(FocusSearch), DispatcherPriority.Input);
            else
                SearchIconButton.Focus();
        }
    }
}

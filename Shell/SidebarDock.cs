using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KillerNotes.Shell
{
    // Which side of the window the sidebar docks to: left (the default) or right. The rail stays
    // on the sidebar's inner lip, next to the editor, on either side, so the whole row is a mirror
    // image: the columns of ScaleHost and of the sidebar's own panel/rail grid flip, and the three
    // theme insets that assume a left sidebar are mirrored locally on ScaleHost.
    public partial class MainWindow
    {
        private const string SidebarSideKey = "SidebarSide";
        private bool _sidebarRight;
        private bool _sidebarMirrored;   // what the grids currently show

        // Theme insets written for a left sidebar. Mirrored copies go on ScaleHost so every
        // DynamicResource inside the row picks them up; removing them restores the theme's own.
        private static readonly string[] MirroredInsets =
            ["ContentPaneMargin", "ContentPaneInnerMargin", "SidebarPanelMargin"];

        /// <summary>Restores the saved side; call once from the constructor, after InitSidebar.</summary>
        private void InitSidebarSide()
        {
            _sidebarRight = string.Equals(App.GetSetting(SidebarSideKey), "Right", StringComparison.OrdinalIgnoreCase);
            Services.ThemeManager.ThemeChanged += ApplySidebarInsets;
            SidebarResizeGrip.Loaded += (_, _) => ApplyGripSide();
            if (_sidebarRight) ApplySidebarSide();
        }

        private void SelectSidebarSide(bool right)
        {
            if (right == _sidebarRight) return;
            _sidebarRight = right;
            App.SetSetting(SidebarSideKey, right ? "Right" : "Left");
            ApplySidebarSide();
        }

        /// <summary>Alt+Shift+Left / Right. The arrow of the side the sidebar is already on
        /// collapses or expands it; the other arrow moves it there.</summary>
        private void SidebarSideShortcut(bool right)
        {
            if (right == _sidebarRight) ToggleSidebar();
            else SelectSidebarSide(right);
        }

        private void ApplySidebarSide()
        {
            if (_sidebarMirrored != _sidebarRight)
            {
                MirrorColumns(ScaleHost);
                MirrorColumns(SidebarHost);
                _sidebarMirrored = _sidebarRight;
            }
            ApplyGripSide();
            ApplySidebarInsets();
            // The header keeps its right inset, which lines it up with the list's scrollbar (still
            // on the right), and gains a left one to clear the grip that is now on that side.
            SidebarHeaderBlock.Margin = _sidebarRight ? new Thickness(8, 0, 8, 0) : new Thickness(0, 0, 8, 0);
            RailFlyoutAnchor.HorizontalAlignment = _sidebarRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            RailFlyoutAnchor.Margin = _sidebarRight ? new Thickness(0, 0, 4, 0) : new Thickness(4, 0, 0, 0);
            FlyoutPlacement.RailOnRight = _sidebarRight;
            UpdateSidebarChevron();
        }

        /// <summary>Reverses a grid's columns and moves every child to the mirrored column.
        /// Its own inverse, which is why the applied state is tracked separately.</summary>
        private static void MirrorColumns(Grid grid)
        {
            var cols = grid.ColumnDefinitions.Cast<ColumnDefinition>().Reverse().ToList();
            int n = cols.Count;
            grid.ColumnDefinitions.Clear();
            foreach (var col in cols) grid.ColumnDefinitions.Add(col);
            foreach (UIElement child in grid.Children)
                Grid.SetColumn(child, n - Grid.GetColumn(child) - Grid.GetColumnSpan(child));
        }

        // The grip and its 1px line sit on the panel's inner edge, against the rail.
        private void ApplyGripSide()
        {
            var side = _sidebarRight ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            SidebarResizeGrip.HorizontalAlignment = side;
            if (SidebarResizeGrip.Template?.FindName("hint", SidebarResizeGrip) is Rectangle hint)
                hint.HorizontalAlignment = side;
        }

        private void ApplySidebarInsets()
        {
            foreach (string key in MirroredInsets)
            {
                ScaleHost.Resources.Remove(key);
                if (_sidebarRight && Application.Current?.TryFindResource(key) is Thickness t)
                    ScaleHost.Resources[key] = new Thickness(t.Right, t.Top, t.Left, t.Bottom);
            }
        }

        // The rail's right-click menu: which side the sidebar docks to. Its buttons keep their own.
        private void SidebarRail_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            for (var node = e.OriginalSource as DependencyObject; node != null && !ReferenceEquals(node, sender);
                 node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node is ButtonBase) return;

            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = Loc("Str_Sidebar"), IsEnabled = false });
            foreach (bool right in new[] { false, true })
            {
                var item = new MenuItem
                {
                    Header = Loc(right ? "Str_Sidebar_Right" : "Str_Sidebar_Left"),
                    IsCheckable = true,
                    IsChecked = right == _sidebarRight,
                    InputGestureText = right ? "Alt+Shift+Right" : "Alt+Shift+Left",
                    Icon = PaneSideIcon(right ? Dock.Right : Dock.Left),
                };
                item.Click += (_, _) => SelectSidebarSide(right);
                menu.Items.Add(item);
            }
            menu.PlacementTarget = (UIElement)sender;
            menu.IsOpen = true;
            e.Handled = true;
        }

        /// <summary>
        /// A small window with its sidebar shaded on the given side, the icon for a side menu's
        /// items. Drawn in the item's text color, read the way a glyph icon reads it, so it
        /// follows the highlight; a checked item shows its checkmark in this slot instead.
        /// </summary>
        private static FrameworkElement PaneSideIcon(Dock side)
        {
            var frame = new Border { BorderThickness = new Thickness(1) };
            frame.SetBinding(Border.BorderBrushProperty, InheritedForeground());
            bool across = side == Dock.Top || side == Dock.Bottom;
            var bar = new Border
            {
                Width = across ? double.NaN : 3,
                Height = across ? 3 : double.NaN,
                HorizontalAlignment = side == Dock.Right ? HorizontalAlignment.Right
                                    : side == Dock.Left ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
                VerticalAlignment = side == Dock.Bottom ? VerticalAlignment.Bottom
                                  : side == Dock.Top ? VerticalAlignment.Top : VerticalAlignment.Stretch,
            };
            bar.SetBinding(Border.BackgroundProperty, InheritedForeground());
            var icon = new Grid { Width = 12, Height = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            icon.Children.Add(frame);
            icon.Children.Add(bar);
            return icon;
        }

        private static System.Windows.Data.Binding InheritedForeground() => new System.Windows.Data.Binding
        {
            Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty),
            RelativeSource = System.Windows.Data.RelativeSource.Self,
        };
    }
}

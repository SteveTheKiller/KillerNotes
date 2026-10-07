using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KillerNotes.Controls
{
    public sealed class OverlayFocusScope
    {
        private readonly List<UIElement> _blocked = new();
        private IInputElement? _previous;
        public UIElement? ActiveOverlay { get; private set; }

        public void Open(Panel root, UIElement overlay, IInputElement? previous)
        {
            if (ActiveOverlay != null) Close(ActiveOverlay);
            _previous = previous;
            ActiveOverlay = overlay;
            foreach (UIElement child in root.Children)
            {
                if (child == overlay || !child.IsHitTestVisible) continue;
                _blocked.Add(child);
                child.SetCurrentValue(UIElement.IsHitTestVisibleProperty, false);
            }
        }

        public IInputElement? Close(UIElement overlay)
        {
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            overlay.Visibility = Visibility.Collapsed;
            if (ActiveOverlay != overlay) return null;
            ActiveOverlay = null;
            foreach (var child in _blocked)
                child.SetCurrentValue(UIElement.IsHitTestVisibleProperty, true);
            _blocked.Clear();
            var previous = _previous;
            _previous = null;
            return previous;
        }

        public bool Contains(DependencyObject? target)
        {
            while (target is FrameworkContentElement content) target = content.Parent;
            return ActiveOverlay != null && target is UIElement element &&
                (element == ActiveOverlay || ActiveOverlay.IsAncestorOf(element));
        }

        public static ListBoxItem? FirstAction(ListBox list)
        {
            var item = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.IsEnabled && i.Focusable);
            if (item != null) list.SelectedItem = item;
            return item;
        }

        public static bool CanRestore(IInputElement? target)
        {
            if (target is UIElement element) return element.IsVisible && element.IsEnabled && element.Focusable;
            if (target is not FrameworkContentElement content || !content.IsEnabled || !content.Focusable) return false;
            DependencyObject? parent = content.Parent;
            while (parent is FrameworkContentElement nested) parent = nested.Parent;
            return parent is UIElement host && host.IsVisible;
        }

        public static bool IsNavigationKey(Key key, ModifierKeys modifiers) =>
            (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift) && key is
                Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or
                Key.PageUp or Key.PageDown or Key.Enter or Key.Space;
    }
}

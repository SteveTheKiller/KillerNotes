#nullable enable
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KillerFamily.Accessibility
{
    /// <summary>
    /// Keeps keyboard focus inside a modal in-window overlay (the F1 shortcuts card, the About
    /// card, a settings card) while it shows. Opening moves focus to the overlay's first control,
    /// Tab and Ctrl+Tab cycle inside it, focus cannot wander to the window behind the dim
    /// backdrop, and closing puts focus back where it was. It follows IsVisibleChanged, so every
    /// way an overlay opens or closes (a fade, a direct Visibility change, a backdrop click, one
    /// overlay replacing another) is covered without touching those code paths.
    /// Wire it once after InitializeComponent: new OverlayFocus(this).Track(ShortcutsOverlay, AboutOverlay);
    /// </summary>
    internal sealed class OverlayFocus
    {
        private sealed class Entry
        {
            public Entry(UIElement overlay, IInputElement? previous) { Overlay = overlay; Previous = previous; }
            public UIElement Overlay { get; }
            public IInputElement? Previous { get; }
        }

        private readonly Window _window;
        private readonly List<Entry> _open = new();
        private Entry? _lastClosed;

        public OverlayFocus(Window window)
        {
            _window = window;
            window.PreviewGotKeyboardFocus += OnPreviewGotKeyboardFocus;
        }

        public OverlayFocus Track(params UIElement[] overlays)
        {
            foreach (var overlay in overlays)
            {
                // Markup that already states a mode keeps it.
                if (overlay.ReadLocalValue(KeyboardNavigation.TabNavigationProperty) == DependencyProperty.UnsetValue)
                    KeyboardNavigation.SetTabNavigation(overlay, KeyboardNavigationMode.Cycle);
                if (overlay.ReadLocalValue(KeyboardNavigation.ControlTabNavigationProperty) == DependencyProperty.UnsetValue)
                    KeyboardNavigation.SetControlTabNavigation(overlay, KeyboardNavigationMode.Cycle);
                if (overlay.ReadLocalValue(KeyboardNavigation.DirectionalNavigationProperty) == DependencyProperty.UnsetValue)
                    KeyboardNavigation.SetDirectionalNavigation(overlay, KeyboardNavigationMode.Contained);
                overlay.IsVisibleChanged += OnOverlayVisibleChanged;
                if (overlay.IsVisible) Opened(overlay);
            }
            return this;
        }

        private Entry? Top => _open.Count > 0 ? _open[_open.Count - 1] : null;

        private void OnOverlayVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not UIElement overlay) return;
            if ((bool)e.NewValue) Opened(overlay);
            else Closed(overlay);
        }

        private void Opened(UIElement overlay)
        {
            int existing = IndexOf(overlay);
            if (existing >= 0) _open.RemoveAt(existing);
            // Opening one overlay over another (F12 while F1 is up) hands back to the original
            // control, not to a control inside the overlay that is fading out.
            IInputElement? previous = Keyboard.FocusedElement;
            foreach (var entry in _open)
                if (Contains(entry.Overlay, previous as DependencyObject)) { previous = entry.Previous; break; }
            if (_lastClosed != null && Contains(_lastClosed.Overlay, previous as DependencyObject))
                previous = _lastClosed.Previous;
            _open.Add(new Entry(overlay, previous));
            overlay.Dispatcher.BeginInvoke(DispatcherPriority.Input, new System.Action(() => FocusInto(overlay)));
        }

        private void Closed(UIElement overlay)
        {
            int index = IndexOf(overlay);
            if (index < 0) return;
            var entry = _open[index];
            bool wasTop = index == _open.Count - 1;
            _open.RemoveAt(index);
            _lastClosed = entry;
            if (!wasTop) return;
            overlay.Dispatcher.BeginInvoke(DispatcherPriority.Input, new System.Action(() =>
            {
                if (Top is { } under) { FocusInto(under.Overlay); return; }
                // Leave focus alone when the app already moved it somewhere real.
                var focused = Keyboard.FocusedElement as DependencyObject;
                bool stranded = focused == null || focused == _window || Contains(entry.Overlay, focused) ||
                    (focused is UIElement u && !u.IsVisible);
                if (stranded && CanRestore(entry.Previous)) Keyboard.Focus(entry.Previous);
            }));
        }

        private void OnPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (Top is not { } top || !top.Overlay.IsVisible || e.NewFocus is not DependencyObject target) return;
            // An overlay hosted in another window is that window's business.
            if (!ReferenceEquals(Window.GetWindow(top.Overlay), _window)) return;
            if (Contains(top.Overlay, target)) return;
            e.Handled = true;
            if (!Contains(top.Overlay, Keyboard.FocusedElement as DependencyObject))
                top.Overlay.Dispatcher.BeginInvoke(DispatcherPriority.Input, new System.Action(() => FocusInto(top.Overlay)));
        }

        private void FocusInto(UIElement overlay)
        {
            if (!overlay.IsVisible || Top?.Overlay != overlay) return;
            if (Contains(overlay, Keyboard.FocusedElement as DependencyObject)) return;
            if (overlay.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)) &&
                Contains(overlay, Keyboard.FocusedElement as DependencyObject)) return;
            if (FirstFocusable(overlay) is { } first) Keyboard.Focus(first);
        }

        private int IndexOf(UIElement overlay)
        {
            for (int i = 0; i < _open.Count; i++)
                if (_open[i].Overlay == overlay) return i;
            return -1;
        }

        private static UIElement? FirstFocusable(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(parent, i) is not UIElement child || !child.IsVisible) continue;
                if (child.Focusable && child.IsEnabled && KeyboardNavigation.GetIsTabStop(child)) return child;
                if (FirstFocusable(child) is { } nested) return nested;
            }
            return null;
        }

        private static bool CanRestore(IInputElement? target)
        {
            if (target is UIElement element) return element.IsVisible && element.IsEnabled && element.Focusable;
            if (target is not FrameworkContentElement content || !content.IsEnabled || !content.Focusable) return false;
            DependencyObject? parent = content.Parent;
            while (parent is FrameworkContentElement nested) parent = nested.Parent;
            return parent is UIElement host && host.IsVisible;
        }

        // Walks up through text elements, templates and popups (a combo box drop-down or a text
        // box's context menu inside the overlay still counts as inside).
        private static bool Contains(UIElement overlay, DependencyObject? node)
        {
            for (int depth = 0; node != null && depth < 4096; depth++)
            {
                if (node == overlay) return true;
                if (node is ContextMenu menu) { node = menu.PlacementTarget; continue; }
                var logical = LogicalTreeHelper.GetParent(node);
                if (logical is System.Windows.Controls.Primitives.Popup popup) { node = popup; continue; }
                node = node is Visual ? VisualTreeHelper.GetParent(node) ?? logical : logical;
            }
            return false;
        }
    }
}

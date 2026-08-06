using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace KillerNotes.Shell
{
    // KillerPDF family standard: pin rail flyouts to the content pane's bottom-left corner.
    internal static class FlyoutPlacement
    {
        private static FrameworkElement? _pane;
        internal static void UsePane(FrameworkElement pane) => _pane = pane;

        internal static void Attach(ContextMenu menu, UIElement _)
        {
            menu.PlacementTarget = _pane;
            menu.Placement = PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = (popup, target, __) =>
            [new CustomPopupPlacement(new Point(0, System.Math.Max(0, target.Height - popup.Height)), PopupPrimaryAxis.None)];
        }
    }
}

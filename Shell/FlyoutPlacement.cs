using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace KillerNotes.Shell
{
    /// <summary>
    /// Rail flyout cards sit eight pixels inside the content pane's left and bottom edges.
    /// (The family flyout standard, from KillerPDF via KillerShell. KillerNotes had been reduced
    /// to a 21-line copy of the callback with the documentation and the Popup overload stripped
    /// out; this is the full file again.)
    ///
    /// That corner is the answer because of what bounds it, and all three matter:
    ///   - it is INSIDE the window, so a flyout never hangs over the desktop;
    ///   - it is ABOVE the footer, so the status bar is never covered;
    ///   - it is clear of the icon rail, so the rail buttons are never covered.
    /// The content pane (ContentPane in MainWindow.xaml) is the one element bounded by all three
    /// at once, so flyouts are positioned against IT - not against the button, and not by any
    /// built-in placement mode. The button argument is deliberately unused; it is kept in the
    /// signature so call sites read as "this flyout belongs to that button".
    ///
    /// WHY NOT PlacementMode.Right / Top / etc: a Popup (and a ContextMenu, which is hosted in
    /// one) is its own top-level window, and WPF's built-in modes only ever avoid the SCREEN
    /// edge. They do not know the app window exists, let alone the footer or the rail. "Right of
    /// the button" opened flyouts over the desktop when the rail sat near the window's right
    /// edge; "Top" opened them over the status bar. Hours went into re-tuning offsets before it
    /// was clear no built-in mode can express the requirement.
    ///
    /// A raw Popup placed wrongly no matter how this callback was tuned, while a
    /// Button.ContextMenu opened correctly with this exact code - the Popup path was the
    /// difference, not the math. Both KillerNotes flyouts (LangMenu, ThemeMenu) are already
    /// Button.ContextMenu for that reason. The Popup overload stays for parity with the rest of
    /// the family; nothing here uses it.
    ///
    /// WIRING (each time a flyout opens):
    ///     FlyoutPlacement.UsePane(ContentPane);         // the bordered card the content sits on
    ///     FlyoutPlacement.Attach(themeMenu, themeButton);
    ///     themeMenu.IsOpen = true;
    /// </summary>
    internal static class FlyoutPlacement
    {
        /// <summary>The content pane. Set before every attach; every flyout positions against it.</summary>
        private static FrameworkElement? _pane;

        internal static void UsePane(FrameworkElement pane) => _pane = pane;

        internal static void Attach(Popup popup, UIElement _)
        {
            popup.PlacementTarget = _pane;
            popup.Placement = PlacementMode.Custom;
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 0;
            popup.CustomPopupPlacementCallback =
                (popupSize, targetSize, __) => BottomLeftOfPane(popupSize, targetSize);
        }

        internal static void Attach(ContextMenu menu, UIElement _)
        {
            menu.PlacementTarget = _pane;
            menu.Placement = PlacementMode.Custom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.CustomPopupPlacementCallback =
                (popupSize, targetSize, __) => BottomLeftOfPane(popupSize, targetSize);
        }

        /// <summary>
        /// Compensate the template's external shadow halo. ContextMenu.Padding belongs inside
        /// the visible card and does not contribute to its outer placement.
        /// </summary>
        private const double VisibleCardInset = 8;

        private static CustomPopupPlacement[] BottomLeftOfPane(Size popupSize, Size targetSize)
        {
            var halo = new Border
            {
                Style = Application.Current?.TryFindResource("FlyoutCard") as Style
            }.Margin;
            double x = VisibleCardInset - halo.Left;
            double y = targetSize.Height - popupSize.Height + halo.Bottom - VisibleCardInset;

            // A flyout taller than the pane would otherwise start above it and run over the
            // toolbar; keep its visible top inset instead.
            if (y < VisibleCardInset - halo.Top) y = VisibleCardInset - halo.Top;

            return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
        }
    }
}

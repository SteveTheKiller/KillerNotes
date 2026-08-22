using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace KillerNotes.Shell
{
    /// <summary>
    /// Where every rail flyout opens: the BOTTOM-LEFT CORNER OF THE CONTENT PANE.
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
            popup.CustomPopupPlacementCallback =
                (popupSize, targetSize, __) => BottomLeftOfPane(popupSize, targetSize);
        }

        internal static void Attach(ContextMenu menu, UIElement _)
        {
            menu.PlacementTarget = _pane;
            menu.Placement = PlacementMode.Custom;
            menu.CustomPopupPlacementCallback =
                (popupSize, targetSize, __) => BottomLeftOfPane(popupSize, targetSize);
        }

        /// <summary>
        /// Coordinates are relative to the placement target's top-left - the pane's top-left. So
        /// x = 0 is the pane's left edge (clear of the rail) and y = pane height - flyout height
        /// puts the flyout's bottom on the pane's bottom (clear of the footer).
        /// </summary>
        /// <summary>Distance from the popup's measured bottom edge to the VISIBLE card's bottom
        /// edge: FlyoutCard's 26px bottom margin (the room its soft shadow renders into, since a
        /// Popup clips to its own bounds) plus the ContextMenu style's 0,4 Padding. Both are in
        /// Controls.xaml; change either and this has to change with it.
        ///
        /// It has to be added back because popupSize is the WHOLE popup, shadow room included.
        /// Subtracting that from the pane height lands the popup's invisible bottom on the pane's
        /// bottom, which leaves the card the user actually sees floating 30px above it.
        ///
        /// This is not tunable padding - it is the exact inset of the card inside its own popup.
        /// Note also that in PlacementMode.Custom the Horizontal/VerticalOffset setters arrive as
        /// the callback's third argument rather than being applied by WPF, and this callback
        /// ignores that argument, so those -16/-12 values do not contribute here at all.</summary>
        private const double CardBottomInset = 26 + 4;

        private static CustomPopupPlacement[] BottomLeftOfPane(Size popupSize, Size targetSize)
        {
            // Sit the VISIBLE card's bottom on the pane's bottom, not the popup's.
            double y = targetSize.Height - popupSize.Height + CardBottomInset;

            // A flyout taller than the pane would otherwise start above it and run over the
            // toolbar; pin it to the pane's top instead and let it use the height it has.
            if (y < 0) y = 0;

            return [new CustomPopupPlacement(new Point(0, y), PopupPrimaryAxis.None)];
        }
    }
}

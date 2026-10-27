using System.Windows;
using System.Windows.Input;
using KillerNotes.Features.About;

namespace KillerNotes.Shell
{
    /// <summary>
    /// APP-OWNED: the rows KillerNotes' About card adds to the shared core (Shell/About.cs).
    /// </summary>
    public partial class MainWindow : IAboutStartup, IAboutTray
    {
        bool IAboutStartup.StartWithWindows { set => AboutStartWithWindows.IsChecked = value; }
        bool IAboutTray.CloseToTray         { set => AboutCloseToTray.IsChecked = value; }

        internal bool SaveOpenNoteForUpdate() => SaveCurrentNote(refreshList: false);

        private void AboutStartWithWindows_Click(object sender, RoutedEventArgs e)
            => _about.SetStartWithWindows(AboutStartWithWindows.IsChecked == true);

        private void AboutCloseToTray_Click(object sender, RoutedEventArgs e)
            => _about.SetCloseToTray(AboutCloseToTray.IsChecked == true);

        // The About, F1 and Fonts overlays are keyboard-managed: About opens inside a focus scope
        // and focuses the card after its fade, and the three close through CloseKeyboardOverlay.
        partial void ShowingCard(ref bool handled)
        {
            HideShortcutsOverlay();
            _overlayFocus.Open(RootGrid, AboutOverlay, Keyboard.FocusedElement);
            FadeOverlayIn(AboutOverlay);
            FocusKeyboardOverlay();
            handled = true;
        }

        partial void OverlayFadingOut(UIElement overlay, ref bool handled)
        {
            if (overlay != AboutOverlay && overlay != ShortcutsOverlay && overlay != FontsOverlay) return;
            CloseKeyboardOverlay(overlay);
            handled = true;
        }

        partial void CloseShortcutsForAbout(ref bool handled)
        {
            HideShortcutsOverlay();
            handled = true;
        }
    }
}

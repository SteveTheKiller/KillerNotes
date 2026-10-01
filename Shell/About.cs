using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using KillerNotes.Controls;
using KillerNotes.Features.About;

// KillerUI kit. A partial of your MainWindow.
//
// The About overlay's window half: the fade, the click handling, and the IAboutHost
// implementation that maps the controller's values onto the named XAML elements. All the
// behavior lives in Features/About/AboutController.cs, and the values it reports come from
// Services/AppInfo.cs, Services/CodeSignature.cs and Services/UpdateService.cs.
//
// Your MainWindow.xaml is expected to provide an "AboutOverlay" Grid (ZIndex high,
// Visibility=Collapsed, dim background, MouseLeftButtonDown="AboutOverlay_Click")
// containing a card (MouseLeftButtonDown="AboutCard_Click") with these named elements:
//   AboutVersionBlock and AboutReleaseDateBlock (one row, version left / date right),
//   AboutPublisherBlock, AboutAkaBlock (Collapsed by default) wrapping AboutAkaRun inside a
//   thekiller.net Hyperlink, AboutThumbprintBlock, AboutSha256Block, AboutUpdateButton,
//   AboutUpdateText  (+ a close button Click="AboutClose_Click")
// The info panel Grid must be named AboutInfoGrid - the header binds its width to it so the
// SHA-256 line stays the only thing that sets the card width (family standard, code/CLAUDE.md).
//
// The self-update confirmation uses the kit's themed ConfirmDialog (ConfirmDialog.xaml/.cs),
// so copy those files too.
//
// Call ShowAboutOverlay() from your About button / F12 handler.
namespace KillerNotes.Shell
{
    public partial class MainWindow : IAboutHost
    {
        private readonly AboutController _about = null!;

        private void ShowAboutOverlay() => _about.Show();

        // ---- IAboutHost ----

        string IAboutHost.Version     { set => AboutVersionBlock.Text = value; }
        string IAboutHost.ReleaseDate { set => AboutReleaseDateBlock.Text = value; }
        string IAboutHost.Publisher   { set => AboutPublisherBlock.Text = value; }
        string IAboutHost.Alias       { set => AboutAkaRun.Text = value; }
        string IAboutHost.Thumbprint  { set => AboutThumbprintBlock.Text = value; }
        string IAboutHost.Sha256      { set => AboutSha256Block.Text = value; }
        string IAboutHost.UpdateText  { set => AboutUpdateText.Text = value; }

        bool IAboutHost.AliasVisible
        {
            set => AboutAkaBlock.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        bool IAboutHost.UpdateVisible
        {
            set => AboutUpdateButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        bool IAboutHost.UpdateEnabled { set => AboutUpdateButton.IsEnabled = value; }

        bool IAboutHost.DemoPreview => DemoMode;

        private IInputElement? _aboutPreviousFocus;

        void IAboutHost.ShowCard()
        {
            HideShortcutsOverlay();
            _aboutPreviousFocus = Keyboard.FocusedElement;
            BlockOverlayBackground(AboutOverlay);
            AutomationProperties.SetName(AboutVersionBlock,
                Loc("Str_About_Title") + ", " + Loc("Str_About_Version") + ": " + AboutVersionBlock.Text);
            FadeOverlayIn(AboutOverlay);
            AboutOverlay.UpdateLayout();
            AboutVersionBlock.Focus();
        }

        private void HideAboutOverlay()
        {
            if (AboutOverlay.Visibility != Visibility.Visible) return;
            AboutOverlay.BeginAnimation(OpacityProperty, null);
            AboutOverlay.Visibility = Visibility.Collapsed;
            ReleaseOverlayBackground();
            SetPreviewOverlayHidden(false);
            if (_aboutPreviousFocus is UIElement previous && previous.IsVisible && previous.IsEnabled)
                previous.Focus();
            else if (_currentId >= 0) FocusNoteBody();
            else NotesList.Focus();
            _aboutPreviousFocus = null;
        }

        private void AboutVersion_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            _about.OpenReleaseNotes();
            e.Handled = true;
        }

        // ---- Overlay fade (shared with the shortcuts overlay: Shortcuts.cs, Fonts.cs) ----

        private void FadeOverlayIn(UIElement o)
        {
            SetPreviewOverlayHidden(true);   // Preview.cs (airspace: the browser draws over overlays)
            o.Visibility = Visibility.Visible;
            Anim.FadeIn(o);
        }

        private void FadeOverlayOut(UIElement o)
        {
            if (o == AboutOverlay) { HideAboutOverlay(); return; }
            var a = new DoubleAnimation(o.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(Anim.FadeMs)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            a.Completed += (_, _) => o.Visibility = Visibility.Collapsed;
            o.BeginAnimation(UIElement.OpacityProperty, a);
            // Bring the preview back only once no overlay is left up (F12 from the F1
            // view swaps overlays; the incoming fade re-hides it immediately).
            bool otherUp = (o == AboutOverlay ? ShortcutOverlay : AboutOverlay).Visibility == Visibility.Visible;
            if (!otherUp) SetPreviewOverlayHidden(false);
        }

        // ---- Handlers ----

        // Click the dim backdrop to dismiss; a click on the card itself is swallowed.
        private void AboutOverlay_Click(object sender, MouseButtonEventArgs e) => FadeOverlayOut(AboutOverlay);
        private void AboutCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
        private void AboutClose_Click(object sender, RoutedEventArgs e) => FadeOverlayOut(AboutOverlay);

        private void AboutVersion_Click(object sender, MouseButtonEventArgs e) => _about.OpenReleaseNotes();

        private void AboutUpdateButton_Click(object sender, RoutedEventArgs e) => _about.Update();

        private void AboutLink_Navigate(object sender, RequestNavigateEventArgs e)
        {
            Services.WebLink.Open(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KillerNotes.Controls
{
    // Themed password prompt. Password/PasswordConfirm are captured on OK.
    public partial class PasswordDialog : Window
    {
        // Cancel the first close, fade out, then close for real (Anim.FadeOutAndClose). A
        // DialogResult set before this survives the cancel and is delivered by the real close.
        private bool _closeFaded;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        public bool Confirmed { get; private set; }
        public bool ExtraClicked { get; private set; }    // the first extra button (e.g. "New database...")
        public bool Extra2Clicked { get; private set; }   // the second extra button (e.g. "Open another...")
        public string Password { get; private set; } = "";
        public string PasswordConfirm { get; private set; } = "";

        public PasswordDialog(string heading, string detail, string confirmText,
                              bool showConfirm = false, string? extraText = null,
                              string? extra2Text = null, bool showCancel = true)
        {
            InitializeComponent();
            DialogShortcuts.Button(this, OkButton, "Enter", Key.Enter);
            DialogShortcuts.Bind(this, TitleBar, "Esc", Key.Escape, ModifierKeys.None, () => Cancel_Click(this, new RoutedEventArgs()));
            DialogShortcuts.Describe(CancelButton, "Esc");
            DialogShortcuts.Button(this, ExtraButton, "Ctrl+N", Key.N, ModifierKeys.Control);
            DialogShortcuts.Button(this, Extra2Button, "Ctrl+O", Key.O, ModifierKeys.Control);
            Loaded += (_, _) => { Anim.FadeIn(RootBorder); PwBox.Focus(); UpdateCapsLine(); };
            // Caps Lock can change while the dialog is up, so every key re-checks it.
            PreviewKeyUp += (_, _) => UpdateCapsLine();
            Activated += (_, _) => UpdateCapsLine();

            HeadingText.Text = heading;
            DetailText.Text = detail;
            DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            OkButton.Content = confirmText;
            if (showConfirm)
            {
                ConfirmLabel.Visibility = Visibility.Visible;
                PwConfirmBox.Visibility = Visibility.Visible;
            }
            if (!showCancel)
            {
                // The title-bar X still closes the dialog and delivers the Cancel outcome
                // (DialogTitleBar > CloseRequested -> Cancel_Click), so an explicit Cancel
                // button is redundant on flows where the escape hatches make the row wide.
                CancelButton.Visibility = Visibility.Collapsed;
            }
            if (!string.IsNullOrEmpty(extraText))
            {
                ExtraButton.Content = extraText;
                ExtraButton.Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrEmpty(extra2Text))
            {
                Extra2Button.Content = extra2Text;
                Extra2Button.Visibility = Visibility.Visible;
            }
        }

        /// <summary>The database unlock prompt's own layout (1.7.0): a lock badge with the file's
        /// name, Unlock as the one filled button across the width, and New and Open folded into
        /// "Use another database". Set, change, backup-test and sharing prompts keep the plain
        /// layout. <paramref name="error"/> shows under the box after a failed attempt.</summary>
        public void UseUnlockLayout(string fileName, string? error = null)
        {
            HeadingText.Visibility = Visibility.Collapsed;
            DetailText.Visibility = Visibility.Collapsed;
            UnlockHeader.Visibility = Visibility.Visible;
            UnlockTitle.Text = fileName;
            UnlockTitle.ToolTip = fileName;
            UnlockSubtitle.Text = TryFindResource("Str_Pw_UnlockHead") as string ?? "";
            ShowError(error);

            // Unlock leaves the button row for its own full-width line in the footer.
            if (OkButton.Parent is Panel row) row.Children.Remove(OkButton);
            OkButton.SetResourceReference(StyleProperty, "PrimaryButton");
            OkButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            OkButton.Margin = new Thickness(0);
            OkButton.Height = 34;
            UnlockFooter.Children.Insert(0, OkButton);
            ((FrameworkElement)CancelButton.Parent).Visibility = Visibility.Collapsed;
            ExtraButton.Visibility = Visibility.Collapsed;
            Extra2Button.Visibility = Visibility.Collapsed;
            ((FrameworkElement)ExtraButton.Parent).Visibility = Visibility.Collapsed;
            UnlockFooter.Visibility = Visibility.Visible;

            OtherDbNew.Header = ExtraButton.Content;
            OtherDbOpen.Header = Extra2Button.Content;
            OtherDbNew.Visibility = ExtraButton.Content == null ? Visibility.Collapsed : Visibility.Visible;
            OtherDbOpen.Visibility = Extra2Button.Content == null ? Visibility.Collapsed : Visibility.Visible;
            OtherDbButton.Visibility = ExtraButton.Content == null && Extra2Button.Content == null
                ? Visibility.Collapsed : Visibility.Visible;
            // The two buttons that carried Ctrl+N and Ctrl+O are hidden now, so the folded menu
            // takes the keys over.
            KeyDown += (_, e) =>
            {
                if (e.Handled || Keyboard.Modifiers != ModifierKeys.Control || OtherDbButton.Visibility != Visibility.Visible) return;
                if (e.Key == Key.N && OtherDbNew.Visibility == Visibility.Visible) { Extra_Click(this, e); e.Handled = true; }
                else if (e.Key == Key.O && OtherDbOpen.Visibility == Visibility.Visible) { Extra2_Click(this, e); e.Handled = true; }
            };
        }

        private void ShowError(string? error)
        {
            ErrorText.Text = error ?? "";
            ErrorLine.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateCapsLine()
            => CapsLine.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;

        // Typing again clears the last attempt's error.
        private void PwBox_PasswordChanged(object sender, RoutedEventArgs e) => ShowError(null);
        private void PwPlain_TextChanged(object sender, TextChangedEventArgs e) => ShowError(null);

        private void OtherDb_Click(object sender, RoutedEventArgs e)
        {
            OtherDbMenu.PlacementTarget = OtherDbButton;
            OtherDbMenu.Placement = PlacementMode.Bottom;
            OtherDbMenu.IsOpen = true;
        }

        /// <summary>Shows or hides the typed password. The plain box is a copy that trades the
        /// text back on each switch, so whichever box is showing holds the real value.</summary>
        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            bool reveal = PwPlain.Visibility != Visibility.Visible;
            if (reveal) PwPlain.Text = PwBox.Password; else PwBox.Password = PwPlain.Text;
            PwPlain.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
            PwBox.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
            RevealGlyph.Text = reveal ? "\uED1A" : "\uE890";   // MDL2 Hide / View
            string tip = TryFindResource(reveal ? "Str_Pw_Hide" : "Str_Pw_Show") as string ?? "";
            RevealButton.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(RevealButton, tip);
            if (reveal) { PwPlain.Focus(); PwPlain.CaretIndex = PwPlain.Text.Length; }
            else PwBox.Focus();
        }

        private string TypedPassword => PwPlain.Visibility == Visibility.Visible ? PwPlain.Text : PwBox.Password;

        private void Extra_Click(object sender, RoutedEventArgs e)
        {
            ExtraClicked = true;
            Close();
        }

        private void Extra2_Click(object sender, RoutedEventArgs e)
        {
            Extra2Clicked = true;
            Close();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Password = TypedPassword;
            PasswordConfirm = PwConfirmBox.Visibility == Visibility.Visible ? PwConfirmBox.Password : Password;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void PwBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            // With a confirm box showing, Enter in the first box goes on to it rather than submitting
            // an empty confirmation.
            if ((sender == PwBox || sender == PwPlain) && PwConfirmBox.Visibility == Visibility.Visible) PwConfirmBox.Focus();
            else OK_Click(sender, e);
            e.Handled = true;
        }
    }
}

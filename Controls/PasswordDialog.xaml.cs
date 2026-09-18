using System.Windows;
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
            Loaded += (_, _) => { Anim.FadeIn(RootBorder); PwBox.Focus(); };

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
            Password = PwBox.Password;
            PasswordConfirm = PwConfirmBox.Visibility == Visibility.Visible ? PwConfirmBox.Password : PwBox.Password;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void PwBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OK_Click(sender, e);
        }
    }
}

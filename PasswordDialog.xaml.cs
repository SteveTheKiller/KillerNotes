using System.Windows;
using System.Windows.Input;

namespace KillerNotes
{
    // Themed password prompt. Password/PasswordConfirm are captured on OK.
    public partial class PasswordDialog : Window
    {
        public bool Confirmed { get; private set; }
        public bool ExtraClicked { get; private set; }   // the optional third button (escape hatch)
        public string Password { get; private set; } = "";
        public string PasswordConfirm { get; private set; } = "";

        public PasswordDialog(string heading, string detail, string confirmText,
                              bool showConfirm = false, string? extraText = null)
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
            if (!string.IsNullOrEmpty(extraText))
            {
                ExtraButton.Content = extraText;
                ExtraButton.Visibility = Visibility.Visible;
            }
        }

        private void Extra_Click(object sender, RoutedEventArgs e)
        {
            ExtraClicked = true;
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}

using System.Windows;
using System.Windows.Input;

// KillerUI kit.
namespace KillerNotes
{
    public partial class ConfirmDialog : Window
    {
        public bool Confirmed { get; private set; }
        public bool Check1Checked => Check1.IsChecked == true;

        public ConfirmDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
        }

        // Configurable variant for reusing the themed dialog beyond the install prompt
        // (e.g. the self-update confirmation). detail may contain newlines for multiple lines.
        public ConfirmDialog(string heading, string detail, string confirmText, string? cancelText = null,
                             string? check1Label = null, bool check1Initial = false)
            : this()
        {
            HeadingText.Text = heading;
            HeadingText.Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(detail) ? 0 : 12);
            DetailText.Text = detail;
            DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            OkButton.Content = confirmText;
            // Localized default: callers rarely pass a cancel caption.
            CancelButton.Content = cancelText
                ?? Application.Current.TryFindResource("Str_Btn_Cancel") as string ?? "Cancel";
            if (!string.IsNullOrEmpty(check1Label))
            {
                Check1.Content    = check1Label;
                Check1.IsChecked  = check1Initial;
                Check1.Visibility = Visibility.Visible;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}

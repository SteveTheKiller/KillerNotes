using System.Windows;
using System.Windows.Input;

// KillerUI kit.
namespace KillerNotes
{
    // Themed one-line text prompt (see InputDialog.xaml). Groups.cs uses it for
    // naming and renaming note groups (#4).
    public partial class InputDialog : Window
    {
        public bool Confirmed { get; private set; }
        public string Value => ValueBox.Text;

        public InputDialog(string heading, string initialValue, string confirmText)
        {
            InitializeComponent();
            HeadingText.Text = heading;
            ValueBox.Text = initialValue;
            OkButton.Content = confirmText;
            Loaded += (_, _) =>
            {
                Anim.FadeIn(RootBorder);
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OK_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.Escape) { Cancel_Click(this, new RoutedEventArgs()); e.Handled = true; }
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

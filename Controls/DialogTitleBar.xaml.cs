using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KillerNotes.Controls
{
    // Code side of the shared dialog caption bar (see DialogTitleBar.xaml). Subtitle drives
    // both the plain Win98-style caption ("KillerNotes - Manage tags") and the wordmark's
    // muted trailing run; hosts wire CloseRequested to their Cancel/Close handler.
    public partial class DialogTitleBar : UserControl
    {
        public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
            nameof(Subtitle), typeof(string), typeof(DialogTitleBar),
            new PropertyMetadata(null, (d, e) => ((DialogTitleBar)d).ApplySubtitle((string?)e.NewValue)));

        public string? Subtitle
        {
            get => (string?)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        /// <summary>Raised by the caption X. Signature matches a Click handler, so hosts wire
        /// their existing Cancel_Click/Close_Click straight to it.</summary>
        public event RoutedEventHandler? CloseRequested;

        public DialogTitleBar()
        {
            InitializeComponent();
        }

        private void ApplySubtitle(string? sub)
        {
            bool none = string.IsNullOrEmpty(sub);
            PlainTitle.Text = none ? "KillerNotes" : "KillerNotes - " + sub;
            SubtitleRun.Text = none ? "" : "  " + sub;
            ShadowSubtitleRun.Text = SubtitleRun.Text;
        }

        private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                Window.GetWindow(this)?.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
            => CloseRequested?.Invoke(this, e);
    }
}

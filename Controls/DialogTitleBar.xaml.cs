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
            DialogFrame.SharpenTitleIcon(TitleIcon);
            // Above the dialog body, so the wordmark's drips hang over it instead of under it.
            Panel.SetZIndex(this, 1);
            FitCloseToBar();
        }

        // The X's box is a square as tall as the bar, so the glyph centered in it sits as far
        // from the right edge as from the top, and it reads at 13 rather than 11. 98SE keeps its
        // framed Win98 caption button at the theme's own size.
        private void FitCloseToBar()
        {
            if (TryFindResource("UseDialogCaption") is true) return;
            CloseButton.SetResourceReference(WidthProperty, "DialogTitleBarHeight");
            CloseButton.SetResourceReference(HeightProperty, "DialogTitleBarHeight");
            CloseButton.FontSize = 13;
            // Segoe UI draws the X a pixel below the middle of its box; one pixel in from the
            // right keeps the two gaps equal.
            CloseButton.Margin = new Thickness(0, 0, 1, 0);
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
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { Window.GetWindow(this)?.DragMove(); }
            catch (System.InvalidOperationException) { }   // the button was already up
        }

        private void Close_Click(object sender, RoutedEventArgs e)
            => CloseRequested?.Invoke(this, e);
    }
}

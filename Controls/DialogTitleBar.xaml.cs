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

        // The X keeps the family size every other window uses (the AboutClose* box) and the same
        // right inset ConfirmDialog's caption gets from DialogTitleBarPadding (14 on the card
        // themes, 2 on 98SE). This bar is laid out on DialogTitleMargin, whose right side is 0 on
        // the card themes, so the X sat jammed in the corner on every dialog built from it
        // (1.7.0). The margin makes up the difference, whatever the theme sets.
        private void FitCloseToBar()
        {
            double want = TryFindResource("DialogTitleBarPadding") is Thickness pad ? pad.Right : 2;
            double have = TryFindResource("DialogTitleMargin") is Thickness bar ? bar.Right : 0;
            CloseButton.Margin = new Thickness(0, 0, System.Math.Max(0, want - have), 0);
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

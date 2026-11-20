using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KillerNotes.Controls
{
    // Code side of the shared dialog caption bar (see DialogTitleBar.xaml). The wordmark carries
    // the app's own name the way ConfirmDialog does, Subtitle names the dialog, and hosts wire
    // CloseRequested to their Close or Cancel handler.
    public partial class DialogTitleBar : UserControl
    {
        public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
            nameof(Subtitle), typeof(string), typeof(DialogTitleBar),
            new PropertyMetadata(null, (d, e) => ((DialogTitleBar)d).Apply()));

        public string? Subtitle
        {
            get => (string?)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        /// <summary>Raised by the caption X. Same signature as a Click handler, so a host wires
        /// its existing Close_Click or Cancel_Click straight to it.</summary>
        public event RoutedEventHandler? CloseRequested;

        public DialogTitleBar()
        {
            InitializeComponent();
            // Above the dialog body, so the wordmark's descent hangs over it instead of under it.
            Panel.SetZIndex(this, 1);
            // The icon comes from the host's DialogChrome.IconUri, the same as ConfirmDialog's.
            if (!DialogChrome.SetTitleIcon(TitleIcon)) TitleIcon.Visibility = Visibility.Collapsed;
            Apply();
        }

        // "Killer" plus the accent word for a KillerX app, "The " plus the name otherwise.
        private void Apply()
        {
            string name = Services.AppInfo.DisplayName;
            bool killer = name.StartsWith("Killer") && name.Length > "Killer".Length;
            string lead = killer ? "Killer" : "The ";
            string accent = killer ? name.Substring("Killer".Length) : name;
            foreach (var run in new[] { WordLead0, WordLead1, WordLead2 }) run.Text = lead;
            foreach (var run in new[] { WordAccent0, WordAccent1, WordAccent2 }) run.Text = accent;
            string sub = Subtitle ?? "";
            foreach (var run in new[] { Sub0, Sub1, Sub2 }) run.Text = sub.Length == 0 ? "" : "  " + sub;
            PlainTitle.Text = sub.Length == 0 ? name : name + " - " + sub;
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

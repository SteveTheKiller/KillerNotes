using System.Windows;
using System.Windows.Input;

// KillerUI kit.
namespace KillerNotes.Controls
{
    public partial class ConfirmDialog : Window
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
        public bool Check1Checked => Check1.IsChecked == true;

        public ConfirmDialog()
        {
            InitializeComponent();
            DialogShortcuts.Button(this, OkButton, "Enter", Key.Enter);
            DialogShortcuts.Bind(this, TitleBar, "Esc", Key.Escape, ModifierKeys.None, () => Cancel_Click(this, new RoutedEventArgs()));
            DialogShortcuts.Describe(CancelButton, "Esc");
            DialogShortcuts.Bind(this, Check1, "Alt+C", Key.C, ModifierKeys.Alt, () => Check1.IsChecked = Check1.IsChecked != true);
            TaskbarIdentity.Track(this);
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            Loaded += (_, _) => ApplyNotice();
        }

        // Configurable variant for reusing the themed dialog beyond the install prompt
        // (e.g. the self-update confirmation). detail may contain newlines for multiple lines.
        public ConfirmDialog(string heading, string detail, string confirmText, string? cancelText = null,
                             string? check1Label = null, bool check1Initial = false, bool showCancel = true)
            : this()
        {
            HeadingText.Text = heading;
            DetailText.Text = detail;
            DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            OkButton.Content = confirmText;
            // Localized default: callers rarely pass a cancel caption.
            CancelButton.Content = cancelText
                ?? Application.Current.TryFindResource("Str_Btn_Cancel") as string ?? "Cancel";
            CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrEmpty(check1Label))
            {
                Check1.Content    = new System.Windows.Controls.TextBlock { Text = check1Label, TextWrapping = TextWrapping.Wrap };
                Check1.IsChecked  = check1Initial;
                Check1.Visibility = Visibility.Visible;
            }
        }

        // The family setup dialog's notice ring: an amber "!" on a confirmation and the accent "i" on a
        // notice with a single button. The amber darkens on a light card so it keeps its contrast.
        private void ApplyNotice()
        {
            FitHeading();
            FitCheckLabels();
            bool confirmation = CancelButton.Visibility == Visibility.Visible;
            System.Windows.Media.Brush brush;
            if (confirmation)
            {
                var card = TryFindResource("BackgroundBrush") as System.Windows.Media.SolidColorBrush;
                bool light = card != null && card.Color.R * 0.299 + card.Color.G * 0.587 + card.Color.B * 0.114 > 140;
                brush = new System.Windows.Media.SolidColorBrush(light
                    ? System.Windows.Media.Color.FromRgb(176, 112, 0)
                    : System.Windows.Media.Color.FromRgb(255, 190, 80));
            }
            else
            {
                brush = TryFindResource("PrimaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
            }
            NoticeGlyph.Text = confirmation ? "!" : "i";
            NoticeGlyph.Foreground = brush;
            NoticeRing.BorderBrush = brush;
        }

        // Long checkbox labels wrap inside the dialog body instead of running past its edge.
        private void FitCheckLabels()
        {
            foreach (var box in new[] { Check1 })
                if (box.Content is System.Windows.Controls.TextBlock label && box.Parent is FrameworkElement body && body.ActualWidth > 0)
                    label.MaxWidth = System.Math.Max(80, body.ActualWidth - box.Margin.Left - box.Margin.Right - 40);
        }

        // A heading is one short line. Text after its first line break moves into the detail, and
        // a first line too long for a heading stays body-sized instead of shouting.
        private void FitHeading()
        {
            string text = HeadingText.Text ?? "";
            int lineBreak = text.IndexOf('\n');
            if (lineBreak >= 0)
            {
                string rest = text.Substring(lineBreak + 1).Trim();
                HeadingText.Text = text.Substring(0, lineBreak).TrimEnd('\r');
                if (rest.Length > 0)
                {
                    string existing = DetailText.Visibility == Visibility.Visible ? DetailText.Text : "";
                    DetailText.Text = string.IsNullOrEmpty(existing) ? rest : rest + "\n\n" + existing;
                    DetailText.Visibility = Visibility.Visible;
                }
            }
            if ((HeadingText.Text ?? "").Length > 90)
            {
                HeadingText.ClearValue(System.Windows.Controls.TextBlock.FontFamilyProperty);
                HeadingText.ClearValue(System.Windows.Media.TextOptions.TextFormattingModeProperty);
                HeadingText.FontSize = 13;
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
    }
}

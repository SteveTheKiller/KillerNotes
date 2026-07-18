using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KillerNotes.Services;

// KillerUI kit.
//
// Title-bar theme + accent pickers. A partial of your MainWindow.
//
// Your MainWindow.xaml is expected to provide:
//   ThemeButton   - Button, Click="ThemeButton_Click"
//   ThemePopup    - Popup anchored to ThemeButton; its child is the flyout Border
//   ThemeSwatches - Panel of theme Buttons, each Style="{StaticResource ThemeSwatch}",
//                   Tag = a Theme name ("Dark","Light","Black","Blood","Greed","Cyanotic"),
//                   Click="ThemeSwatch_Click"
//   AccentSwatches- Panel of accent Buttons, Tag = an Accent name ("Green","Red",...),
//                   Click="AccentSwatch_Click"
//   AccentLabel   - (optional) label shown only for accent-capable themes
//
// Call UpdateThemeSwatchSelection() + UpdateAccentSwatches() once after InitializeComponent
// to seed the rings, and subscribe ThemeManager.ThemeChanged to re-run them on a live change.
namespace KillerNotes
{
    public partial class MainWindow
    {
        private void ThemeSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string name && Enum.TryParse<Theme>(name, out var theme))
            {
                ThemeManager.Apply(theme);
                ApplyThemeBorder(this);   // retint the DWM frame border to the new palette
                UpdateThemeSwatchSelection();
                UpdateAccentSwatches();
            }
        }

        private void AccentSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string name && Enum.TryParse<Accent>(name, out var accent))
            {
                ThemeManager.ApplyAccent(ThemeManager.Current, accent);
                UpdateThemeSwatchSelection();   // ring colours follow the accent
                UpdateAccentSwatches();
            }
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("ThemePopup") is System.Windows.Controls.Primitives.Popup p)
            {
                p.IsOpen = !p.IsOpen;
                if (p.IsOpen && p.Child is UIElement child) Anim.FadeIn(child);
            }
        }

        /// <summary>Highlights the active theme's swatch.</summary>
        private void UpdateThemeSwatchSelection()
            => HighlightSwatches(FindName("ThemeSwatches") as Panel, ThemeManager.Current.ToString());

        /// <summary>Shows the accent dots for accent-capable themes and highlights the active accent.</summary>
        private void UpdateAccentSwatches()
        {
            if (FindName("AccentSwatches") is not Panel panel) return;
            var t = ThemeManager.Current;
            bool hasAccents = t == Theme.Dark || t == Theme.Light || t == Theme.Black;
            var vis = hasAccents ? Visibility.Visible : Visibility.Collapsed;
            panel.Visibility = vis;
            if (FindName("AccentLabel") is UIElement lbl) lbl.Visibility = vis;
            if (hasAccents)
                HighlightSwatches(panel, ThemeManager.AccentChoiceFor(t).ToString());
        }

        private void HighlightSwatches(Panel? panel, string current)
        {
            if (panel == null) return;
            var activeRing = TryFindResource("PrimaryBrush") as Brush ?? Brushes.White;
            var idleRing   = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            foreach (var child in panel.Children)
            {
                if (child is not Button b || b.Tag is not string name) continue;
                bool active = name == current;
                b.BorderBrush     = active ? activeRing : idleRing;
                b.BorderThickness = new Thickness(active ? 2 : 1);
            }
        }
    }
}

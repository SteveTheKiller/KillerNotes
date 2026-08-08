using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private static readonly Theme[] PickerThemes =
        [
            Theme.Dark, Theme.Light, Theme.Black, Theme.SE98, Theme.Blood, Theme.Greed, Theme.Cyanotic,
            Theme.Ectoplasm, Theme.Decay,
            Theme.Malaise, Theme.Sepulchre, Theme.Delirium, Theme.Mourning
        ];
        private static readonly (Accent Accent, string Color)[] PickerAccents =
        [(Accent.Red,"#DD504B"),(Accent.Orange,"#E8962C"),(Accent.Green,"#1EA54C"),(Accent.Teal,"#1FB8A8"),(Accent.Blue,"#50AEE8"),(Accent.Purple,"#B982E3")];
        private static readonly (Accent Accent, string Color)[] SE98Accents =
        [(Accent.Blue,"#000080"),(Accent.Teal,"#008080"),(Accent.Green,"#006000"),(Accent.Orange,"#A05000"),(Accent.Red,"#800040"),(Accent.Purple,"#5A376E")];
        private readonly Dictionary<Theme, RadioButton> _themeRadios = [];
        private readonly Dictionary<Theme, StackPanel> _accentRows = [];
        private const double AccentRowHeight = 26;
        private const double AccentSlideMs = 160;

        private void ThemeButton_Click(object sender, RoutedEventArgs e) => OpenThemeMenu();
        private void UpdateThemeSwatchSelection() { }
        private void UpdateAccentSwatches() { if (ThemeMenu.IsOpen) RefreshAccentDots(); }

        private void OpenThemeMenu()
        {
            if (ThemeMenu.IsOpen) { ThemeMenu.IsOpen = false; return; }
            BuildThemeMenu();
            FlyoutPlacement.UsePane(ContentPane);
            FlyoutPlacement.Attach(ThemeMenu, ThemeButton);
            ThemeMenu.IsOpen = true;
            ThemeMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        private void BuildThemeMenu()
        {
            ThemeMenu.Items.Clear();
            _themeRadios.Clear();
            _accentRows.Clear();
            var panel = new StackPanel { Margin = new Thickness(12,10,14,10) };
            foreach (Theme theme in PickerThemes)
            {
                var radio = new RadioButton { Content = ThemeName(theme), Tag = theme, GroupName = "ThemeGroup",
                    Style = (Style)FindResource("ThemeRadio"), IsChecked = ThemeManager.Current == theme };
                radio.Checked += ThemeRadio_Checked;
                _themeRadios[theme] = radio;
                panel.Children.Add(radio);

                if (HasAccents(theme))
                {
                    var row = BuildAccentRow(theme);
                    bool shown = theme == ThemeManager.Current;
                    row.Height = shown ? AccentRowHeight : 0;
                    row.Visibility = Visibility.Visible;
                    _accentRows[theme] = row;
                    panel.Children.Add(row);
                }
            }
            ThemeMenu.Items.Add(new ScrollViewer
            {
                Content = panel,
                MaxHeight = 620,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            // Fonts... - the entry the flyout rework dropped. The whole Fonts overlay
            // (FontsOverlay, the combos, font import) and its FontsRow_Click handler survived
            // the rework untouched; only the row that opened it vanished, which orphaned the
            // feature for all of 1.2.0's development and left help.html describing a door
            // that no longer existed. The ItemContainerStyle above gives this row the same
            // PanelMenuItem look as every other flyout row.
            ThemeMenu.Items.Add(new Separator());
            var fonts = new MenuItem { Header = FindResource("Str_Fonts_Open") };
            fonts.Click += FontsRow_Click;
            ThemeMenu.Items.Add(fonts);
        }

        private StackPanel BuildAccentRow(Theme theme)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10,-5,0,5), ClipToBounds = true, Tag = theme };
            foreach (var (accent, color) in theme == Theme.SE98 ? SE98Accents : PickerAccents)
            {
                var dot = new Border { Tag = accent, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    Style = (Style)FindResource("AccentDot") };
                dot.MouseLeftButtonUp += AccentDot_Click;
                row.Children.Add(dot);
            }
            return row;
        }

        private static bool HasAccents(Theme t) => t is Theme.Dark or Theme.Light or Theme.Black or Theme.SE98;
        private static string ThemeName(Theme t)
        {
            if (t == Theme.SE98) return "98SE";
            return t.ToString();
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton { Tag: Theme theme } || theme == ThemeManager.Current) return;
            Theme old = ThemeManager.Current;
            // The crossfade lives in ThemeManager.Publish, which animates the palette's brushes
            // in place. Nothing here needs to capture or cover the window - see the comment on
            // the live palette for why every snapshot-based version of this throbbed.
            ThemeManager.Apply(theme);
            ApplyThemeBorder(this);
            ApplyThemeElevation();
            RefreshAccentDots();
            if (HasAccents(old) && HasAccents(theme))
                AnimateNeutralRowsInVisualOrder(old, theme);
            else
            {
                foreach (var pair in _accentRows) SetAccentRow(pair.Value, pair.Key == theme);
            }
        }

        /// <summary>98SE is intentionally flat. Clearing the effects themselves is more robust
        /// than changing only their opacity because WPF can retain a shared Freezable effect's
        /// previously resolved DynamicResource value across an in-place dictionary update.</summary>
        private void ApplyThemeElevation()
        {
            if (ThemeManager.Current == Theme.SE98)
            {
                ContentPane.Effect = null;
                FormatBar.Effect = null;
                return;
            }

            // Do not reuse the resource Freezables here. After 98SE resolves their dynamic
            // opacity to zero, reattaching those same objects can leave them permanently flat.
            ContentPane.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = .60,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
            };
            FormatBar.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = .38,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
            };
        }

        // Order matters. Updating top-to-bottom is KillerPDF's exact behavior: when the incoming
        // row is above the outgoing one (Light -> Dark), it starts opening before the lower row
        // starts closing. Reversing those calls produces a one-layout-pass upward jump.
        private void AnimateNeutralRowsInVisualOrder(Theme old, Theme selected)
        {
            // Establish a constant-height start state before WPF gets another render pass. The
            // actual animations then begin together at Render priority; no intermediate popup
            // measurement can observe one row changed and the other not yet changed.
            foreach (Theme theme in new[] { Theme.Dark, Theme.Light, Theme.Black, Theme.SE98 })
            {
                var row = _accentRows[theme];
                row.BeginAnimation(HeightProperty, null);
                row.Height = theme == old ? AccentRowHeight : 0;
            }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                new Action(() =>
                {
                    foreach (Theme theme in new[] { Theme.Dark, Theme.Light, Theme.Black, Theme.SE98 })
                        if (theme == old || theme == selected)
                            SlideAccentRow(_accentRows[theme], theme == selected);
                }));
        }

        private static void SlideAccentRow(FrameworkElement row, bool show)
        {
            row.BeginAnimation(HeightProperty, null);
            if (show)
            {
                row.Height = 0;
                row.BeginAnimation(HeightProperty,
                    new DoubleAnimation(0, AccentRowHeight, TimeSpan.FromMilliseconds(AccentSlideMs)));
            }
            else if (row.ActualHeight > 0.5)
            {
                row.Height = AccentRowHeight;
                row.BeginAnimation(HeightProperty,
                    new DoubleAnimation(AccentRowHeight, 0, TimeSpan.FromMilliseconds(AccentSlideMs)));
            }
            else row.Height = 0;
        }

        private static void SetAccentRow(FrameworkElement row, bool show)
        {
            row.BeginAnimation(HeightProperty, null);
            row.Height = show ? AccentRowHeight : 0;
        }

        private void RefreshAccentDots()
        {
            var ring = TryFindResource("TextBrush") as Brush ?? Brushes.White;
            foreach (var pair in _accentRows)
                foreach (Border dot in pair.Value.Children)
                    dot.BorderBrush = dot.Tag is Accent accent && accent == ThemeManager.AccentChoiceFor(pair.Key)
                        ? ring : Brushes.Transparent;
        }

        private void AccentDot_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: Accent accent })
            {
                ThemeManager.ApplyAccent(ThemeManager.Current, accent);
                RefreshAccentDots();
                e.Handled = true;
            }
        }
    }
}

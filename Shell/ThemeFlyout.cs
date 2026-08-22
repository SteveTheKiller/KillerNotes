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
        private static readonly (Accent Accent, string Color)[] DarkStripColors =
        [(Accent.Red,"#DD504B"),(Accent.Orange,"#E8962C"),(Accent.Green,"#1EA54C"),(Accent.Teal,"#1FB8A8"),(Accent.Blue,"#4580D9"),(Accent.Purple,"#B982E3")];
        private static readonly (Accent Accent, string Color)[] LightStripColors =
        [(Accent.Red,"#931A1A"),(Accent.Orange,"#C7710F"),(Accent.Green,"#1B5E20"),(Accent.Teal,"#0D827E"),(Accent.Blue,"#18608E"),(Accent.Purple,"#5A1690")];
        private static readonly (Accent Accent, string Color)[] BlackStripColors =
        [(Accent.Red,"#FF2929"),(Accent.Orange,"#FF910A"),(Accent.Green,"#00FF66"),(Accent.Teal,"#0AFFE7"),(Accent.Blue,"#298DFF"),(Accent.Purple,"#B829FF")];
        private static readonly (Accent Accent, string Color)[] SE98StripColors =
        [(Accent.Red,"#800040"),(Accent.Orange,"#A05000"),(Accent.Green,"#006000"),(Accent.Teal,"#008080"),(Accent.Blue,"#000080"),(Accent.Purple,"#5A376E")];
        private readonly Dictionary<Theme, RadioButton> _themeRadios = [];
        private readonly List<Border> _accentStripDots = [];
        private Grid? _accentStripHost;
        private Grid? _accentStrip;
        private Theme _stripFamily = Theme.Dark;
        private bool _stripOpen;
        private const double AccentStripWidth = 39;
        private const double AccentStripSlideMs = 180;

        private void ThemeButton_Click(object sender, RoutedEventArgs e) => OpenThemeMenu();
        private void UpdateThemeSwatchSelection() { }
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
            _accentStripDots.Clear();
            var picker = new Grid { Margin = new Thickness(12,10,3,10) };
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var panel = new StackPanel { Width = 120 };
            Grid.SetColumn(panel, 0);
            picker.Children.Add(panel);
            foreach (Theme theme in PickerThemes)
            {
                var radio = new RadioButton { Content = ThemeName(theme), Tag = theme, GroupName = "ThemeGroup",
                    Style = (Style)FindResource("ThemeRadio"), IsChecked = ThemeManager.Current == theme };
                radio.Checked += ThemeRadio_Checked;
                _themeRadios[theme] = radio;
                panel.Children.Add(radio);
            }
            picker.Children.Add(BuildAccentStrip());
            ThemeMenu.Items.Add(new ScrollViewer
            {
                Content = picker,
                MaxHeight = 620,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            UpdateAccentStrip(animate: false);
            // Fonts... - the entry the flyout rework dropped. The whole Fonts overlay
            // (FontsOverlay, the combos, font import) and its FontsRow_Click handler survived
            // the rework untouched; only the row that opened it vanished, which orphaned the
            // feature for all of 1.2.0's development and left help.html describing a door
            // that no longer existed. The ItemContainerStyle above gives this row the same
            // PanelMenuItem look as every other flyout row.
            // BOTH items carry EXPLICIT styles, and that is load-bearing, not cosmetic: the
            // flyout's ItemContainerStyle is TargetType=MenuItem, and WPF applies it to EVERY
            // container it generates - including a Separator, where the TargetType mismatch
            // THROWS as the menu opens. A bare `new Separator()` here crashed the app on the
            // first theme-button click (2026-08-08). An explicit local style stops the
            // ItemContainerStyle from being applied; the keyed alias keeps it themed, and the
            // implicit MenuItem style gives the Fonts row real hover chrome instead of
            // PanelMenuItem's bare ContentPresenter.
            ThemeMenu.Items.Add(new Separator { Style = (Style)FindResource(MenuItem.SeparatorStyleKey) });
            var fonts = new MenuItem
            {
                Header = FindResource("Str_Fonts_Open"),
                Style = (Style)FindResource(typeof(MenuItem)),
            };
            fonts.Click += FontsRow_Click;
            ThemeMenu.Items.Add(fonts);
        }

        private Grid BuildAccentStrip()
        {
            _accentStripHost = new Grid { Width = 0, ClipToBounds = true };
            Grid.SetColumn(_accentStripHost, 1);
            _accentStripHost.Children.Add(new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 6),
                Background = (Brush)FindResource("MenuBorderBrush")
            });
            _accentStrip = new Grid { Margin = new Thickness(7, 6, 2, 6) };
            for (int i = 0; i < 6; i++)
            {
                _accentStrip.RowDefinitions.Add(new RowDefinition());
                var dot = new Border
                {
                    Style = (Style)FindResource("AccentDot"),
                    Width = 26,
                    Height = double.NaN,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, i == 5 ? 0 : 8)
                };
                dot.MouseLeftButtonUp += AccentDot_Click;
                Grid.SetRow(dot, i);
                _accentStripDots.Add(dot);
                _accentStrip.Children.Add(dot);
            }
            _accentStripHost.Children.Add(_accentStrip);
            return _accentStripHost;
        }

        private static bool HasAccents(Theme t) => t is Theme.Dark or Theme.Light or Theme.Black or Theme.SE98;
        /// <summary>Localized display name for a theme, keyed Str_Theme_&lt;member&gt; - the same keys
        /// the rest of the family uses, so a translation is shared key for key across the apps.
        /// The picker showed raw enum members in every language until 1.3.0.
        /// SE98 is spelled 98SE for the user (a C# enum member cannot start with a digit) and the
        /// resource carries that in every locale, so the fallback below is the only place it is
        /// spelled out in code.</summary>
        private string ThemeName(Theme t)
        {
            string key = "Str_Theme_" + t;
            string name = Loc(key);
            // Loc hands back the key itself when a locale has no entry. Fall back to the enum
            // member so a theme added before its translations reads as a name, not as Str_Theme_X.
            return name == key ? (t == Theme.SE98 ? "98SE" : t.ToString()) : name;
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
            // Corner preference is owned by ApplyCornerState (never ApplyThemeBorder - see its
            // comment): re-evaluate here because 98SE squares even a floating window.
            ApplyCornerState();
            ApplyThemeElevation();
            UpdateAccentStrip(animate: true);
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

        private static (Accent Accent, string Color)[] StripColorsFor(Theme family) => family switch
        {
            Theme.Light => LightStripColors,
            Theme.Black => BlackStripColors,
            Theme.SE98 => SE98StripColors,
            _ => DarkStripColors,
        };

        private void PopulateAccentStrip(Theme family)
        {
            var colors = StripColorsFor(family);
            for (int i = 0; i < _accentStripDots.Count; i++)
            {
                _accentStripDots[i].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i].Color));
                _accentStripDots[i].Tag = colors[i].Accent;
            }
            _stripFamily = family;
            RingAccentStrip();
        }

        private void RingAccentStrip()
        {
            if (_accentStrip is null) return;
            var ring = TryFindResource("TextBrush") as Brush ?? Brushes.White;
            var chosen = ThemeManager.AccentChoiceFor(_stripFamily);
            foreach (var dot in _accentStripDots)
                dot.BorderBrush = dot.Tag is Accent accent && accent == chosen ? ring : Brushes.Transparent;
        }

        private void UpdateAccentStrip(bool animate)
        {
            var current = ThemeManager.Current;
            bool show = HasAccents(current);
            if (show)
            {
                if (animate && _stripOpen && _stripFamily != current && _accentStrip is not null)
                {
                    var target = current;
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90));
                    fadeOut.Completed += (_, _) =>
                    {
                        PopulateAccentStrip(target);
                        _accentStrip.BeginAnimation(OpacityProperty,
                            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90)));
                    };
                    _accentStrip.BeginAnimation(OpacityProperty, fadeOut);
                }
                else PopulateAccentStrip(current);
            }
            SlideAccentStrip(show, animate);
        }

        private void SlideAccentStrip(bool show, bool animate)
        {
            if (_accentStripHost is null) return;
            if (show == _stripOpen && animate) return;
            _stripOpen = show;
            _accentStripHost.BeginAnimation(WidthProperty, null);
            if (!animate)
            {
                _accentStripHost.Width = show ? AccentStripWidth : 0;
                return;
            }
            double from = double.IsNaN(_accentStripHost.Width) ? _accentStripHost.ActualWidth : _accentStripHost.Width;
            var animation = new DoubleAnimation(from, show ? AccentStripWidth : 0,
                TimeSpan.FromMilliseconds(AccentStripSlideMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += (_, _) =>
            {
                _accentStripHost.BeginAnimation(WidthProperty, null);
                _accentStripHost.Width = _stripOpen ? AccentStripWidth : 0;
            };
            _accentStripHost.BeginAnimation(WidthProperty, animation);
        }

        private void AccentDot_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: Accent accent })
            {
                ThemeManager.ApplyAccent(ThemeManager.Current, accent);
                RingAccentStrip();
                e.Handled = true;
            }
        }
    }
}

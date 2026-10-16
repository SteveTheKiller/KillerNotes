#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KillerFamily.Theming
{
    /// <summary>
    /// Light with the Yellow accent keeps its accent yellow and draws a dark shadow behind every
    /// yellow text and glyph so it reads on the light surfaces, the same halo the wordmark and the
    /// rail icons use. A theme turns it on by giving WordmarkAccentShadowOpacity a value above 0;
    /// every other theme leaves that at 0 and nothing changes. Call Initialize once at startup.
    /// </summary>
    internal static class AccentShadow
    {
        private static bool _initialized;
        private static readonly IValueConverter Shadow = new ShadowConverter();
        private static readonly DependencyProperty TrackedProperty = DependencyProperty.RegisterAttached(
            "Tracked", typeof(bool), typeof(AccentShadow), new PropertyMetadata(false));
        private static readonly string[] AccentKeys =
            { "PrimaryBrush", "PrimaryHoverBrush", "PrimaryPressedBrush", "AccentLogo", "OutlineBtnBrush",
              "HeaderLineBrush", "SelectionAccent", "RadioAccent" };

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler(OnTextSizeChanged), true);
        }

        private static void OnTextSizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (sender is not TextBlock text || (bool)text.GetValue(TrackedProperty)) return;
            text.SetValue(TrackedProperty, true);
            // Text that already draws its own effect, such as a wordmark halo copy, keeps it.
            if (text.Effect != null || BindingOperations.IsDataBound(text, UIElement.EffectProperty)) return;
            // Bound to the text's own color, so hover and selection colors and theme changes
            // add or remove the shadow as the color changes.
            text.SetBinding(UIElement.EffectProperty, new Binding(nameof(TextBlock.Foreground))
            {
                RelativeSource = RelativeSource.Self,
                Converter = Shadow,
            });
        }

        /// <summary>True when the theme asks for the shadow and the color is one of its accent colors.</summary>
        internal static bool Applies(Brush? brush)
        {
            var app = Application.Current;
            if (app == null || brush is not SolidColorBrush solid) return false;
            if (!(app.TryFindResource("WordmarkAccentShadowOpacity") is double opacity) || opacity <= 0) return false;
            foreach (string key in AccentKeys)
                if (app.TryFindResource(key) is SolidColorBrush accent && accent.Color == solid.Color)
                    return true;
            return false;
        }

        private sealed class ShadowConverter : IValueConverter
        {
            private DropShadowEffect? _effect;
            private double _opacity = -1;

            public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (!Applies(value as Brush)) return null;
                double opacity = (double)Application.Current.FindResource("WordmarkAccentShadowOpacity");
                if (_effect == null || _opacity != opacity)
                {
                    // Matches the wordmark halo: black, a little below and right, softly blurred.
                    _effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 300,
                        ShadowDepth = 1.5,
                        BlurRadius = 3,
                        Opacity = opacity,
                    };
                    _effect.Freeze();
                    _opacity = opacity;
                }
                return _effect;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
                Binding.DoNothing;
        }
    }
}

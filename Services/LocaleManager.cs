using System;
using System.Windows;

namespace KillerNotes.Services
{
    // Mirrors the KillerScan/KillerPDF LocaleManager. en-US.xaml is always the base layer
    // so any locale that omits a key falls back to English; the chosen locale's file is
    // layered on top. MergedDictionaries layout here:
    //   [0] theme colors (ThemeManager, updated in place)
    //   [1] Controls.xaml
    //   [2] Strings/en-US.xaml  - always present (English base)
    //   [3] the chosen locale's overrides (absent for English)
    internal enum Locale { EnUS, Es, ZhTW, ZhCN, Bn, TrTR, De, Fr, Ja }

    internal static class LocaleManager
    {
        private static Locale _current = Locale.EnUS;
        public static Locale Current => _current;

        /// <summary>Call once at startup (after ThemeManager.Initialize) to restore the saved locale.</summary>
        public static void Initialize()
        {
            var saved = App.GetSetting("Locale");
            _current = Enum.TryParse<Locale>(saved, out var l) ? l : Locale.EnUS;
            ApplyInternal(_current);
        }

        /// <summary>Switch locale, persist the choice, and hot-swap the string ResourceDictionary.</summary>
        public static void Apply(Locale locale)
        {
            _current = locale;
            App.SetSetting("Locale", locale.ToString());
            ApplyInternal(locale);
        }

        private static void ApplyInternal(Locale locale)
        {
            var merged = Application.Current.Resources.MergedDictionaries;

            var enUS = new ResourceDictionary { Source = new Uri("pack://application:,,,/Strings/en-US.xaml") };
            if (merged.Count > 2) merged[2] = enUS; else merged.Add(enUS);

            Uri? overrideUri = locale switch
            {
                Locale.Es   => new Uri("pack://application:,,,/Strings/es.xaml"),
                Locale.Fr   => new Uri("pack://application:,,,/Strings/fr-FR.xaml"),
                Locale.ZhTW => new Uri("pack://application:,,,/Strings/zh-TW.xaml"),
                Locale.ZhCN => new Uri("pack://application:,,,/Strings/zh-CN.xaml"),
                Locale.Bn   => new Uri("pack://application:,,,/Strings/bn.xaml"),
                Locale.TrTR => new Uri("pack://application:,,,/Strings/tr-TR.xaml"),
                Locale.De   => new Uri("pack://application:,,,/Strings/de-DE.xaml"),
                Locale.Ja   => new Uri("pack://application:,,,/Strings/ja-JP.xaml"),
                _           => null,   // English: base only
            };

            if (overrideUri is not null)
            {
                try
                {
                    var ov = new ResourceDictionary { Source = overrideUri };
                    if (merged.Count > 3) merged[3] = ov; else merged.Add(ov);
                }
                catch
                {
                    // Locale file not present yet (or invalid) - stay on the English base.
                    if (merged.Count > 3) merged.RemoveAt(3);
                }
            }
            else if (merged.Count > 3)
            {
                merged.RemoveAt(3);
            }
        }
    }
}

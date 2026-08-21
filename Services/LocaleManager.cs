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
    internal enum Locale { EnUS, Es, ZhTW, ZhCN, Bn, TrTR, De, Fr, Ja, Cs, PlPL, HuHU }

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

        // The two dictionaries THIS class owns, tracked by reference. They used to be addressed by
        // index - merged[2] and merged[3] - which silently destroyed whatever else happened to sit
        // there. App.xaml's PickerStyles.xaml was at [2], so the first locale apply at startup
        // overwrote it and every StaticResource into it (the picker's PickerViewBtn, FolderTreeItem,
        // row templates and file-type icons) stopped resolving. Index-based slots in a shared
        // collection are a trap: anything appended to App.xaml silently lands in someone's slot.
        private static ResourceDictionary? _base;
        private static ResourceDictionary? _override;

        private static void ApplyInternal(Locale locale)
        {
            var merged = Application.Current.Resources.MergedDictionaries;

            // Replace our own entries in place if present, otherwise append. Never index.
            var enUS = new ResourceDictionary { Source = new Uri("pack://application:,,,/Strings/en-US.xaml") };
            int baseAt = _base is null ? -1 : merged.IndexOf(_base);
            if (baseAt >= 0) merged[baseAt] = enUS; else merged.Add(enUS);
            _base = enUS;

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
                Locale.Cs   => new Uri("pack://application:,,,/Strings/cs-CZ.xaml"),
                Locale.PlPL => new Uri("pack://application:,,,/Strings/pl-PL.xaml"),
                Locale.HuHU => new Uri("pack://application:,,,/Strings/hu-HU.xaml"),
                _           => null,   // English: base only
            };

            // Drop the previous override (by reference) before adding the new one, so switching
            // locale repeatedly cannot stack dictionaries or delete a neighbor's.
            if (_override is not null && merged.Contains(_override)) merged.Remove(_override);
            _override = null;

            if (overrideUri is not null)
            {
                try
                {
                    var ov = new ResourceDictionary { Source = overrideUri };
                    merged.Add(ov);
                    _override = ov;
                }
                catch
                {
                    // Locale file not present yet (or invalid) - stay on the English base.
                }
            }
        }
    }
}

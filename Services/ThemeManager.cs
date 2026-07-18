using System;
using System.Windows;

// KillerUI kit.
namespace KillerNotes.Services
{
    public enum Theme { Dark, Light, Black, Blood, Greed, Cyanotic }

    // Accent-hue variants for the accent-capable families (Dark, Light, Black).
    // Green is the base theme (no overlay); the others apply a small overlay
    // dictionary that recolours only the accent-family keys.
    public enum Accent { Green, Red, Blue, Purple, Orange, Teal }

    /// <summary>
    /// Swaps the theme colour dictionary (MergedDictionaries[0]) in place at runtime.
    /// Control styles live in Controls.xaml and bind brushes via DynamicResource, so an
    /// in-place per-key update repaints everything without structural churn.
    ///
    /// Persistence is decoupled: wire GetSetting/SetSetting to your storage (registry,
    /// JSON, etc.) at startup if you want the choice to survive restarts. Left unset,
    /// the theme still works for the session, it just won't be remembered.
    ///
    /// REQUIRES (as app resources, merged in App.xaml before Controls.xaml):
    ///   MergedDictionaries[0] = a Themes/{Theme}.xaml colour dictionary.
    /// Colour dictionaries + Accents/ overlays are app-neutral; copy them from KillerScan.
    /// </summary>
    public static class ThemeManager
    {
        // ---- Persistence hooks (optional). Default: in-memory only. ----
        public static Func<string, string?> GetSetting { get; set; } = _ => null;
        public static Action<string, string> SetSetting { get; set; } = (_, _) => { };

        // Default theme/accent when nothing is stored. Tweak per app if you like.
        private static Theme _current = Theme.Black;
        private static Accent _darkAccent  = Accent.Green;
        private static Accent _lightAccent = Accent.Green;
        private static Accent _blackAccent = Accent.Purple;   // KillerNotes default: Black + Purple

        public static Theme Current => _current;
        public static Accent AccentChoiceFor(Theme t) => AccentFor(t);

        private static Accent AccentFor(Theme t) =>
            t == Theme.Light ? _lightAccent : t == Theme.Black ? _blackAccent : _darkAccent;

        // Only these families carry accent-hue overlays.
        private static bool HasAccents(Theme t) =>
            t == Theme.Dark || t == Theme.Light || t == Theme.Black;

        /// <summary>Fired after the theme dictionary has been updated.</summary>
        public static event Action? ThemeChanged;

        /// <summary>Call once at startup, before the main window is created, to restore the saved theme.</summary>
        public static void Initialize()
        {
            _current     = Enum.TryParse<Theme>(GetSetting("Theme"),        out var t)  ? t  : _current;
            _darkAccent  = Enum.TryParse<Accent>(GetSetting("DarkAccent"),  out var da) ? da : _darkAccent;
            _lightAccent = Enum.TryParse<Accent>(GetSetting("LightAccent"), out var la) ? la : _lightAccent;
            _blackAccent = Enum.TryParse<Accent>(GetSetting("BlackAccent"), out var ba) ? ba : _blackAccent;
            LoadDict(_current);
        }

        /// <summary>Change theme, persist the choice, and repaint.</summary>
        public static void Apply(Theme theme)
        {
            _current = theme;
            SetSetting("Theme", theme.ToString());
            LoadDict(theme);
            ThemeChanged?.Invoke();
        }

        /// <summary>
        /// Change a family's accent hue, persist it, and reapply if that family is active.
        /// Dark/Light/Black keep independent accents, so changing one never disturbs another.
        /// </summary>
        public static void ApplyAccent(Theme family, Accent accent)
        {
            if      (family == Theme.Light) { _lightAccent = accent; SetSetting("LightAccent", accent.ToString()); }
            else if (family == Theme.Black) { _blackAccent = accent; SetSetting("BlackAccent", accent.ToString()); }
            else                            { _darkAccent  = accent; SetSetting("DarkAccent",  accent.ToString()); }

            if (_current == family)
            {
                LoadDict(_current);
                ThemeChanged?.Invoke();
            }
        }

        private static void LoadDict(Theme theme)
        {
            var uri = new Uri($"pack://application:,,,/Themes/{theme}.xaml");
            var newDict = new ResourceDictionary { Source = uri };
            var merged  = Application.Current.Resources.MergedDictionaries;

            // In-place per-key update: fires a targeted change notification for each key without
            // structurally modifying MergedDictionaries (a structural swap fires a synchronous
            // ResourcesChanged that can re-enter lookups before the new dict is fully in place).
            if (merged.Count > 0)
            {
                var existing = merged[0];
                foreach (object key in newDict.Keys)
                    existing[key] = newDict[key];
            }
            else
            {
                merged.Add(newDict);
            }

            // Accent overlay: Dark/Light/Black recolour their accent-family keys on top of the base
            // green. Green is the base itself, so it needs no overlay. Overlays live in Accents/<Family>/.
            var accent = AccentFor(theme);
            if (HasAccents(theme) && accent != Accent.Green)
            {
                string family = theme == Theme.Light ? "Light" : theme == Theme.Black ? "Black" : "Dark";
                try
                {
                    var accentDict = new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/Themes/Accents/{family}/{accent}.xaml")
                    };
                    var target = merged[0];
                    foreach (object key in accentDict.Keys)
                        target[key] = accentDict[key];
                }
                catch { /* overlay file not present - base theme stands */ }
            }
        }
    }
}

using System;
using System.Windows;
using System.Windows.Media;

// KillerUI kit.
namespace KillerNotes.Services
{
    public enum Theme
    {
        Dark, Light, Black, SE98, Blood, Greed, Cyanotic, Ectoplasm, Decay,
        Mourning, Sepulchre, Delirium, Malaise
    }

    // Accent-hue variants for the accent-capable families (Dark, Light, Black).
    // Green is the base theme (no overlay); the others apply a small overlay
    // dictionary that recolours only the accent-family keys.
    public enum Accent { Green, Red, Blue, Purple, Orange, Teal }

    /// <summary>
    /// Builds a complete colour palette per theme and publishes it as MergedDictionaries[0] in a
    /// single assignment. Control styles bind through DynamicResource, so replacing the dictionary
    /// invalidates and repaints every surface at once. Theme changes are instant by design - see
    /// the comment on Publish for the transitions that were tried and why they were reverted.
    ///
    /// Persistence is decoupled: wire GetSetting/SetSetting to your storage (registry,
    /// JSON, etc.) at startup if you want the choice to survive restarts. Left unset,
    /// the theme still works for the session, it just won't be remembered.
    ///
    /// REQUIRES (as app resources, merged in App.xaml before Controls.xaml):
    ///   MergedDictionaries[0] = a Themes/{Theme}.xaml colour dictionary.
    /// Shared trademark tokens are overlaid from KillerUI; this app dictionary contains
    /// only compatibility defaults and KillerNotes-specific resources.
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
        private static Accent _se98Accent = Accent.Blue;

        public static Theme Current => _current;
        public static Accent AccentChoiceFor(Theme t) => AccentFor(t);

        private static Accent AccentFor(Theme t) =>
            t == Theme.Light ? _lightAccent : t == Theme.Black ? _blackAccent :
            t == Theme.SE98 ? _se98Accent : _darkAccent;

        // Only these families carry accent-hue overlays.
        private static bool HasAccents(Theme t) =>
            t == Theme.Dark || t == Theme.Light || t == Theme.Black || t == Theme.SE98;

        /// <summary>Fired after the theme dictionary has been updated.</summary>
        public static event Action? ThemeChanged;

        /// <summary>Call once at startup, before the main window is created, to restore the saved theme.</summary>
        public static void Initialize()
        {
            string? savedTheme = GetSetting("Theme");
            // Migrate the four surviving palette-lab IDs to their permanent names. All other
            // discarded RetroXX experiments safely return to Black rather than seeking a file
            // that no longer exists.
            _current = savedTheme switch
            {
                "Retro06" => Theme.Malaise,
                "Retro10" or "CoffeeSignboard" => Theme.Sepulchre,
                "Retro16" or "MovingQuickly" => Theme.Delirium,
                "Retro24" or "MultiEthnic" => Theme.Mourning,
                "Retro" => Theme.Ectoplasm,
                "Earthy" => Theme.Decay,
                _ when Enum.TryParse<Theme>(savedTheme, out var parsed) => parsed,
                _ => Theme.Black
            };
            if (savedTheme is not null && savedTheme != _current.ToString())
                SetSetting("Theme", _current.ToString());
            _darkAccent  = Enum.TryParse<Accent>(GetSetting("DarkAccent"),  out var da) ? da : _darkAccent;
            _lightAccent = Enum.TryParse<Accent>(GetSetting("LightAccent"), out var la) ? la : _lightAccent;
            _blackAccent = Enum.TryParse<Accent>(GetSetting("BlackAccent"), out var ba) ? ba : _blackAccent;
            _se98Accent = Enum.TryParse<Accent>(GetSetting("98SEAccent"), out var wa) ? wa : _se98Accent;
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
            else if (family == Theme.SE98) { _se98Accent = accent; SetSetting("98SEAccent", accent.ToString()); }
            else                            { _darkAccent  = accent; SetSetting("DarkAccent",  accent.ToString()); }

            if (_current == family)
            {
                LoadDict(_current);
                ThemeChanged?.Invoke();
            }
        }

        /// <summary>File stem for a theme. Only SE98 differs, because an enum member cannot
        /// start with a digit but the file and the accent folder are both named "98SE".</summary>
        private static string ThemeFileName(Theme theme) =>
            theme == Theme.SE98 ? "98SE" : theme.ToString();

        private static void SetIfAbsent(ResourceDictionary dict, string key, object value)
        {
            if (!dict.Contains(key)) dict[key] = value;
        }

        /// <summary>
        /// Attaches a fully built palette as MergedDictionaries[0], in ONE assignment.
        ///
        /// There is deliberately no transition here. Animating the brushes in place was tried and
        /// reverted: mutating a brush's Colour fires no resource-changed notification, so only the
        /// surfaces that happen to hold that exact brush object repaint. The result was a theme
        /// applying to some of the window and not the rest - the sidebar recoloured while the
        /// editor pane and the Killculator stayed on the previous palette. A correct repaint needs
        /// the invalidation that replacing the dictionary provides, and correctness beats a fade.
        ///
        /// One assignment, never key-by-key: a per-key loop can overwrite a key but cannot REMOVE
        /// one, so any key only some themes define leaks into every theme chosen afterwards (98SE's
        /// yellow AccentLogo followed every subsequent theme). It also fired a full invalidation
        /// pass per key, 150+ tree walks per click, instead of one.
        /// </summary>
        private static void Publish(ResourceDictionary target)
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count > 0) merged[0] = target;
            else merged.Add(target);
        }

        private static void LoadDict(Theme theme)
        {
            // EVERY theme is a complete, standalone palette in exactly two halves: the
            // app-specific colours in Themes/<Name>.xaml and the shared contract tokens in
            // KillerUI/Themes/<Name>.xaml. No theme inherits another and none is generated at
            // runtime - the former Cyanotic/Light base-plus-patch forms and the four
            // RetroPaletteCatalog palettes were flattened into files of their own, so adding a
            // theme means adding two files and an enum entry, nothing else.
            string name = ThemeFileName(theme);
            var newDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/{name}.xaml")
            };
            KillerThemeContract.Apply(newDict, name);

            // Material tokens: defaults only. A theme that states its own keeps them, which is
            // how 98SE stays flat (no shadows, hard 2px frame, raised bevels) without a branch
            // here. Set before the palette can be read, never over it.
            SetIfAbsent(newDict, "PaneShadowOpacity", 0.60);
            SetIfAbsent(newDict, "BarShadowOpacity", 0.38);
            SetIfAbsent(newDict, "FlyoutShadowOpacity", 0.55);
            SetIfAbsent(newDict, "WindowFrameThickness", new Thickness(0));
            SetIfAbsent(newDict, "BevelLightBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "BevelDarkBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "BevelLightThickness", new Thickness(0));
            SetIfAbsent(newDict, "BevelDarkThickness", new Thickness(0));
            if (!newDict.Contains("SortButtonBrush") && newDict.Contains("PaneBrush"))
                newDict["SortButtonBrush"] = newDict["PaneBrush"];
            // The 1px line ringing the window (RootBorder). Same as the app border unless a theme
            // says otherwise - 98SE makes it transparent, because on a bevelled theme it lands
            // outside the dark bottom/right bevel as a bright stripe.
            if (!newDict.Contains("WindowEdgeBrush") && newDict.Contains("AppBorderBrush"))
                newDict["WindowEdgeBrush"] = newDict["AppBorderBrush"];
            // RootBorder's thickness. It is the PARENT of everything, so its border sits OUTSIDE
            // the bevel layer - a transparent brush is not enough to hide it, because the window's
            // own Background then shows through the same 1px band. A bevelled theme sets this to 0
            // so its bevel reaches the true window edge.
            SetIfAbsent(newDict, "WindowEdgeThickness", new Thickness(1));
            // A button's flat edge. Bevelled themes make it transparent so the bevel is the edge
            // instead of a second line drawn beside it.
            if (!newDict.Contains("ButtonEdgeBrush") && newDict.Contains("CardBorderBrush"))
                newDict["ButtonEdgeBrush"] = newDict["CardBorderBrush"];

            // ---- Dialog caption bands (SketchPad, colour picker, file picker) ----
            //
            // Transparent by DEFAULT, so the band shows the card's own face and is invisible - the
            // family look, where a dialog's title blends into the surface. Painting these with
            // TitleBarBrush unconditionally gave every theme a visible caption, and on the themes
            // whose background is a gradient it also re-ramped that gradient across the short band,
            // so it could never line up with the card behind it.
            //
            // A theme that genuinely wants a distinct caption declares UseDialogCaption and gets
            // its own TitleBarBrush - read AFTER the accent overlay, so it picks up the accent's
            // gradient rather than the base one. No theme is named here.
            newDict["DialogTitleBarBrush"] = newDict.Contains("UseDialogCaption") && newDict.Contains("TitleBarBrush")
                ? newDict["TitleBarBrush"]
                : new SolidColorBrush(Colors.Transparent);
            // Caption height and the transparent halo a dialog leaves around itself for its drop
            // shadow. A flat theme wants a short caption and NO halo - the halo is where the resize
            // grab lives, so an invisible one puts the grab handle outside the visible window.
            SetIfAbsent(newDict, "DialogTitleBarHeight", 28.0);
            SetIfAbsent(newDict, "DialogHaloMargin", new Thickness(10));
            // Accent overlay: Dark/Light/Black recolour their accent-family keys on top of the base
            // green. Green is the base itself, so it needs no overlay. Overlays live in Accents/<Family>/.
            var accent = AccentFor(theme);
            if (HasAccents(theme) && accent != Accent.Green)
            {
                string family = ThemeFileName(theme);
                try
                {
                    var accentDict = new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/Themes/Accents/{family}/{accent}.xaml")
                    };
                    foreach (object key in accentDict.Keys)
                        newDict[key] = accentDict[key];
                }
                catch { /* overlay file not present - base theme stands */ }
            }
            // Button edge at rest. Default is the accent outline; a theme that wants a different
            // one states it in its own file. No theme is named here - 98SE used to be special-cased
            // to Transparent, which left its buttons with NO edge at all (the "raised gray control"
            // that was supposed to replace the outline was never drawn), so the New note button
            // rendered as bare text with its padding invisible.
            if (!newDict.Contains("OutlineRestBrush") && newDict.Contains("OutlineBtnBrush"))
                newDict["OutlineRestBrush"] = newDict["OutlineBtnBrush"];

            // AccentLogo (title-bar wordmark) and BgFlyout (format bar) are KillerPDF-vocabulary
            // keys that only the newer themes declare. Rather than hand-adding them to the six
            // original palettes, default them from colours those palettes already define, AFTER
            // the accent overlay so the wordmark tracks the live accent. A theme that sets either
            // key itself keeps its own value - this is what preserves 98SE's yellow wordmark and
            // its raised-grey flyout, and Ectoplasm's and Decay's overrides.
            //
            // The wordmark follows HeaderLineBrush, NOT PrimaryBrush. On Blood, Greed and Cyanotic
            // the KillerUI palette deliberately sets PrimaryBrush to #ffffff - white is those
            // themes' button fill - and keeps the signature colour (#e8485a / #3fbf6f / #3aa0d8)
            // in HeaderLineBrush. Since KillerThemeContract.Apply runs after the local theme file,
            // that white wins, and sourcing the logo from PrimaryBrush painted those three white.
            // HeaderLineBrush is the accent on every theme and in all 21 accent overlays.
            if (!newDict.Contains("AccentLogo") && newDict.Contains("HeaderLineBrush"))
                newDict["AccentLogo"] = newDict["HeaderLineBrush"];
            if (!newDict.Contains("BgFlyout") && newDict.Contains("MenuBackgroundBrush"))
                newDict["BgFlyout"] = newDict["MenuBackgroundBrush"];

            Publish(newDict);
        }
    }
}

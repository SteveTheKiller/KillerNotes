using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace KillerNotes.Services
{
    /// <summary>The four palette themes promoted from the 1.2 theme lab.</summary>
    internal static class RetroPaletteCatalog
    {
        private static readonly Dictionary<Theme, string[]> Palettes = new()
        {
            [Theme.Malaise] = ["#bd00c6", "#26222e", "#df260c", "#0bc2c6", "#ead900"],
            [Theme.Sepulchre] = ["#292d2f", "#347c80", "#986632", "#705827"],
            [Theme.Delirium] = ["#cf1020", "#3a2b78", "#20a8b5", "#5fce00", "#9a9b00", "#dd8500"],
            [Theme.Mourning] = ["#ffb199", "#ff6f91", "#f9f871", "#a0ffe6", "#4b4453"]
        };

        internal static bool Contains(Theme theme) => Palettes.ContainsKey(theme);

        internal static void Apply(Theme theme, ResourceDictionary target)
        {
            string[] palette = Palettes[theme];
            Color A(int offset) => Parse(palette[offset % palette.Length]);
            Color Shade(int offset, double dark) => Mix(A(offset), Color.FromRgb(35, 37, 40), dark);
            void Brush(string key, Color color) => target[key] = new SolidColorBrush(color);

            // Pick the most colorful swatch for interactive states. Fixed offsets made the
            // Coffee B accent nearly charcoal, hiding its checked radio and wordmark.
            Color accent = A(0);
            double bestChroma = -1;
            for (int i = 0; i < palette.Length; i++)
            {
                Color candidate = A(i);
                double chroma = Math.Max(candidate.R, Math.Max(candidate.G, candidate.B))
                              - Math.Min(candidate.R, Math.Min(candidate.G, candidate.B));
                if (chroma > bestChroma) { accent = candidate; bestChroma = chroma; }
            }
            if (theme == Theme.Sepulchre)
                accent = Parse("#4faaa8");
            if (theme == Theme.Mourning)
                accent = Parse("#ff6f91");

            // One continuous outer-shell material: title, exposed side chrome and footer
            // all resolve the same horizontal gradient, while panes remain solid layers.
            Color chromeRight = Shade(1, .46);
            var chrome = new LinearGradientBrush(
                Shade(0, .62), chromeRight, new Point(0, .5), new Point(1, .5));
            target["BackgroundBrush"] = chrome;
            target["TitleBarBrush"] = chrome.Clone();
            // Each writing material carries a subdued version of its palette. They remain dark
            // and low-saturation enough for long-form text without collapsing into one shared
            // charcoal: cyan-black, coffee-gray, blue-violet and weathered blue-green.
            Color pane = theme switch
            {
                Theme.Malaise => Parse("#293c3f"),
                Theme.Sepulchre => Parse("#3a352f"),
                Theme.Delirium => Parse("#343344"),
                Theme.Mourning => Parse("#4b4453"),
                _ => Mix(Color.FromRgb(42, 44, 47), A(2), .13)
            };
            Color surface = theme switch
            {
                Theme.Malaise => Parse("#314548"),
                Theme.Sepulchre => Parse("#454039"),
                Theme.Delirium => Parse("#3e3c50"),
                Theme.Mourning => Parse("#4b4453"),
                _ => Mix(Color.FromRgb(48, 51, 54), A(1), .15)
            };
            if (theme == Theme.Mourning)
            {
                pane = Parse("#4b4453");
                surface = Parse("#4b4453");
            }
            Brush("SurfaceBrush", surface);
            Brush("PaneBrush", pane);
            Brush("BgCanvas", pane);
            Brush("TableHeaderBrush", Mix(pane, Colors.White, .07));
            Brush("BgFlyout", Color.FromRgb(37, 39, 42));
            Brush("BgRecentPanel", Color.FromRgb(46, 49, 52));
            Color cardBorder = Shade(3, .22);
            Color paneBorder = Shade(0, .20);
            Color appBorder = Shade(1, .28);
            if (theme is Theme.Malaise or Theme.Delirium or Theme.Mourning)
                (paneBorder, appBorder) = (appBorder, paneBorder);
            if (theme == Theme.Malaise)
                paneBorder = Mix(Parse("#0bc2c6"), Color.FromRgb(35, 37, 40), .18);
            if (theme == Theme.Mourning)
            {
                paneBorder = Parse("#756b79");
                appBorder = Parse("#a0ffe6");
            }
            if (theme == Theme.Sepulchre)
            {
                appBorder = Parse("#4faaa8");
                paneBorder = Mix(Parse("#986632"), Color.FromRgb(35, 37, 40), .18);
                cardBorder = paneBorder;
            }
            Brush("CardBorderBrush", cardBorder);
            Brush("PaneBorderBrush", paneBorder);
            Brush("AppBorderBrush", appBorder);
            Brush("InputBorderBrush", Shade(1, .25));
            Brush("InputHoverBrush", accent);

            // Coffee B's secondary controls deliberately echo the teal end of its chrome.
            Brush("SortButtonBrush", theme == Theme.Sepulchre ? Parse("#347c80") : Color.FromRgb(42, 44, 47));

            Brush("TextBrush", Color.FromRgb(244, 244, 240));
            Brush("MutedTextBrush", Color.FromRgb(215, 215, 207));
            Brush("ChromeTextBrush", Color.FromRgb(250, 250, 246));
            Brush("DimTextBrush", Color.FromRgb(180, 182, 177));
            Brush("TextFooter", Color.FromRgb(226, 228, 224));

            Brush("PrimaryBrush", accent);
            Brush("PrimaryHoverBrush", Mix(accent, Colors.White, .16));
            Brush("PrimaryPressedBrush", Mix(accent, Color.FromRgb(35, 37, 40), .22));
            Brush("OnPrimaryBrush", Contrast(accent));
            Brush("OutlineBtnBrush", accent);
            Brush("HeaderLineBrush", A(0));
            Brush("AccentLogo", Mix(accent, Colors.White, .18));
            Brush("SelectionAccent", A(5));
            Brush("RadioAccent", A(3));
            Brush("SelectionBg", accent);
            Brush("SelectionFg", Contrast(accent));
            Brush("DangerRed", Color.FromRgb(221, 70, 65));

            Brush("RowAltBrush", Color.FromArgb(24, 255, 255, 255));
            Brush("RowHoverBrush", Shade(1, .40));
            Brush("RowSelectedBrush", Mix(accent, Color.FromRgb(35, 37, 40), .25));
            Brush("OutlinePressedBrush", Mix(accent, Color.FromRgb(35, 37, 40), .28));
            Brush("MenuBackgroundBrush", Shade(0, .80));
            Brush("MenuBorderBrush", Shade(1, .25));
            Brush("MenuTextBrush", Color.FromRgb(244, 244, 240));
            Brush("MenuHoverBrush", Mix(accent, Color.FromRgb(35, 37, 40), .35));
            Brush("ScrollThumbBrush", Color.FromArgb(180, A(5).R, A(5).G, A(5).B));
            Brush("ScrollThumbHoverBrush", A(5));
            Brush("SliderTrack", Shade(1, .22));
            Brush("SettingsOpenRowBg", Mix(accent, Color.FromRgb(35, 37, 40), .35));
            Brush("WarnBrush", A(3));
            Brush("OkBrush", A(0));

            // Mourning uses the original peach/teal/charcoal/purple composition exactly.
            if (theme == Theme.Mourning)
            {
                var originalChrome = new SolidColorBrush(Parse("#3b3642"));
                target["BackgroundBrush"] = originalChrome;
                target["TitleBarBrush"] = originalChrome.Clone();
                Brush("SurfaceBrush", Parse("#34373e"));
                Brush("PaneBrush", Parse("#554c5d"));
                Brush("BgCanvas", Parse("#554c5d"));
                Brush("TableHeaderBrush", Parse("#5c5364"));
                Brush("BgFlyout", Parse("#343039"));
                Brush("BgRecentPanel", Parse("#3c3841"));
                Brush("CardBorderBrush", Parse("#756b79"));
                Brush("PaneBorderBrush", Parse("#756b79"));
                Brush("AppBorderBrush", Parse("#756b79"));
                Brush("InputBorderBrush", Parse("#ffb199"));
                Brush("InputHoverBrush", Parse("#f9f871"));
                Brush("PrimaryBrush", Parse("#ff6f91"));
                Brush("PrimaryHoverBrush", Parse("#ff86a2"));
                Brush("PrimaryPressedBrush", Parse("#e85e80"));
                Brush("OnPrimaryBrush", Parse("#292c31"));
                Brush("OutlineBtnBrush", Parse("#ffb199"));
                Brush("HeaderLineBrush", Parse("#a0ffe6"));
                Brush("AccentLogo", Parse("#ff6f91"));
                Brush("SelectionAccent", Parse("#a0ffe6"));
                Brush("RadioAccent", Parse("#f9f871"));
                Brush("SelectionBg", Parse("#ff6f91"));
                Brush("SelectionFg", Parse("#292c31"));
                Brush("TextFooter", Parse("#a0ffe6"));
                Brush("ScrollThumbBrush", Parse("#a0ffe6"));
                Brush("ScrollThumbHoverBrush", Parse("#a0ffe6"));
                Brush("SortButtonBrush", Parse("#554c5d"));
            }

        }

        private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
        private static Color Mix(Color a, Color b, double amountB) => Color.FromRgb(
            (byte)Math.Round(a.R * (1 - amountB) + b.R * amountB),
            (byte)Math.Round(a.G * (1 - amountB) + b.G * amountB),
            (byte)Math.Round(a.B * (1 - amountB) + b.B * amountB));
        private static Color Contrast(Color c) => c.R * .299 + c.G * .587 + c.B * .114 > 150
            ? Color.FromRgb(30, 31, 34) : Colors.White;
    }
}

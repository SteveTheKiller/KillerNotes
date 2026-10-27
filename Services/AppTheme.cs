using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KillerNotes.Services
{
    /// <summary>
    /// KillerNotes' side of the family theme engine. ThemeManager.cs is the KillerUI template and
    /// is never edited here; everything particular to KillerNotes comes in through its hooks:
    /// the saved settings, the Black/Purple first run, the values in Themes/App, and the tokens
    /// KillerNotes' own views derive (the editor pane, the notes list, the mentions strip, the
    /// database rows).
    /// </summary>
    internal static class AppTheme
    {
        /// <summary>Call once before ThemeManager.Initialize or InitializeInstaller.</summary>
        internal static void Configure()
        {
            ThemeManager.GetSetting = App.GetSetting;
            ThemeManager.SetSetting = App.SetSetting;
            ThemeManager.SetDefaults(Theme.Black, dark: Accent.Green, light: Accent.Green, black: Accent.Purple, se98: Accent.Blue);
            ThemeManager.AppTokens = AppTokens;
            ThemeManager.AppDerivedTokens = AppDerivedTokens;
            // Dialog captions (Controls/DialogChrome.cs): the file picker's and ConfirmDialog's
            // title icon and wordmark come from here.
            Controls.DialogChrome.AppName = "KillerNotes";
            Controls.DialogChrome.WordmarkSplit = 6;
            Controls.DialogChrome.IconUri = new Uri("pack://application:,,,/Resources/kn-icon.ico");
        }

        /// <summary>
        /// Rewrites a saved theme name KillerNotes no longer uses before ThemeManager reads it.
        /// Ectoplasm and Malaise once held each other's palettes, so that swap is undone once, and
        /// the four surviving palette-lab IDs move to their permanent names. Any other unknown
        /// name falls back to the first-run theme inside ThemeManager.
        /// </summary>
        internal static void MigrateSavedTheme()
        {
            string? saved = ThemeManager.GetSetting("Theme");
            if (ThemeManager.GetSetting("ThemeNameSwapFixed") is null)
            {
                if (saved == "Ectoplasm") saved = "Malaise";
                else if (saved == "Malaise") saved = "Ectoplasm";
                ThemeManager.SetSetting("ThemeNameSwapFixed", "1");
            }
            string? migrated = saved switch
            {
                "Retro06" => "Malaise",
                "Retro10" or "CoffeeSignboard" => "Sepulchre",
                "Retro16" or "MovingQuickly" => "Delirium",
                "Retro24" or "MultiEthnic" => "Mourning",
                "Retro" => "Ectoplasm",
                "Earthy" => "Decay",
                _ => saved,
            };
            if (migrated is not null && migrated != ThemeManager.GetSetting("Theme"))
                ThemeManager.SetSetting("Theme", migrated);
        }

        private static string FileName(Theme theme) => theme == Theme.SE98 ? "98SE" : theme.ToString();

        // Before the family contract: KillerNotes' per-theme values, then its accent values, then
        // its defaults for whatever a theme left unsaid.
        private static void AppTokens(ResourceDictionary d, Theme theme, Accent accent)
        {
            Merge(d, $"Themes/App/{FileName(theme)}.xaml", overwrite: true);
            if (ThemeManager.SupportsAccents(theme))
                Merge(d, $"Themes/App/Accents/{FileName(theme)}/{accent}.xaml", overwrite: true);
            Merge(d, "Themes/App/Defaults.xaml", overwrite: false);

            // KillerNotes' own layout tokens that follow a family brush. Set before the family
            // contract so its KillerShell defaults for the same names do not apply here.
            ThemeManager.Mirror(d, "BarEdgeBrush", "PaneBorderBrush");
            // The search field takes the darkest tone, SurfaceBrush, unless a theme states it.
            ThemeManager.Mirror(d, "SearchFieldBrush", "SurfaceBrush");
            // Every dialog's caption X is a floating block, so it rounds all four corners.
            ThemeManager.SetIfAbsent(d, "DialogCloseCornerRadius",
                d.Contains("SmallCornerRadius") ? d["SmallCornerRadius"] : new CornerRadius(0));
        }

        // After the family contract: tokens built from family ones.
        private static void AppDerivedTokens(ResourceDictionary d, Theme theme, Accent accent)
        {
            bool flat = d.Contains("UseDialogCaption") && d["UseDialogCaption"] is bool f && f;

            // The mentions strip's shadow, cast up and to the left from the note's bottom-right
            // corner, at the pane shadow's opacity. Frozen, and null on a flat theme, for the same
            // reason the family pane shadow is.
            if (d["PaneShadowOpacity"] is double cso && cso > 0)
            {
                var cornerShadow = new DropShadowEffect
                { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 3, Direction = 135, Opacity = cso };
                cornerShadow.Freeze();
                d["CornerShadowEffect"] = cornerShadow;
            }
            else d["CornerShadowEffect"] = null;

            // The mentions strip rounds only its top-left and bottom-right corners, and squares
            // off on a square theme.
            if (!d.Contains("MentionsCornerRadius"))
            {
                bool square = d["SmallCornerRadius"] is CornerRadius mcr && mcr.TopLeft <= 0;
                double r = square ? 0.0 : 5.0;
                d["MentionsCornerRadius"] = new CornerRadius(r, 0, r, 0);
            }

            // The editor pane's inner sunken ring sits PaneBevelInnerMargin inside its outer one.
            if (!d.Contains("ContentPaneInnerMargin"))
            {
                var cm = d["ContentPaneMargin"] is Thickness cpm ? cpm : new Thickness(0);
                var im = d["PaneBevelInnerMargin"] is Thickness pim ? pim : new Thickness(0);
                d["ContentPaneInnerMargin"] = new Thickness(
                    cm.Left + im.Left, cm.Top + im.Top, cm.Right + im.Right, cm.Bottom + im.Bottom);
            }

            // Overlays bleed back over the window frame's inset.
            if (!d.Contains("OverlayBleed"))
            {
                var fp = d["WindowFramePadding"] is Thickness wfp ? wfp : new Thickness(0);
                d["OverlayBleed"] = new Thickness(-fp.Left, -fp.Top, -fp.Right, -fp.Bottom);
            }

            // Caption pieces of KillerNotes' main window, graph window and tool windows.
            ThemeManager.SetIfAbsent(d, "CaptionAppGroupGap", new Thickness(0, 0, 6, 0));
            d["CaptionFontGlyphVisibility"] = flat ? Visibility.Collapsed : Visibility.Visible;
            d["CaptionDrawnGlyphVisibility"] = flat ? Visibility.Visible : Visibility.Collapsed;
            ThemeManager.Mirror(d, "CaptionPressedTextBrush", "SelectionFg");
            ThemeManager.Mirror(d, "TitleMetaBrush", "MutedTextBrush");
            double toolCaptionHeight = d["TitleBarHeight"] is double toolHeight ? toolHeight : 36.0;
            d["ToolWindowCloseMargin"] = flat ? new Thickness(0) :
                new Thickness(0, 0, Math.Max(0, (toolCaptionHeight - 28.0) / 2), 0);

            // Surfaces of KillerNotes' own views.
            ThemeManager.Mirror(d, "GraphCanvasBrush", "BackgroundBrush");
            ThemeManager.Mirror(d, "SidePaneBrush", "BgFlyout");
            ThemeManager.Mirror(d, "SketchMenuSeparatorBrush", "CardBorderBrush");

            // The focused edit field's edge, halfway between the resting and hovered edges.
            if (d["InputHoverBrush"] is SolidColorBrush hoverEdge && d["InputBorderBrush"] is SolidColorBrush restEdge)
                d["InputFocusBrush"] = flat ? d["InputHoverBrush"] : Frozen(Mix(restEdge.Color, hoverEdge.Color, 0.5));
            else
                d["InputFocusBrush"] = d.Contains("InputHoverBrush") ? d["InputHoverBrush"] : d["PrimaryBrush"];

            // The sidebar and header slots, which the Fonts dialog can override (Shell/Fonts.cs).
            // Killer Sans UI for the sidebar; Lacquer at 20 for group and Trash headings, raised
            // 5px to center its capitals, except 98SE, which keeps Killer Sans bold at 18.
            ThemeManager.SetIfAbsent(d, "SidebarFont", new FontFamily(new Uri("pack://application:,,,/"),
                "/KillerNotes;component/Fonts/#Killer Sans UI, Segoe UI"));
            ThemeManager.SetIfAbsent(d, "HeaderFont", new FontFamily(new Uri("pack://application:,,,/"),
                flat ? "/KillerNotes;component/Fonts/#Killer Sans" : "/KillerNotes;component/Fonts/#Lacquer"));
            ThemeManager.SetIfAbsent(d, "HeaderFontWeight", flat ? FontWeights.Bold : FontWeights.Normal);
            ThemeManager.SetIfAbsent(d, "HeaderFontSize", flat ? 18.0 : 20.0);
            ThemeManager.SetIfAbsent(d, "HeaderTextMargin", flat ? new Thickness(0) : new Thickness(0, -5, 0, 1));

            // The soft text outline the picker rows in Controls/Controls.xaml use.
            if (!d.Contains("TextStroke"))
                d["TextStroke"] = new DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0.8, Direction = 270, Opacity = 0.7,
                };

            // The Databases dialog's rows (Services/DatabaseRowPalette.cs).
            DatabaseRowPalette.Complete(d);
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromArgb(255,
            (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

        private static void Merge(ResourceDictionary d, string path, bool overwrite)
        {
            ResourceDictionary source;
            try { source = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{path}") }; }
            catch (IOException) { return; }   // no file for this theme or accent
            foreach (object key in source.Keys)
                if (overwrite || !d.Contains(key)) d[key] = source[key];
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}

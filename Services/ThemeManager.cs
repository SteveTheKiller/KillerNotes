using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

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
            // One-time: Ectoplasm and Malaise held each other's palettes while the picker
            // relabelled them. The palettes have been put under their correct names, so swap
            // the saved value once - otherwise everyone's chosen theme flips underneath them.
            if (GetSetting("ThemeNameSwapFixed") is null)
            {
                if (savedTheme == "Ectoplasm") savedTheme = "Malaise";
                else if (savedTheme == "Malaise") savedTheme = "Ectoplasm";
                SetSetting("ThemeNameSwapFixed", "1");
            }
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
            // NOTE: the actual assignment happens AFTER the accent overlay below - see the comment
            // there. Computing it here reads the BASE TitleBarBrush, and the overlay then replaces
            // TitleBarBrush without touching this copy, so every dialog kept the base theme's
            // caption colour while the main window followed the accent.
            // Caption height and the transparent halo a dialog leaves around itself for its drop
            // shadow. A flat theme wants a short caption and NO halo - the halo is where the resize
            // grab lives, so an invisible one puts the grab handle outside the visible window.
            SetIfAbsent(newDict, "DialogTitleBarHeight", 28.0);
            SetIfAbsent(newDict, "DialogHaloMargin", new Thickness(10));
            // The About card's caption band, gated on the same UseDialogCaption flag as the brush
            // above. 0 on every theme that does not ask for a caption, so their cards keep the exact
            // layout they had; a theme that wants one gets the same band height as its dialogs.
            newDict["AboutCaptionHeight"] = newDict.Contains("UseDialogCaption")
                ? newDict["DialogTitleBarHeight"]
                : 0.0;
            // (AboutCloseMargin is computed further down, once CaptionButtonHeight is known - it has
            // to be derived from the band height, not hand-picked.)
            // Caption buttons. Transparent face and no inset by default, so the title bar brush runs
            // edge to edge exactly as it always has and only the hover/press fill shows. A bevelled
            // theme gives them a real face and an inset - that is what stops the gradient AT the
            // button instead of letting it run underneath, which is how Win98 drew a caption.
            SetIfAbsent(newDict, "CaptionButtonBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "CaptionButtonMargin", new Thickness(0));
            // Title bar geometry. A bevelled theme wants the short Win98 caption with buttons that
            // fill it; everything else keeps the 36px bar and 44x36 hit targets it always had.
            SetIfAbsent(newDict, "TitleBarHeight", 36.0);
            SetIfAbsent(newDict, "CaptionButtonWidth", 44.0);
            SetIfAbsent(newDict, "CaptionButtonHeight", 36.0);
            SetIfAbsent(newDict, "TitleWordmarkSize", 17.0);
            SetIfAbsent(newDict, "TitleWordmarkBoldSize", 22.0);
            SetIfAbsent(newDict, "WordmarkEmbossOpacity", 0.0);
            // A Win98-style caption has no logotype in it - just the icon and the window's name in
            // plain bold. Swapping the wordmark out for that on a flat theme is both more authentic
            // and removes the whole problem of fitting a two-size logotype into an 18px bar.
            bool flatCaption = newDict.Contains("UseDialogCaption");
            newDict["WordmarkVisibility"] = flatCaption ? Visibility.Collapsed : Visibility.Visible;
            newDict["PlainTitleVisibility"] = flatCaption ? Visibility.Visible : Visibility.Collapsed;
            // The caption's own font. MS Sans Serif is the real Win98 UI face and still ships with
            // Windows; Tahoma is the fallback if it ever stops resolving.
            SetIfAbsent(newDict, "ChromeFontFamily", new FontFamily("Segoe UI"));
            // Extra gap before the close button. Win98 sat minimise and maximise flush together and
            // pushed close a couple of pixels clear of them.
            SetIfAbsent(newDict, "CaptionCloseGap", new Thickness(0));
            // Inset for the caption button strip. Win98 left a couple of pixels between the last
            // button and the window frame rather than butting it against the edge.
            SetIfAbsent(newDict, "CaptionButtonsMargin", new Thickness(0));
            // Gap between the app's caption buttons (databases, lock) and the window's three.
            SetIfAbsent(newDict, "CaptionAppGroupGap", new Thickness(0, 0, 6, 0));
            SetIfAbsent(newDict, "AboutCaptionMargin", new Thickness(0));
            // The close button's hover block. Only the TOP-RIGHT corner is rounded, and by the
            // CARD's radius - that corner is the card's corner, and the other three are interior
            // edges that must stay square. Rounding all four to SmallCornerRadius made the hover a
            // floating lozenge at the wrong radius. Derived, so it tracks the card automatically
            // and squares off with it on a square theme. (Steve, 2026-08-07.)
            if (!newDict.Contains("CaptionCloseCornerRadius"))
            {
                double tr = newDict["PanelCornerRadius"] is CornerRadius pcr ? pcr.TopRight : 0.0;
                newDict["CaptionCloseCornerRadius"] = new CornerRadius(0, tr, 0, 0);
            }
            // The DIALOG twin of that inset. Same right-hand gap, no top inset: a dialog band is
            // not covered by the window frame overlay the way the main window's is, so inheriting
            // the main key's top value only pushed the close button below centre.
            if (!newDict.Contains("DialogCaptionButtonsMargin"))
            {
                var cbm = newDict["CaptionButtonsMargin"] is Thickness ct ? ct : new Thickness(0);
                newDict["DialogCaptionButtonsMargin"] = new Thickness(0, 0, cbm.Right, 0);
            }
            // Hover fill for the caption buttons. A Win98 caption button had NO hover state at all -
            // it reacted only to being pressed - so a bevelled theme states Transparent and the
            // button simply sinks when clicked. Everything else keeps the row-hover wash.
            if (!newDict.Contains("CaptionHoverBrush"))
                newDict["CaptionHoverBrush"] = flatCaption
                    ? new SolidColorBrush(Colors.Transparent) : newDict["RowHoverBrush"];
            // Caption glyphs are drawn bold on a bevelled theme - a 1px MDL2 stroke disappears
            // against a grey button face at 16px.
            newDict["CaptionGlyphWeight"] = flatCaption ? FontWeights.Bold : FontWeights.Normal;
            // The About card's close button. It must match the caption buttons when the card has a
            // caption band, and keep the smaller bare-glyph box it always had when it does not.
            newDict["AboutCloseWidth"] = flatCaption ? newDict["CaptionButtonWidth"] : 28.0;
            newDict["AboutCloseHeight"] = flatCaption ? newDict["CaptionButtonHeight"] : 26.0;
            // +1 for the card's own 1px border. A dialog's caption band meets the window edge, so
            // all of DialogTitleBarHeight is visible; the About band sits INSIDE the card border
            // (MainWindow.xaml:1641, BorderThickness=1), so at the same value it presents one row
            // of pixels fewer. That made its slack odd - 3px around a 16px button - and no integer
            // margin could centre it. Giving it the extra row restores an even 4, so the close sits
            // 2 above and 2 below like every other caption. The centring math below deliberately
            // uses the DIALOG height, not this one: the extra pixel is spent on the border, not on
            // the band, so it must not enter the arithmetic.
            double dlgH = newDict["DialogTitleBarHeight"] is double dh ? dh : 0.0;
            if (flatCaption) newDict["AboutCaptionHeight"] = dlgH + 2.0;
            // The button is VerticalAlignment=Top in the band, so the top margin is the only thing
            // positioning it. Derive that margin from the heights instead of hand-picking a number:
            // every time the band height changed (28 -> 20) a hardcoded value silently went wrong.
            double btnH = newDict["AboutCloseHeight"] is double bh ? bh : 0.0;
            // Centre in the BAND's own height, not the dialog caption height. The button lives
            // inside the band, so the band is what it has to centre in - measuring against dlgH
            // was two pixels of headroom against three of footroom. +2 above rather than +1 is
            // also deliberate: the slack has to stay EVEN or no integer margin can centre a 16px
            // button, which is the same odd-number trap the card hit the first time.
            double capH = newDict["AboutCaptionHeight"] is double ch ? ch : 0.0;
            newDict["AboutCloseMargin"] = flatCaption
                ? new Thickness(0, System.Math.Max(0, (capH - btnH) / 2.0), 4, 0)
                : new Thickness(0, 6, 6, 0);
            // The DIALOG twin. The About card's band is deliberately one row taller than a
            // dialog's - that extra row is the card border - so About's margin is one pixel too
            // low anywhere else. The picker and the Databases window were both using
            // AboutCloseMargin, which is exactly why their X sat off-centre while the About card's
            // looked right. Same arithmetic, dlgH instead of capH. (Steve, 2026-08-07.)
            newDict["DialogCloseMargin"] = flatCaption
                ? new Thickness(0, System.Math.Max(0, (dlgH - btnH) / 2.0), 4, 0)
                : new Thickness(0, 6, 6, 0);
            SetIfAbsent(newDict, "TitleIconSize", 25.0);
            SetIfAbsent(newDict, "TitleIconMargin", new Thickness(0, 0, 7, 0));
            SetIfAbsent(newDict, "TitleBarPadding", new Thickness(14, 0, 0, 0));
            // The editor pane's inset. The default keeps the family's 8px right gutter and a FLUSH
            // top, and that is correct on the themes that blend: their title bar, the strip behind
            // the content row and the footer are all the same chrome tier, so the pane meeting the
            // caption reads as one continuous surface and a gap there would look like a seam.
            // A theme whose caption is a DISTINCT coloured band does not blend - the pane butts
            // straight into a hard edge and reads as jammed under it - so it opens the gap instead.
            SetIfAbsent(newDict, "ContentPaneMargin", new Thickness(0, 0, 8, 0));
            // The content pane's INNER sunken ring sits one pixel inside its outer one. Derived so
            // the two can never drift: whatever the pane's margin is, the inner ring is that plus
            // PaneBevelInnerMargin. Both are 0 by default, so the ring lands exactly on the outer
            // one and draws nothing (its brushes are transparent too).
            if (!newDict.Contains("ContentPaneInnerMargin"))
            {
                var cm = newDict["ContentPaneMargin"] is Thickness cpm ? cpm : new Thickness(0);
                var im = newDict["PaneBevelInnerMargin"] is Thickness pim ? pim : new Thickness(0);
                newDict["ContentPaneInnerMargin"] = new Thickness(
                    cm.Left + im.Left, cm.Top + im.Top, cm.Right + im.Right, cm.Bottom + im.Bottom);
            }
            // The search field's fill. Defaults to SurfaceBrush, which is what it has always been.
            // The family rule is that inputs take the darkest tone, and that is right on the twelve
            // blended themes - but on a theme whose "darkest tone" IS the button face, it makes an
            // edit field the same grey as the panel around it. Win98 edit fields are white.
            if (!newDict.Contains("SearchFieldBrush") && newDict.Contains("SurfaceBrush"))
                newDict["SearchFieldBrush"] = newDict["SurfaceBrush"];
            // The notes LIST's fill - not the sidebar. Transparent by default, so the chrome strip
            // behind the content row shows through and the list blends in exactly as before. A
            // theme where a list is a client area - a Win98 list box is white - states its own.
            // Scope matters here: this must not reach the header above the list or the calculator
            // below it. Those are controls sitting on the window face, and painting them the client
            // colour makes the whole sidebar look like one big edit field.
            SetIfAbsent(newDict, "ListPaneBrush", new SolidColorBrush(Colors.Transparent));
            // The notes list's top and bottom scroll fades. 1 keeps them; a retro theme sets 0.
            // Opacity rather than Visibility because Sidebar.cs drives Visibility from scroll
            // position - overriding that would fight the code every time the list moves.
            SetIfAbsent(newDict, "EdgeFadeOpacity", 1.0);
            // The window frame's OUTER 3D edge. Win98 draws a window border as TWO stacked bevels,
            // not one: a raised-outer (white top/left over black bottom/right) wrapping a
            // raised-inner (#dfdfdf over #808080). That doubling is why a real Win98 edge looks
            // bevelled on both sides of the line. The existing Bevel* pair is the inner one and is
            // shared with every control, so the outer pair gets its own keys rather than making
            // buttons thicker. All four default to nothing, so no other theme grows an edge.
            SetIfAbsent(newDict, "FrameOuterLightBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "FrameOuterDarkBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "FrameOuterLightThickness", new Thickness(0));
            SetIfAbsent(newDict, "FrameOuterDarkThickness", new Thickness(0));
            // The window frame's INNER 3D edge, and the frame face itself. These three used to be
            // the shared Bevel* pair and AppBorderBrush read straight off the window border, which
            // tied the window's edge to every button's edge and made the frame a flat slab of one
            // grey. A real Win98 frame is 1px inner bevel over a SHADED face, thinner than the 2px
            // a control gets. Each falls back to what the window used before, so the twelve other
            // themes are pixel-identical. (Steve, 2026-08-07.)
            if (!newDict.Contains("FrameInnerLightBrush") && newDict.Contains("BevelLightBrush"))
                newDict["FrameInnerLightBrush"] = newDict["BevelLightBrush"];
            if (!newDict.Contains("FrameInnerDarkBrush") && newDict.Contains("BevelDarkBrush"))
                newDict["FrameInnerDarkBrush"] = newDict["BevelDarkBrush"];
            if (!newDict.Contains("FrameInnerLightThickness") && newDict.Contains("BevelLightThickness"))
                newDict["FrameInnerLightThickness"] = newDict["BevelLightThickness"];
            if (!newDict.Contains("FrameInnerDarkThickness") && newDict.Contains("BevelDarkThickness"))
                newDict["FrameInnerDarkThickness"] = newDict["BevelDarkThickness"];
            if (!newDict.Contains("WindowFrameBrush") && newDict.Contains("AppBorderBrush"))
                newDict["WindowFrameBrush"] = newDict["AppBorderBrush"];
            // How far each layer of the window frame is inset from the one outside it. Zero by
            // default, which is what every flat theme wants (it draws nothing here anyway). A
            // bevelled theme insets the face past the outer bevel and the inner bevel past the
            // face, so the three read as one 5px edge instead of three lines fighting for pixel 0.
            SetIfAbsent(newDict, "WindowFrameMargin", new Thickness(0));
            SetIfAbsent(newDict, "FrameInnerMargin", new Thickness(0));
            // How far the CONTENT is held off the window edge to make room for that frame. Zero by
            // default - a theme with no frame gives its content the whole window, exactly as
            // before. A theme with one insets by the frame's full width, or the frame paints over
            // the caption buttons and the footer instead of sitting beside them.
            SetIfAbsent(newDict, "WindowFramePadding", new Thickness(0));
            // Optical nudge for the plain caption text. Zero by default; a theme whose caption font
            // renders low in its line box lifts it here.
            SetIfAbsent(newDict, "TitleTextMargin", new Thickness(0));
            // Negative margin that lets the caption band bleed past the content inset. 0 by
            // default: on a theme with no window frame the band already spans the full width.
            SetIfAbsent(newDict, "TitleBarBleed", new Thickness(0));
            // The full-window overlays (About, shortcuts, fonts) live inside the content grid, so
            // once that grid is inset for the frame they stop short of the window edge and the
            // frame stays undimmed while everything else behind the overlay dims - a bright 5px
            // ring around a dimmed window. This is the negative of the frame padding, so they
            // bleed back out over it. Zero when there is no frame. (Steve, 2026-08-07.)
            if (!newDict.Contains("OverlayBleed"))
            {
                var fp = newDict["WindowFramePadding"] is Thickness wfp ? wfp : new Thickness(0);
                newDict["OverlayBleed"] = new Thickness(-fp.Left, -fp.Top, -fp.Right, -fp.Bottom);
            }
            // Toolbar separator. One flat 1px rule by default. A bevelled theme makes it 2px and
            // fills it with a hard-stop gradient - grey then white - which is the ETCHED divider
            // every Win98 toolbar and taskbar draws between bands. Done as one Rectangle with a
            // two-stop brush rather than a second element, because each separator is named and
            // code toggles its visibility; a sibling would need every one of those call sites too.
            // Menu edge. The inner ring defaults to the shared control bevel, which is exactly what
            // menus drew before; the outer ring is new and draws nothing unless a theme asks for
            // it. Two rings is what makes a menu read as a raised card rather than a bordered box.
            if (!newDict.Contains("MenuBevelLightBrush") && newDict.Contains("BevelLightBrush"))
                newDict["MenuBevelLightBrush"] = newDict["BevelLightBrush"];
            if (!newDict.Contains("MenuBevelDarkBrush") && newDict.Contains("BevelDarkBrush"))
                newDict["MenuBevelDarkBrush"] = newDict["BevelDarkBrush"];
            if (!newDict.Contains("MenuBevelLightThickness") && newDict.Contains("BevelLightThickness"))
                newDict["MenuBevelLightThickness"] = newDict["BevelLightThickness"];
            if (!newDict.Contains("MenuBevelDarkThickness") && newDict.Contains("BevelDarkThickness"))
                newDict["MenuBevelDarkThickness"] = newDict["BevelDarkThickness"];
            SetIfAbsent(newDict, "MenuBevelInnerMargin", new Thickness(0));
            SetIfAbsent(newDict, "MenuBevel2LightBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "MenuBevel2DarkBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "MenuBevel2LightThickness", new Thickness(0));
            SetIfAbsent(newDict, "MenuBevel2DarkThickness", new Thickness(0));
            // The file dialog's own row insets. 16 is the family's dialog gutter and assumes a 1px
            // window edge; a theme with a real sizing frame sets these smaller so the frame and the
            // gutter do not stack. Same reasoning as SidebarPanelMargin in the main window.
            // The picker's Win32 status bar. Off by default: the family's floating card puts the
            // selection beside the buttons, and a bare strip along the bottom of a rounded card
            // reads as bolted on. A theme that IS a Win32 dialog turns it on and hides the inline
            // pair instead. (Steve, 2026-08-07: "98SE only".)
            SetIfAbsent(newDict, "DialogStatusBarHeight", 0.0);
            SetIfAbsent(newDict, "DialogStatusBarVisibility", Visibility.Collapsed);
            SetIfAbsent(newDict, "DialogInlineSelVisibility", Visibility.Visible);
            SetIfAbsent(newDict, "DialogContentMargin", new Thickness(16, 8, 16, 8));
            SetIfAbsent(newDict, "DialogRowMargin", new Thickness(16, 0, 16, 0));
            SetIfAbsent(newDict, "DialogFieldMargin", new Thickness(16, 12, 16, 0));
            SetIfAbsent(newDict, "DialogButtonsMargin", new Thickness(16, 12, 16, 12));
            SetIfAbsent(newDict, "BarSepWidth", 1.0);
            if (!newDict.Contains("BarSepBrush") && newDict.Contains("PaneBorderBrush"))
                newDict["BarSepBrush"] = newDict["PaneBorderBrush"];
            // The sidebar's inset from the window edge. 8,8,0,0 is the family default and assumes a
            // 1px window edge; a theme with a real sizing frame sets it smaller so the two do not
            // stack into a band several times the frame's width.
            SetIfAbsent(newDict, "SidebarPanelMargin", new Thickness(8, 8, 0, 0));
            // The bottom-right resize grip. CLAUDE.md locks the family grip as six 2x2 dots across
            // every app, so that stays the default and the twelve other themes never see the hatch.
            // A Win98-style theme swaps in the era's diagonal bevelled bands instead - Steve,
            // 2026-08-07: "the corner will look different from the rest because this theme is not
            // like the rest". Gated on the same UseDialogCaption marker as the rest of the retro
            // treatment, so a theme opts into the whole look at once rather than piecemeal.
            newDict["GripDotsVisibility"] = flatCaption ? Visibility.Collapsed : Visibility.Visible;
            newDict["GripHatchVisibility"] = flatCaption ? Visibility.Visible : Visibility.Collapsed;
            // Scrollbars. The family bar is a thin overlay with a rounded thumb floating in an
            // invisible track; a Win98 bar is a 16px control with a dithered sunken channel and a
            // square raised thumb that fills it. Every default below is the existing look.
            SetIfAbsent(newDict, "ScrollBarThickness", 12.0);
            SetIfAbsent(newDict, "ScrollThumbRadius", new CornerRadius(3));
            SetIfAbsent(newDict, "ScrollThumbMargin", new Thickness(4, 0, 4, 0));
            SetIfAbsent(newDict, "ScrollTrackBrush", new SolidColorBrush(Colors.Transparent));
            // The track's SUNKEN bevel is crossed - dark on the light thickness (top/left) and
            // light on the dark one - so the channel reads as pressed into the panel. Transparent
            // by default, so no other theme sprouts a groove.
            SetIfAbsent(newDict, "ScrollTrackBevelDark", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "ScrollTrackBevelLight", new SolidColorBrush(Colors.Transparent));
            // The line (arrow) buttons at each end. 0 means they occupy no space at all, which is
            // how the twelve modern themes keep their bare overlay bar - the buttons are always in
            // the template, just measured to nothing, so there is no second template to maintain.
            SetIfAbsent(newDict, "ScrollArrowSize", 0.0);
            // The pane's SUNKEN edge. Win98 client areas - the Notepad text area, an Explorer list -
            // are recessed into the window face, and the pane reading as flat is the last thing that
            // gives it away. Crossed on purpose: the dark brush takes the LIGHT thickness (top/left)
            // and vice versa, which is the inversion that makes a control look pressed in rather
            // than standing out. Transparent by default, so no other theme gains an edge.
            SetIfAbsent(newDict, "PaneBevelDarkBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "PaneBevelLightBrush", new SolidColorBrush(Colors.Transparent));
            // A real sunken client edge is TWO tones, not one - Win32's EDGE_SUNKEN draws #808080
            // then #000000 down the top/left, and #ffffff then the face colour up the bottom/right.
            // A single 2px grey has the right width and the wrong depth: it reads as a drawn line
            // rather than as an edge, which is exactly how it differed from Notepad's list box.
            // (Steve, 2026-08-07.)
            //
            // The pane rings also get their OWN thickness keys rather than borrowing the shared
            // control Bevel* pair: a 2-tone edge needs 1px per ring, and a button still wants 2.
            if (!newDict.Contains("PaneBevelLightThickness") && newDict.Contains("BevelLightThickness"))
                newDict["PaneBevelLightThickness"] = newDict["BevelLightThickness"];
            if (!newDict.Contains("PaneBevelDarkThickness") && newDict.Contains("BevelDarkThickness"))
                newDict["PaneBevelDarkThickness"] = newDict["BevelDarkThickness"];
            SetIfAbsent(newDict, "PaneBevelDark2Brush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "PaneBevelLight2Brush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "PaneBevel2LightThickness", new Thickness(0));
            SetIfAbsent(newDict, "PaneBevel2DarkThickness", new Thickness(0));
            SetIfAbsent(newDict, "PaneBevelInnerMargin", new Thickness(0));
            // The flat 1px edge on a pane that ALSO carries a sunken bevel. On a bevelled theme the
            // bevel is the edge, so this goes transparent there and the two stop stacking into a
            // three-pixel border - which is what made the About card look heavier, not better,
            // the moment its info panel got a proper bevel.
            if (!newDict.Contains("PaneEdgeBrush") && newDict.Contains("PaneBorderBrush"))
                newDict["PaneEdgeBrush"] = newDict["PaneBorderBrush"];
            // The footer's SLIGHT recess and its cell divider. A Win98 status bar is sunk a hair
            // into the window face - much shallower than a client area, hence its own thickness key
            // rather than reusing the pane bevel - and is split into cells by a two-tone 1px rule.
            // All default to nothing, so the other twelve keep a flat, undivided footer.
            SetIfAbsent(newDict, "FooterBevelDarkBrush", new SolidColorBrush(Colors.Transparent));
            SetIfAbsent(newDict, "FooterBevelLightBrush", new SolidColorBrush(Colors.Transparent));
            // The CELLS. A Win98 status bar is a row of sunken boxes, and the "divider" between them
            // is just the gap - drawing a standalone rule there is what looked wrong. 1px, not the
            // 2px control bevel: a status cell is the shallowest recess in the whole UI.
            SetIfAbsent(newDict, "FooterCellLightThickness", new Thickness(0));
            SetIfAbsent(newDict, "FooterCellDarkThickness", new Thickness(0));
            SetIfAbsent(newDict, "FooterCellMargin", new Thickness(0));
            SetIfAbsent(newDict, "FooterCellPadding", new Thickness(0));
            SetIfAbsent(newDict, "FooterPadding", new Thickness(16, 0, 16, 0));
            // Edit-field fill for code-built text boxes (the colour picker's RGB and hex inputs).
            // Same reasoning as SearchFieldBrush: the family rule sends inputs to the darkest tone,
            // which on a theme whose darkest tone IS the button face makes a field vanish into the
            // dialog. Defaults to BackgroundBrush, exactly what those boxes had.
            if (!newDict.Contains("TextFieldBrush") && newDict.Contains("BackgroundBrush"))
                newDict["TextFieldBrush"] = newDict["BackgroundBrush"];
            // The white sunken well behind a radio button's ring. Transparent by default.
            SetIfAbsent(newDict, "RadioWellBrush", new SolidColorBrush(Colors.Transparent));
            // The file picker's left column (folder tree + pinned places). Transparent by default,
            // which is the family's deliberate FLAT sidebar sitting on the chrome background. A
            // theme where a tree is a client area - Win98's Explorer is white and recessed - fills
            // it instead. Only the fill is keyed; the recess comes from the shared PaneBevel pair.
            SetIfAbsent(newDict, "SidebarPaneBrush", new SolidColorBrush(Colors.Transparent));
            // The colour picker's Replace/Reset chips. They are buttons, so on a theme where
            // PaneBrush is the white client colour they must NOT take it - they came out looking
            // like empty text fields. All three default to what the chips already used.
            if (!newDict.Contains("ChipFaceBrush") && newDict.Contains("PaneBrush"))
                newDict["ChipFaceBrush"] = newDict["PaneBrush"];
            if (!newDict.Contains("ChipEdgeBrush") && newDict.Contains("InputBorderBrush"))
                newDict["ChipEdgeBrush"] = newDict["InputBorderBrush"];
            if (!newDict.Contains("ChipHoverBrush") && newDict.Contains("InputBorderBrush"))
                newDict["ChipHoverBrush"] = newDict["InputBorderBrush"];
            // OutlineButton's resting fill and text. Transparent + accent is the family standard
            // and stays the default, so the twelve flat themes are untouched. A theme whose
            // buttons all share one face states these instead: a Win98 dialog has two IDENTICAL
            // grey buttons and marks the default one with its border, never with a different
            // colour, so an accent-outlined OK beside a grey Cancel is wrong there.
            // SurfaceButton's hover fill. Separate from RowHoverBrush because a list row and a
            // button do not hover to the same colour on a theme where the button face IS the row
            // hover colour - 98SE has both at #d4d0c8, so Cancel had no hover at all.
            if (!newDict.Contains("SurfaceHoverBrush") && newDict.Contains("RowHoverBrush"))
                newDict["SurfaceHoverBrush"] = newDict["RowHoverBrush"];
            // The flat 1px edge on a field that ALSO carries a sunken bevel. On a bevelled theme
            // the bevel is the edge, and drawing InputBorderBrush as well put a second line right
            // beside it - visible as a doubled rule along the top of the picker's address bar.
            if (!newDict.Contains("InputEdgeBrush") && newDict.Contains("InputBorderBrush"))
                newDict["InputEdgeBrush"] = newDict["InputBorderBrush"];
            // Text selection. It was PrimaryBrush at 0.35 everywhere, which ties the selection to
            // the ACCENT - so a theme with a fixed system selection colour could not have one, and
            // 98SE selected text in the app's purple instead of the era's navy. Both default to
            // what they were, so the twelve accent-led themes are unchanged. (Steve, 2026-08-07.)
            if (!newDict.Contains("TextSelectionBrush") && newDict.Contains("PrimaryBrush"))
                newDict["TextSelectionBrush"] = newDict["PrimaryBrush"];
            SetIfAbsent(newDict, "TextSelectionOpacity", 0.35);
            // The selected text's own colour. At the family's 0.35 wash the glyphs still read
            // through, so this defaults to TextBrush and nothing changes. A theme that selects
            // with a SOLID block - Win98 fills the run with navy - has to flip the text to white
            // or the selection is unreadable, which is what a translucent navy was working around.
            if (!newDict.Contains("TextSelectionTextBrush") && newDict.Contains("TextBrush"))
                newDict["TextSelectionTextBrush"] = newDict["TextBrush"];
            SetIfAbsent(newDict, "OutlineFaceBrush", new SolidColorBrush(Colors.Transparent));
            if (!newDict.Contains("OutlineTextBrush") && newDict.Contains("OutlineBtnBrush"))
                newDict["OutlineTextBrush"] = newDict["OutlineBtnBrush"];
            if (!newDict.Contains("OutlineHoverBrush") && newDict.Contains("OutlineBtnBrush"))
                newDict["OutlineHoverBrush"] = newDict["OutlineBtnBrush"];
            if (!newDict.Contains("OutlineHoverTextBrush") && newDict.Contains("OnPrimaryBrush"))
                newDict["OutlineHoverTextBrush"] = newDict["OnPrimaryBrush"];
            // Caption glyphs: font character or drawn shape. Segoe MDL2 has no Win98 equivalents -
            // the era's minimize/maximize/close were hand-drawn bitmaps - so a bevelled theme swaps
            // to shapes rather than trying to find a character that looks close. Every other theme
            // keeps the MDL2 glyph it has always had.
            newDict["CaptionFontGlyphVisibility"] = flatCaption ? Visibility.Collapsed : Visibility.Visible;
            newDict["CaptionDrawnGlyphVisibility"] = flatCaption ? Visibility.Visible : Visibility.Collapsed;
            // The format bar. Defaults reproduce the current floating card exactly - a 1px
            // PaneBorderBrush edge open at the top, 2px padding. A Win98 theme turns it into a
            // taskbar: a raised strip with a white top/left highlight and a dark bottom/right
            // shadow, and a little more padding so the tool buttons sit ON the strip.
            if (!newDict.Contains("BarEdgeBrush") && newDict.Contains("PaneBorderBrush"))
                newDict["BarEdgeBrush"] = newDict["PaneBorderBrush"];
            SetIfAbsent(newDict, "BarEdgeThickness", new Thickness(1, 0, 1, 1));
            SetIfAbsent(newDict, "BarPadding", new Thickness(2));
            // The About panel's flat 1px edge. A bevelled theme drops it to 0 so the crossed sunken
            // bevel IS the edge: in 98SE PaneBorderBrush and BevelDarkBrush are both #808080, so the
            // flat line sat exactly on top of the dark half of the bevel and hid it completely.
            newDict["AboutPanelBorderThickness"] = newDict.Contains("UseDialogCaption")
                ? new Thickness(0)
                : new Thickness(1);
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
            // Dialog caption band. Resolved HERE, after the accent overlay, so it picks up the
            // accent's gradient rather than the base theme's - the 98SE overlays each restate
            // TitleBarBrush, and reading it before the merge left the SketchPad, Dictation and
            // Databases captions green while the main window went red.
            newDict["DialogTitleBarBrush"] = newDict.Contains("UseDialogCaption") && newDict.Contains("TitleBarBrush")
                ? newDict["TitleBarBrush"]
                : new SolidColorBrush(Colors.Transparent);
            if (!newDict.Contains("BgFlyout") && newDict.Contains("MenuBackgroundBrush"))
                newDict["BgFlyout"] = newDict["MenuBackgroundBrush"];
            // The About card's info panel. Defaults to the context-menu fill so the panel reads as
            // the same material as a menu; a theme states its own when that is wrong for it - 98SE
            // wants a white, sunken client area rather than its button-face grey.
            if (!newDict.Contains("AboutPanelBrush") && newDict.Contains("MenuBackgroundBrush"))
                newDict["AboutPanelBrush"] = newDict["MenuBackgroundBrush"];
            // Close caption button. Read AFTER the accent overlay so it tracks the live accent.
            // The caption close is RED AT REST and fills a red BLOCK with a white glyph on hover.
            //
            // Only six palettes state DangerRed (Decay, Delirium, Ectoplasm, Malaise, Mourning,
            // Sepulchre); the base #e04444 is declared in Controls.xaml, which is NOT part of the
            // theme dictionary, so newDict.Contains("DangerRed") is false for the other seven.
            // Falling back to ChromeTextBrush there made the glyph near-WHITE at rest and painted a
            // big white block on hover. The fallback has to be the same literal Controls.xaml uses.
            var dangerRed = new SolidColorBrush(Color.FromRgb(0xE0, 0x44, 0x44));
            if (!newDict.Contains("CaptionCloseBrush"))
                newDict["CaptionCloseBrush"] = newDict.Contains("DangerRed")
                    ? newDict["DangerRed"] : dangerRed;
            if (!newDict.Contains("CaptionCloseHoverBrush"))
                newDict["CaptionCloseHoverBrush"] = newDict.Contains("DangerRed")
                    ? newDict["DangerRed"] : dangerRed;
            SetIfAbsent(newDict, "CaptionCloseHoverFgBrush", new SolidColorBrush(Colors.White));
            // Caption glyphs (Databases, Lock, Minimize, Maximize). ChromeTextBrush is written for
            // text ON the title bar brush - white on 98SE's green. Once those buttons have their own
            // grey face, white is unreadable, so a bevelled theme states a dark glyph instead.
            if (!newDict.Contains("CaptionGlyphBrush"))
                newDict["CaptionGlyphBrush"] = newDict["ChromeTextBrush"];

            // (TreeLineBrush was the folder picker's connecting elbows. The tree came out of the
            // picker on 2026-08-07 - the left column is bookmarks only now - so the key, the six
            // per-theme hexes that restated it, and FolderTree.cs itself are all gone. KillerPDF,
            // KillerShell and Killendar still have trees and still need it; the KIT keeps it.)

            // The picker's text shadow. An EFFECT, not a brush - a SolidColorBrush here silently
            // does nothing, because the Setter it feeds is TextBlock.Effect. Identical in every app
            // and every theme that declares it, so it is defined once here instead of thirteen
            // times; a theme that wants none of it states its own with Opacity 0.
            if (!newDict.Contains("TextStroke"))
                newDict["TextStroke"] = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 3,
                    ShadowDepth = 0.8,
                    Direction = 270,
                    Opacity = 0.7,
                };

            Publish(newDict);
        }
    }
}

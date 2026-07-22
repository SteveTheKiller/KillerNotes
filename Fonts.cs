using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerNotes
{
    // A row in the two font combos. Public (not nested) so WPF's ItemTemplate
    // bindings can reflect on Display / Fam.
    public sealed class FontChoice
    {
        public string Display { get; set; } = "";
        public string Value { get; set; } = "";      // "" | "sys:<family>" | "file:<family>"
        public FontFamily? Fam { get; set; }         // null = inherit (the Default row)
        public override string ToString() => Display;
    }

    // Custom fonts (1.1.2, DantexDT's "previous font was more readable" thread): a Fonts
    // overlay with three slots - Headers (group titles, the Killculator title), Sidebar
    // (note titles in the list), and Note text (the editor default). Each slot picks the
    // KillerNotes default, any installed family, or a .ttf/.otf dropped on the card
    // (copied to %APPDATA%\KillerNotes\Fonts so it survives moves and reinstalls). The
    // title-bar wordmark is the logo and never changes.
    //
    // A readability guard keeps Wingdings and friends out: a font must map the plain
    // Latin alphabet and digits to real glyphs before either slot will take it.
    //
    // Settings (app-wide, not per-database): FontHeader / FontContent as FontChoice.Value.
    // Applying a font overrides the HeaderFont / ContentFont DynamicResources from
    // Controls.xaml at the Application level, so every usage repaints live; removing the
    // override falls back to the Controls.xaml defaults.
    public partial class MainWindow
    {
        private const string SetHeader = "FontHeader";
        private const string SetSidebar = "FontSidebar";
        private const string SetContent = "FontContent";
        private bool _fontsBuilt;
        private bool _fontsSyncing;

        private static string FontsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KillerNotes", "Fonts");

        // ---- Startup (MainWindow ctor) ----

        private void InitFonts()
        {
            ApplyFontSlot(SetHeader, "HeaderFont");
            ApplyFontSlot(SetSidebar, "SidebarFont");
            ApplyFontSlot(SetContent, "ContentFont");
        }

        /// <summary>Reads one slot's setting and overrides (or restores) its resource.</summary>
        private static void ApplyFontSlot(string setting, string resKey)
        {
            var fam = ResolveFamily(App.GetSetting(setting) ?? "");
            if (fam is null) Application.Current.Resources.Remove(resKey);   // Controls.xaml default
            else Application.Current.Resources[resKey] = fam;
        }

        private static FontFamily? ResolveFamily(string v)
        {
            try
            {
                if (v.StartsWith("sys:")) return new FontFamily(v[4..]);
                if (v.StartsWith("file:") && Directory.Exists(FontsDir))
                    return new FontFamily(new Uri(FontsDir + Path.DirectorySeparatorChar), "./#" + v[5..]);
            }
            catch { /* stale setting (deleted file, uninstalled font): fall back to default */ }
            return null;
        }

        /// <summary>Notes saved as XamlPackage bake the editor's font AT SAVE TIME onto
        /// the document - TextRange.Save resolves effective formatting onto EVERY run,
        /// the same reason NormalizeThemeColors (Editor.cs) exists for colors - so a
        /// loaded note ignored a later ContentFont change and the Fonts dialog looked
        /// dead on existing notes. On every load: read the save-time body font off the
        /// root (pre-1.1.2 notes carry it only on runs; they were all saved under the
        /// editor's old Segoe UI default), then clear exactly that family from the whole
        /// tree. Anything DIFFERENT is a real choice - Consolas mono spans, pasted
        /// fonts - and keeps its font. Called from OpenNote (Notes.cs).</summary>
        internal static void NormalizeContentFont(FlowDocument doc)
        {
            string baked = "Segoe UI";
            if (doc.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily df) baked = df.Source;
            else if (doc.Blocks.FirstBlock is Section rs
                     && rs.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily sf) baked = sf.Source;
            doc.ClearValue(TextElement.FontFamilyProperty);
            foreach (var b in doc.Blocks.ToList()) UnbakeBlockFont(b, baked);
        }

        private static void UnbakeBlockFont(Block block, string baked)
        {
            UnbakeElementFont(block, baked);
            switch (block)
            {
                case Paragraph p:
                    foreach (var i in p.Inlines.ToList()) UnbakeInlineFont(i, baked);
                    break;
                case List list:
                    foreach (var li in list.ListItems.ToList())
                    {
                        UnbakeElementFont(li, baked);
                        foreach (var b in li.Blocks.ToList()) UnbakeBlockFont(b, baked);
                    }
                    break;
                case Table t:
                    foreach (var g in t.RowGroups.ToList())
                        foreach (var row in g.Rows.ToList())
                        {
                            UnbakeElementFont(row, baked);
                            foreach (var cell in row.Cells.ToList())
                            {
                                UnbakeElementFont(cell, baked);
                                foreach (var b in cell.Blocks.ToList()) UnbakeBlockFont(b, baked);
                            }
                        }
                    break;
                case Section s:
                    foreach (var b in s.Blocks.ToList()) UnbakeBlockFont(b, baked);
                    break;
            }
        }

        private static void UnbakeInlineFont(Inline inline, string baked)
        {
            UnbakeElementFont(inline, baked);
            if (inline is Span sp)
                foreach (var i in sp.Inlines.ToList()) UnbakeInlineFont(i, baked);
        }

        private static void UnbakeElementFont(TextElement te, string baked)
        {
            if (te.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily f
                && string.Equals(f.Source, baked, StringComparison.OrdinalIgnoreCase))
                te.ClearValue(TextElement.FontFamilyProperty);
        }

        // ---- Readability guard ----

        private const string GuardChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>A usable text font must map plain letters and digits to real glyphs.
        /// Symbol fonts (Wingdings, Webdings, Marlett...) map none of these.</summary>
        private static bool IsReadable(GlyphTypeface g) => GuardChars.All(c => g.CharacterToGlyphMap.ContainsKey(c));

        private static bool IsReadableFamily(FontFamily fam)
        {
            try
            {
                foreach (var tf in fam.GetTypefaces())
                    if (tf.TryGetGlyphTypeface(out var g) && IsReadable(g)) return true;
            }
            catch { }
            return false;   // composite-only or broken family: nothing we could verify
        }

        // ---- Dialog ----

        private void FontsRow_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("ThemeMenu") is ContextMenu m) m.IsOpen = false;
            OpenFontsDialog();
        }

        private void OpenFontsDialog()
        {
            if (!_fontsBuilt) RebuildFontCombos();
            SyncFontCombos();
            if (AboutOverlay.Visibility == Visibility.Visible) FadeOverlayOut(AboutOverlay);
            if (ShortcutOverlay.Visibility == Visibility.Visible) FadeOverlayOut(ShortcutOverlay);
            FadeOverlayIn(FontsOverlay);
        }

        private void HideFontsOverlay() => FadeOverlayOut(FontsOverlay);
        private void FontsOverlay_Click(object sender, MouseButtonEventArgs e) => HideFontsOverlay();
        private void FontsCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
        private void FontsClose_Click(object sender, RoutedEventArgs e) => HideFontsOverlay();

        /// <summary>Default row + imported files + every installed family, shared by both
        /// combos. Installed families are listed unfiltered (guarding all ~300 up front
        /// costs seconds); the guard runs on selection instead.</summary>
        private void RebuildFontCombos()
        {
            _fontsBuilt = true;
            var items = new List<FontChoice>();

            try
            {
                if (Directory.Exists(FontsDir))
                {
                    var seen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var fam in Fonts.GetFontFamilies(new Uri(FontsDir + Path.DirectorySeparatorChar)))
                    {
                        var name = fam.Source[(fam.Source.IndexOf('#') + 1)..];
                        if (seen.Add(name))
                            items.Add(new FontChoice { Display = name, Value = "file:" + name, Fam = fam });
                    }
                }
            }
            catch { /* unreadable fonts folder: imported entries just don't list */ }

            foreach (var fam in Fonts.SystemFontFamilies.OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase))
                items.Add(new FontChoice { Display = fam.Source, Value = "sys:" + fam.Source, Fam = fam });

            // Each combo gets its own list (selection state never crosses) headed by its
            // own Default row, named for what that slot's shipped default actually is.
            List<FontChoice> WithDefault(string resKey)
            {
                var (name, fam) = ShippedDefault(resKey);
                var l = new List<FontChoice>(items.Count + 1)
                {
                    new()
                    {
                        Display = name.Length > 0 ? $"{Loc("Str_Fonts_Default")} ({name})" : Loc("Str_Fonts_Default"),
                        Value = "", Fam = fam,
                    },
                };
                l.AddRange(items);
                return l;
            }
            _fontsSyncing = true;
            FontHeaderCombo.ItemsSource = WithDefault("HeaderFont");
            FontSidebarCombo.ItemsSource = WithDefault("SidebarFont");
            FontContentCombo.ItemsSource = WithDefault("ContentFont");
            _fontsSyncing = false;
        }

        /// <summary>A slot's PRISTINE Controls.xaml default. The user's override lives in
        /// the top-level application dictionary; the merged dictionaries still hold the
        /// shipped value, so this stays right even while an override is active. The name
        /// is shortened for the label: first family of a fallback list ("Bahnschrift,
        /// Segoe UI" - Bahnschrift), pack fonts trimmed to the part before " - "
        /// ("Typewriter - a602 ..." - Typewriter).</summary>
        private static (string Name, FontFamily? Fam) ShippedDefault(string resKey)
        {
            foreach (var d in Application.Current.Resources.MergedDictionaries)
                if (d.Contains(resKey) && d[resKey] is FontFamily f)
                {
                    string s = f.Source ?? "";
                    int h = s.IndexOf('#'); if (h >= 0) s = s[(h + 1)..];
                    int c = s.IndexOf(','); if (c >= 0) s = s[..c];
                    int dash = s.IndexOf(" - ", StringComparison.Ordinal); if (dash >= 0) s = s[..dash];
                    return (s.Trim(), f);
                }
            return ("", null);
        }

        /// <summary>Points both combos at their saved values without re-triggering apply.</summary>
        private void SyncFontCombos()
        {
            _fontsSyncing = true;
            SelectChoice(FontHeaderCombo, App.GetSetting(SetHeader) ?? "");
            SelectChoice(FontSidebarCombo, App.GetSetting(SetSidebar) ?? "");
            SelectChoice(FontContentCombo, App.GetSetting(SetContent) ?? "");
            _fontsSyncing = false;
        }

        private static void SelectChoice(ComboBox cb, string value)
        {
            if (cb.ItemsSource is not IEnumerable<FontChoice> items) return;
            cb.SelectedItem = items.FirstOrDefault(f => f.Value == value)
                           ?? items.FirstOrDefault(f => f.Value.Length == 0);
        }

        /// <summary>The wheel browses fonts. Hovering a CLOSED combo steps one font per
        /// notch with live apply - the whole point of the dialog - skipping fonts that
        /// fail the readability guard so the wheel never wedges on a symbol font. Over
        /// the OPEN dropdown it scrolls the list instead (driven by hand: wheel events
        /// route through the combo but do not reliably reach the ScrollViewer inside a
        /// custom template's Popup).</summary>
        private void FontCombo_Wheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            e.Handled = true;

            if (cb.IsDropDownOpen)
            {
                if (cb.Template.FindName("PART_Popup", cb) is System.Windows.Controls.Primitives.Popup p
                    && p.Child is DependencyObject root && FindScroller(root) is ScrollViewer sv)
                {
                    int notches = Math.Max(1, Math.Abs(e.Delta) / 120);
                    for (int i = 0; i < notches * 3; i++)   // 3 rows per notch, the usual feel
                    {
                        if (e.Delta > 0) sv.LineUp(); else sv.LineDown();
                    }
                }
                return;
            }

            if (cb.ItemsSource is not IList<FontChoice> items || items.Count == 0) return;
            int dir = e.Delta < 0 ? 1 : -1;
            for (int i = (cb.SelectedIndex < 0 ? 0 : cb.SelectedIndex) + dir;
                 i >= 0 && i < items.Count; i += dir)
            {
                var fc = items[i];
                if (fc.Value.Length == 0) { cb.SelectedIndex = i; return; }   // the Default row
                var fam = ResolveFamily(fc.Value);
                if (fam is not null && IsReadableFamily(fam)) { cb.SelectedIndex = i; return; }
            }
        }

        private static ScrollViewer? FindScroller(DependencyObject d)
        {
            if (d is ScrollViewer s) return s;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                if (FindScroller(VisualTreeHelper.GetChild(d, i)) is ScrollViewer found) return found;
            return null;
        }

        private void FontCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_fontsSyncing || sender is not ComboBox cb || cb.SelectedItem is not FontChoice fc) return;
            var (setting, resKey) =
                ReferenceEquals(cb, FontHeaderCombo) ? (SetHeader, "HeaderFont")
                : ReferenceEquals(cb, FontSidebarCombo) ? (SetSidebar, "SidebarFont")
                : (SetContent, "ContentFont");

            if (fc.Value.Length != 0)
            {
                var fam = ResolveFamily(fc.Value);
                if (fam is null || !IsReadableFamily(fam))
                {
                    FlashStatus(Loc("Str_Fonts_Bad"));
                    _fontsSyncing = true;
                    SelectChoice(cb, App.GetSetting(setting) ?? "");
                    _fontsSyncing = false;
                    return;
                }
            }
            App.SetSetting(setting, fc.Value);
            ApplyFontSlot(setting, resKey);
        }

        private void FontsReset_Click(object sender, RoutedEventArgs e)
        {
            App.SetSetting(SetHeader, "");
            App.SetSetting(SetSidebar, "");
            App.SetSetting(SetContent, "");
            InitFonts();
            SyncFontCombos();
        }

        // ---- Drag-in (.ttf / .otf dropped on the card) ----

        private void FontsCard_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string? added = null;
            foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") continue;
                try
                {
                    var g = new GlyphTypeface(new Uri(f));
                    if (!IsReadable(g)) { FlashStatus(Loc("Str_Fonts_Bad")); continue; }
                    Directory.CreateDirectory(FontsDir);
                    File.Copy(f, Path.Combine(FontsDir, Path.GetFileName(f)), overwrite: true);
                    added = g.FamilyNames.Values.FirstOrDefault() ?? Path.GetFileNameWithoutExtension(f);
                }
                catch { FlashStatus(Loc("Str_Fonts_Bad")); }
            }
            if (added is null) return;
            RebuildFontCombos();   // the imported section changed
            SyncFontCombos();
            FlashStatus(string.Format(Loc("Str_St_FontAdded"), added));
        }
    }
}

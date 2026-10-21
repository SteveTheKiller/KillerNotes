using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Accessibility audit (1.6.0, KN-P08), read from the sources so it covers every window and
    /// dialog without building them: every control a keyboard or screen reader can reach has a
    /// name, nothing interactive is cut off from the keyboard without another route, every shared
    /// control style shows a focus ring the theme can see, the app is per-monitor DPI aware, and
    /// colours written straight into the UI stay the decorative ones reviewed here, so high
    /// contrast keeps replacing everything that carries meaning.
    /// </summary>
    public sealed class AccessibilityAuditTests
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly HashSet<string> Interactive = new(StringComparer.Ordinal)
        {
            "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "ComboBox", "TextBox",
            "PasswordBox", "Slider", "ListBox", "MenuItem",
        };

        private static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj"))) return dir.FullName;
            throw new InvalidOperationException("Cannot locate application sources");
        }

        private static IEnumerable<string> WindowXaml() =>
            Directory.GetFiles(Path.Combine(Root(), "Shell"), "*.xaml")
                .Concat(Directory.GetFiles(Path.Combine(Root(), "Controls"), "*.xaml", SearchOption.AllDirectories))
                .Where(f => !f.EndsWith("Controls.xaml", StringComparison.Ordinal) && !f.EndsWith("PickerStyles.xaml", StringComparison.Ordinal)
                            && !f.EndsWith("OverlayScrollViewer.xaml", StringComparison.Ordinal));

        private static string AllCode() => string.Join("\n",
            new[] { "Shell", "Controls", "SharedPicker", "Features" }
                .SelectMany(d => Directory.GetFiles(Path.Combine(Root(), d), "*.cs", SearchOption.AllDirectories))
                .Select(File.ReadAllText));

        private static bool IsGlyph(string? text) =>
            !string.IsNullOrWhiteSpace(text) && text!.Trim().All(c => (c >= '\uE000' && c <= '\uF8FF') || char.IsWhiteSpace(c));

        /// <summary>Styles that give their control a name from its ToolTip or Tag, from every XAML file.</summary>
        private static Dictionary<string, string> NamingStyles()
        {
            var styles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(Path.Combine(Root(), "Shell"), "*.xaml")
                         .Concat(Directory.GetFiles(Path.Combine(Root(), "Controls"), "*.xaml", SearchOption.AllDirectories)))
                foreach (var style in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "Style"))
                {
                    var setter = style.Elements().FirstOrDefault(s => s.Name.LocalName == "Setter" && (string?)s.Attribute("Property") == "AutomationProperties.Name");
                    if (setter == null || (string?)style.Attribute(X + "Key") is not string key) continue;
                    string value = (string?)setter.Attribute("Value") ?? "";
                    styles[key] = value.Contains("Path=ToolTip") ? "ToolTip" : value.Contains("Path=Tag") ? "Tag" : "Name";
                }
            return styles;
        }

        [Fact]
        public void EveryInteractiveControlHasAName()
        {
            var styles = NamingStyles();
            string code = AllCode();
            // Surfaces the shell names at run time (ActionShortcuts.cs DescribeSurface, aliases and
            // action-shortcut surfaces), and controls whose text is set in code.
            var described = new HashSet<string>(Regex.Matches(File.ReadAllText(Path.Combine(Root(), "Shell", "ActionShortcuts.cs")),
                "\\(\"(\\w+)\", \"Str_|, \"(\\w+)\"\\),?\\r?\\n").Cast<Match>()
                .Select(m => m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value), StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string file in WindowXaml())
                foreach (var el in XDocument.Load(file).Descendants().Where(e => Interactive.Contains(e.Name.LocalName)))
                {
                    if ((string?)el.Attribute("Focusable") == "False" && (string?)el.Attribute("IsHitTestVisible") == "False") continue;   // drawn separators
                    string name = (string?)el.Attribute(X + "Name") ?? "";
                    string? style = Regex.Match((string?)el.Attribute("Style") ?? "", @"StaticResource (\w+)") is { Success: true } s ? s.Groups[1].Value : null;
                    bool named =
                        el.Attribute("AutomationProperties.Name") != null || el.Attribute("AutomationProperties.LabeledBy") != null ||
                        (el.Attribute("Header") is { } header && !IsGlyph(header.Value)) ||
                        (el.Attribute("Content") is { } content && !IsGlyph(content.Value)) ||
                        el.Descendants().Any(d => d.Name.LocalName is "TextBlock" or "Run" or "AccessText" &&
                            ((string?)d.Attribute("Text") ?? d.Value) is string t && t.Trim().Length > 0 && !IsGlyph(t)) ||
                        (style != null && styles.TryGetValue(style, out var from) &&
                            (from == "Name" || el.Attribute(from) != null)) ||
                        (name.Length > 0 && (described.Contains(name) ||
                            Regex.IsMatch(code, $@"\b{name}\.(Content|Header|Text)\s*=|AutomationProperties\.SetName\({name}\b|{name}\.SetResourceReference\((ContentControl\.)?ContentProperty")));
                    if (!named) missing.Add($"{Path.GetFileName(file)}: {el.Name.LocalName} {name}".TrimEnd());
                }
            Assert.True(missing.Count == 0, "Unnamed controls:\n" + string.Join("\n", missing));
        }

        /// <summary>Interactive controls taken out of the keyboard order, each with the other way in.</summary>
        private static readonly Dictionary<string, string> KeyboardRoutes = new(StringComparer.Ordinal)
        {
            ["RecentsBtn"] = "F4 or Alt+Down opens recent locations (SharedPicker/FileDialog.xaml.cs)",
        };

        [Fact]
        public void NothingInteractiveIsCutOffFromTheKeyboard()
        {
            var cut = new List<string>();
            foreach (string file in WindowXaml())
                foreach (var el in XDocument.Load(file).Descendants().Where(e => Interactive.Contains(e.Name.LocalName)))
                {
                    if ((string?)el.Attribute("IsHitTestVisible") == "False") continue;   // drawn, not clickable either
                    if ((string?)el.Attribute("Focusable") != "False" && (string?)el.Attribute("IsTabStop") != "False") continue;
                    string name = (string?)el.Attribute(X + "Name") ?? "";
                    if (!KeyboardRoutes.ContainsKey(name)) cut.Add($"{Path.GetFileName(file)}: {el.Name.LocalName} {name}");
                }
            Assert.True(cut.Count == 0, "Not reachable by keyboard:\n" + string.Join("\n", cut));
            Assert.Contains("key == Key.Down && !FilterCombo.IsKeyboardFocusWithin)) { RecentsBtn_Click(", File.ReadAllText(Path.Combine(Root(), "SharedPicker", "FileDialog.xaml.cs")));
        }

        [Fact]
        public void EverySharedControlStyleUsesTheFamilyFocusRing()
        {
            // The family ring (Controls/AccessibleControls.cs) goes on every focusable control whose
            // FocusVisualStyle is null, in the theme's accent or text colour and the system highlight
            // in high contrast. A style that leaves Windows' default box keeps a dotted line in the
            // system text colour - black - which the dark themes cannot show.
            Assert.Contains("control.Focusable && control.FocusVisualStyle == null",
                File.ReadAllText(Path.Combine(Root(), "Controls", "AccessibleControls.cs")));
            var doc = XDocument.Load(Path.Combine(Root(), "Controls", "Controls.xaml"));
            var styles = doc.Descendants().Where(e => e.Name.LocalName == "Style").ToList();
            var needs = new HashSet<string> { "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "ComboBox" };
            var silent = new List<string>();
            foreach (var style in styles)
            {
                string type = ((string?)style.Attribute("TargetType") ?? "").Replace("{x:Type ", "").TrimEnd('}');
                if (!needs.Contains(type)) continue;
                var setters = style.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
                string? Value(string property) => (string?)setters.FirstOrDefault(s => (string?)s.Attribute("Property") == property)?.Attribute("Value");
                bool familyRing = Value("FocusVisualStyle") == "{x:Null}";
                bool notFocusable = Value("Focusable") == "False";
                bool basedOn = style.Attribute("BasedOn") != null;   // inherits its parent's choice
                if (!familyRing && !notFocusable && !basedOn)
                    silent.Add((string?)style.Attribute(X + "Key") ?? "(implicit " + type + ")");
            }
            Assert.True(silent.Count == 0, "Styles with no visible focus:\n" + string.Join("\n", silent));
        }

        [Fact]
        public void TheAppIsPerMonitorDpiAware()
        {
            string manifest = File.ReadAllText(Path.Combine(Root(), "app.manifest"));
            Assert.Contains("PerMonitorV2", manifest);
            Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", File.ReadAllText(Path.Combine(Root(), "KillerNotes.csproj")));
        }

        /// <summary>Colours written straight into windows and code, per file, as reviewed for 1.6.0:
        /// wordmark emboss and shadow layers, drop shadows, fade masks, the highlighter swatches, the
        /// colour picker's palette, sketch ink and the graph's node colours - none of them carries
        /// meaning that high contrast would need to replace. A new one fails here until it is
        /// reviewed and either moved to a theme resource or added with its reason.</summary>
        private static readonly Dictionary<string, int> ReviewedColours = new(StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void LiteralColoursStayTheReviewedDecorativeOnes()
        {
            var found = CountLiteralColours();
            var grown = found.Where(f => !Baseline().TryGetValue(f.Key, out int allowed) || f.Value > allowed)
                             .Select(f => $"{f.Key}: {f.Value} (reviewed {(Baseline().TryGetValue(f.Key, out int a) ? a : 0)})").ToList();
            Assert.True(grown.Count == 0, "New literal colours:\n" + string.Join("\n", grown));
        }

        private static Dictionary<string, int> CountLiteralColours()
        {
            var xaml = new Regex("(Foreground|Background|BorderBrush|Fill|Stroke|Color|CaretBrush|SelectionBrush)=\"(#[0-9A-Fa-f]{3,8}|(?!Transparent\\b|True\\b|False\\b)[A-Z][a-z]+)\"");
            var cs = new Regex(@"Brushes\.(?!Transparent\b)\w+|Color\.FromRgb|Color\.FromArgb|Colors\.(?!Transparent\b)\w+");
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string root = Root();
            foreach (string file in WindowXaml())
                counts[Rel(root, file)] = File.ReadLines(file).Sum(l => xaml.Matches(l).Count);
            foreach (string file in new[] { "Shell", "Controls", "SharedPicker" }
                         .SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories)))
                counts[Rel(root, file)] = File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).Sum(l => cs.Matches(l).Count);
            return counts.Where(c => c.Value > 0).ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static string Rel(string root, string file) => file.Substring(root.Length + 1).Replace('\\', '/');

        private static Dictionary<string, int> Baseline()
        {
            if (ReviewedColours.Count > 0) return ReviewedColours;
            foreach (string line in File.ReadAllLines(Path.Combine(Root(), "KillerNotes.Tests", "ReviewedColours.txt")))
            {
                if (line.Trim().Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var parts = line.Split('\t');
                ReviewedColours[parts[0]] = int.Parse(parts[1]);
            }
            return ReviewedColours;
        }
    }
}

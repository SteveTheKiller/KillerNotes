using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Writes every resolved theme resource for every theme and accent, one "theme/accent key value"
    /// line each, so a theme refactor can be compared value by value before and after. Not a check:
    /// it produces a file and passes or fails nothing, and does nothing unless KILLERNOTES_THEME_DUMP
    /// names the output file. Headless: it builds the dictionaries on an Application with no window.
    /// </summary>
    public sealed class ThemeDumpTests
    {
        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Fact]
        public void DumpEveryThemeValue()
        {
            string? path = Environment.GetEnvironmentVariable("KILLERNOTES_THEME_DUMP");
            if (string.IsNullOrEmpty(path)) return;
            Sta.RunApplication(() => Dump(path!));
        }

        private static void Dump(string path)
        {
            var assembly = typeof(KillerNotes.App).Assembly;
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var merged = app.Resources.MergedDictionaries;
            var previous = merged.ToArray();
            var manager = assembly.GetType("KillerNotes.Services.ThemeManager", true)!;
            var themeType = assembly.GetType("KillerNotes.Services.Theme", true)!;
            var accentType = assembly.GetType("KillerNotes.Services.Accent", true)!;
            assembly.GetType("KillerNotes.Services.AppTheme")?.GetMethod("Configure", Any)?.Invoke(null, null);
            var create = manager.GetMethod("CreateDictionary", Any);
            var lines = new List<string>();
            try
            {
                // App.xaml's dictionaries in order: slot 0 is the theme, the rest the control styles.
                merged.Clear();
                var xaml = XDocument.Load(Path.Combine(RepoRoot(), "App.xaml"));
                foreach (var source in xaml.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute("Source") != null))
                    merged.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + (string)source.Attribute("Source")!, UriKind.Relative) });

                foreach (var theme in Enum.GetValues(themeType))
                    foreach (var accent in Enum.GetValues(accentType))
                    {
                        manager.GetField("_current", Any)!.SetValue(null, theme);
                        ResourceDictionary dictionary;
                        if (create != null)
                            dictionary = (ResourceDictionary)create.Invoke(null, new[] { theme, accent })!;
                        else
                        {
                            // Before the KillerUI engine: LoadDict(theme) reads the family's saved accent.
                            string field = theme.ToString() switch
                            {
                                "Light" => "_lightAccent", "Black" => "_blackAccent", "SE98" => "_se98Accent", _ => "_darkAccent",
                            };
                            manager.GetField(field, Any)!.SetValue(null, accent);
                            manager.GetMethod("LoadDict", Any)!.Invoke(null, new[] { theme });
                            dictionary = merged[0];
                        }
                        merged[0] = dictionary;
                        var keys = new SortedSet<string>(StringComparer.Ordinal);
                        void Collect(ResourceDictionary d)
                        {
                            foreach (var key in d.Keys) if (key is string s) keys.Add(s);
                            foreach (var child in d.MergedDictionaries) Collect(child);
                        }
                        foreach (var d in merged) Collect(d);
                        foreach (string key in keys)
                            lines.Add(theme + "/" + accent + " " + key + " " + Describe(app.TryFindResource(key)));
                    }
            }
            finally
            {
                merged.Clear();
                foreach (var d in previous) merged.Add(d);
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static string Describe(object? value) => value switch
        {
            null => "null",
            SolidColorBrush b => "brush " + b.Color + " o" + F(b.Opacity),
            GradientBrush g => g.GetType().Name + " " + string.Join(",", g.GradientStops.Select(s => s.Color + "@" + F(s.Offset))) + " o" + F(g.Opacity),
            Color c => "color " + c,
            double d => "double " + F(d),
            bool b => "bool " + b,
            Thickness t => "thickness " + t,
            CornerRadius r => "radius " + r,
            DropShadowEffect e => "shadow " + e.Color + " b" + F(e.BlurRadius) + " d" + F(e.ShadowDepth) + " a" + F(e.Direction) + " o" + F(e.Opacity),
            string s => "string " + s.Replace("\n", "\\n"),
            FontFamily f => "font " + f.Source,
            Visibility v => "visibility " + v,
            FontWeight w => "weight " + w,
            _ => value.GetType().Name,
        };

        private static string F(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("KillerNotes.csproj not found above the test output.");
        }
    }
}

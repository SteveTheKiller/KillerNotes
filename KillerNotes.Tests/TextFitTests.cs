using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Text that fits (1.6.0, KN-P08). WPF scales a whole window evenly, so 200% or a second
    /// monitor never squeezes a label on its own; what cuts text off is a translation longer than
    /// the fixed width a button was given. Every fixed-width button and toggle with a localized
    /// caption is measured here in all 20 languages, in the font and size it is drawn with.
    /// </summary>
    public sealed class TextFitTests
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        private static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj"))) return dir.FullName;
            throw new InvalidOperationException("Cannot locate application sources");
        }

        /// <summary>Font size and horizontal padding of the button styles a caption is drawn in.</summary>
        private static readonly Dictionary<string, (double Size, double Padding)> Styles = new()
        {
            ["PrimaryButton"] = (14, 32), ["OutlineButton"] = (13, 12), ["SurfaceButton"] = (13, 24),
        };

        [Fact]
        public void FixedWidthCaptionsFitInEveryLanguage() => Sta.Run(() =>
        {
            var strings = Directory.GetFiles(Path.Combine(Root(), "Strings"), "*.xaml").ToDictionary(
                f => Path.GetFileNameWithoutExtension(f),
                f => XDocument.Load(f).Root!.Elements().ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value));
            var face = new Typeface("Segoe UI");
            var tooWide = new List<string>();
            var files = Directory.GetFiles(Path.Combine(Root(), "Shell"), "*.xaml")
                .Concat(Directory.GetFiles(Path.Combine(Root(), "Controls"), "*.xaml", SearchOption.AllDirectories));
            foreach (string file in files)
                foreach (var el in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName is "Button" or "ToggleButton"))
                {
                    if (!double.TryParse((string?)el.Attribute("Width"), NumberStyles.Float, CultureInfo.InvariantCulture, out double width)) continue;
                    var key = Regex.Match((string?)el.Attribute("Content") ?? "", @"DynamicResource (Str_\w+)");
                    if (!key.Success) continue;
                    string style = Regex.Match((string?)el.Attribute("Style") ?? "", @"StaticResource (\w+)").Groups[1].Value;
                    var (size, padding) = Styles.TryGetValue(style, out var s) ? s : (12.0, 12.0);
                    if (double.TryParse((string?)el.Attribute("FontSize"), NumberStyles.Float, CultureInfo.InvariantCulture, out double own)) size = own;
                    foreach (var pair in strings)
                    {
                        string locale = pair.Key;
                        if (!pair.Value.TryGetValue(key.Groups[1].Value, out string? text)) continue;
                        // Measured at 2.0 pixels per DIP: 200%, where hinting widens text the most.
                        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Brushes.Black, 2.0);
                        if (formatted.WidthIncludingTrailingWhitespace + padding > width + 0.5)
                            tooWide.Add($"{Path.GetFileName(file)} {(string?)el.Attribute(X + "Name") ?? key.Groups[1].Value} ({width}): {locale} \"{text}\" needs {formatted.WidthIncludingTrailingWhitespace + padding:0}");
                    }
                }
            Assert.True(tooWide.Count == 0, "Captions cut off:\n" + string.Join("\n", tooWide));
        });
    }
}

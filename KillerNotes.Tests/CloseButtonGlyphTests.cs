using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The family close button's template shows its content and draws no X of its own, so
    /// a close button with no content is a blank square, and a red one on hover.</summary>
    public sealed class CloseButtonGlyphTests
    {
        private static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj"))) return dir.FullName;
            throw new InvalidOperationException("Cannot locate application sources");
        }

        private static IEnumerable<string> Xaml() =>
            new[] { "Shell", "Controls" }.SelectMany(folder => Directory.GetFiles(Path.Combine(Root(), folder), "*.xaml"));

        private static bool UsesStyle(XElement button, string style) =>
            (string?)button.Attribute("Style") == "{StaticResource " + style + "}";

        [Fact]
        public void TheDialogCloseStyleCarriesTheX()
        {
            var style = XDocument.Load(Path.Combine(Root(), "Controls", "AppStyles.xaml")).Descendants()
                .Single(e => e.Name.LocalName == "Style" && e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == "DialogCloseButton"));

            Assert.Contains(style.Elements(), setter => (string?)setter.Attribute("Property") == "Content"
                && (string?)setter.Attribute("Value") == "");
        }

        [Fact]
        public void EveryWindowCloseButtonDrawsItsX()
        {
            var blank = new List<string>();
            foreach (string file in Xaml())
                foreach (var button in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "Button" && UsesStyle(e, "ChromeCloseButton")))
                    if (button.Attribute("Content") == null && !button.Nodes().Any())
                        blank.Add(Path.GetFileName(file) + " " + (string?)button.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")));

            Assert.Empty(blank);
        }
    }
}

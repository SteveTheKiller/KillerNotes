using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class KeyboardFocusRingStyleTests
    {
        // A style that sets FocusVisualStyle to null switches the family keyboard ring off for every
        // control that uses it (the rail, the folder tree, check rows, toolbar buttons and the danger
        // close button) once the style is applied again, as it is on a theme change. WPF only draws
        // FocusVisualStyle for keyboard focus, so leaving it to the family ring keeps clicks ring-free.
        [Fact]
        public void NoSharedStyleSwitchesTheKeyboardFocusRingOff()
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "Controls", "Controls.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var off = xaml.Descendants()
                .Where(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "FocusVisualStyle"
                    && (string?)e.Attribute("Value") == "{x:Null}")
                .Select(e => (string?)e.Parent?.Attribute(x + "Key") ?? "(implicit)")
                .ToList();
            Assert.Empty(off);
        }

        // The main window's own styles follow the same rule, unless the style draws its own
        // keyboard cue (a KeyboardFocus border or an IsKeyboardFocused trigger in its template).
        [Fact]
        public void MainWindowStylesKeepAKeyboardFocusCue()
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var off = xaml.Descendants()
                .Where(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "FocusVisualStyle"
                    && (string?)e.Attribute("Value") == "{x:Null}")
                .Select(e => e.Parent!)
                .Where(style => !style.Descendants().Any(d =>
                    (string?)d.Attribute(x + "Name") == "KeyboardFocus" ||
                    ((string?)d.Attribute("Property") == "IsKeyboardFocused" && d.Name.LocalName == "Trigger")))
                .Select(style => (string?)style.Attribute(x + "Key") ?? "(implicit)")
                .ToList();
            Assert.Empty(off);
        }

        private static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj"))) return dir.FullName;
            throw new InvalidOperationException("KillerNotes.csproj was not found above the test output.");
        }
    }
}

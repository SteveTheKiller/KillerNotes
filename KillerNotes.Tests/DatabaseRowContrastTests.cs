using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Markup;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class DatabaseRowContrastTests
    {
        [Fact]
        public void EveryThemeAndAccentKeepsDatabaseNamesAndMetadataReadable() => Sta.Run(() =>
        {
            string root = Root();
            foreach (Theme theme in Enum.GetValues(typeof(Theme)))
            foreach (Accent accent in Enum.GetValues(typeof(Accent)))
            {
                bool hasAccents = theme is Theme.Dark or Theme.Light or Theme.Black or Theme.SE98;
                if (!hasAccents && accent != Accent.Green) continue;
                string family = theme == Theme.SE98 ? "98SE" : theme.ToString();
                var palette = (ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(root, "Themes", family + ".xaml")));
                if (hasAccents && accent != Accent.Green)
                {
                    var overlay = (ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(root, "Themes", "Accents", family, accent + ".xaml")));
                    foreach (var key in overlay.Keys) palette[key] = overlay[key];
                }
                if (!palette.Contains("PaneShadowOpacity")) palette["PaneShadowOpacity"] = .6;
                DatabaseRowPalette.Complete(palette);
                Color pane = ((SolidColorBrush)palette["PaneBrush"]).Color;
                Color hover = DatabaseRowPalette.Over(((SolidColorBrush)palette["RowHoverBrush"]).Color, pane);
                foreach (string key in new[] { "TextBrush", "DatabaseActiveTextBrush", "DatabaseMetadataBrush" })
                {
                    Color foreground = ((SolidColorBrush)palette[key]).Color;
                    Check(foreground, pane, theme + "/" + accent + " resting " + key);
                    Check(foreground, hover, theme + "/" + accent + " hover " + key);
                }
                Color selectedText = ((SolidColorBrush)palette["DatabaseSelectionTextBrush"]).Color;
                if (palette["DatabaseSelectionBrush"] is SolidColorBrush solid)
                    Check(selectedText, solid.Color, theme + "/" + accent + " selected");
                else
                    foreach (var stop in ((LinearGradientBrush)palette["DatabaseSelectionBrush"]).GradientStops)
                        Check(selectedText, stop.Color, theme + "/" + accent + " selected gradient");
                if (theme == Theme.Black && accent == Accent.Magenta) Assert.Equal(Colors.White, selectedText);
                if (theme == Theme.SE98) Assert.IsType<SolidColorBrush>(palette["DatabaseSelectionBrush"]);
            }
        });

        private static void Check(Color foreground, Color background, string label)
        {
            double contrast = DatabaseRowPalette.Contrast(DatabaseRowPalette.Over(foreground, background), background);
            Assert.True(contrast >= 4.5, label + " contrast=" + contrast);
        }

        [Fact]
        public void DatabaseRowsUseTheirMatchedPaletteForFillNamesAndMetadata()
        {
            string root = Root();
            string template = File.ReadAllText(Path.Combine(root, "Controls", "DatabasesDialog.xaml"));
            Assert.Contains("Value=\"{DynamicResource DatabaseSelectionBrush}\"", template);
            Assert.Contains("Value=\"{DynamicResource DatabaseSelectionTextBrush}\"", template);
            string rows = File.ReadAllText(Path.Combine(root, "Controls", "DatabasesDialog.xaml.cs"));
            Assert.Contains("name.SetResourceReference(TextBlock.ForegroundProperty, \"DatabaseSelectionTextBrush\");", rows);
            Assert.Contains("meta.SetResourceReference(TextBlock.ForegroundProperty, \"DatabaseSelectionTextBrush\");", rows);
            Assert.DoesNotContain("meta.Opacity = 0.78", rows);
            Assert.Contains("box.SetResourceReference(TextBox.BackgroundProperty, \"PaneBrush\");", rows);
            Assert.Contains("box.SetResourceReference(TextBox.SelectionBrushProperty, \"DatabaseSelectionBrush\");", rows);
            Assert.Contains("box.SetResourceReference(TextBox.SelectionTextBrushProperty, \"DatabaseSelectionTextBrush\");", rows);
            string completion = File.ReadAllText(Path.Combine(root, "Services", "ThemeManager.cs"));
            Assert.True(completion.IndexOf("DatabaseRowPalette.Complete(newDict);", StringComparison.Ordinal) >
                completion.IndexOf("newDict[key] = accentDict[key];", StringComparison.Ordinal));
        }

        private static string Root()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj"))) return directory.FullName;
            throw new DirectoryNotFoundException("KillerNotes source root not found");
        }
    }
}

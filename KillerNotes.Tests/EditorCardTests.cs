using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The editor's right-click menu is a card (1.7.0): a clipboard strip, recording and table
    /// strips, the view switches as chips and segments, and only the destructive table rows as a
    /// list. The shortcut table names its buttons as surfaces, so a renamed button silently drops
    /// the key from its tooltip; these tests hold the names, the shape and the look.
    /// </summary>
    [Collection("Application resources")]
    public sealed class EditorCardTests
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        private static XElement EditorCard() =>
            XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml")).Descendants()
                .Single(e => (string?)e.Attribute(X + "Name") == "EditorCard");

        [Fact]
        public void EveryShortcutSurfaceStillExists()
        {
            string src = File.ReadAllText(Path.Combine(Root(), "Shell", "ActionShortcuts.cs"));
            string xaml = File.ReadAllText(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var surfaces = Regex.Matches(src, @"\), ""([A-Za-z]+)""\),").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.NotEmpty(surfaces);
            foreach (string name in surfaces)
                Assert.True(xaml.Contains("x:Name=\"" + name + "\""), $"The shortcut table names {name}, which no longer exists, so its key drops off the tooltip.");
        }

        [Fact]
        public void TheEditorMenuIsACardWithNoSubmenus()
        {
            var card = EditorCard();
            Assert.Equal("{StaticResource CardMenu}", (string?)card.Attribute("Style"));
            foreach (string name in new[] { "EditorCutBtn", "EditorCopyBtn", "EditorPasteBtn", "EditorLinkBtn", "EditorListBtn",
                         "RecPlayPauseBtn", "TblRowAboveBtn", "TableStyleBtn", "WordWrapToggle", "SyntaxToggle", "SpellToggle",
                         "PageWidthFullSeg", "PreviewRenderedSeg", "TblDeleteMenuItem" })
                Assert.Contains(card.Descendants(), e => (string?)e.Attribute(X + "Name") == name);
            // A card holds no flyout rows: every MenuItem is a slot or a leaf.
            Assert.DoesNotContain(card.Descendants().Where(e => e.Name.LocalName == "MenuItem"),
                e => e.Elements().Any(c => c.Name.LocalName == "MenuItem"));
        }

        [Fact]
        public void TheCardRendersOnDark() => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var merged = app.Resources.MergedDictionaries;
            var saved = merged.ToList();
            merged.Clear();
            merged.Add(ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark)));
            foreach (string r in new[] { "Controls/Controls.xaml", "Strings/en-US.xaml", "Controls/AppStyles.xaml" })
                merged.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + r, UriKind.Relative) });
            try
            {
                // A ContextMenu only draws in its own popup window, so its rows are laid out in a
                // panel on the card's surface instead: the same row templates, no window.
                var menu = LoadCard(showAll: true);
                // As a real note shows them: wrap on, full width, rendered preview.
                foreach (string on in new[] { "WordWrapToggle", "PageWidthFullSeg", "PreviewRenderedSeg" })
                    ((System.Windows.Controls.Primitives.ToggleButton)menu.FindName(on)).IsChecked = true;
                var rows = menu.Items.Cast<object>().ToList();
                menu.Items.Clear();
                var panel = new StackPanel();
                foreach (var row in rows) panel.Children.Add((UIElement)row);
                var (minWidth, maxWidth) = CardWidths();
                var host = new Border { MinWidth = minWidth, MaxWidth = maxWidth, Padding = new Thickness(6), Child = panel };
                host.SetResourceReference(Border.BackgroundProperty, "MenuBackgroundBrush");
                host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                host.Arrange(new Rect(host.DesiredSize));
                host.UpdateLayout();
                Assert.True(host.ActualHeight > 200, "The card laid out empty.");

                string? outPath = Environment.GetEnvironmentVariable("KN_EDITOR_CARD_PNG");
                if (outPath != null)
                {
                    var bmp = new RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(host);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using var f = File.Create(outPath);
                    enc.Save(f);
                }
            }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
        });

        /// <summary>The "Rendered" chip read as a label: it cycled on click, with no sign it was a
        /// button. It now carries an eye and a chevron and opens the three views with their keys.</summary>
        [Fact]
        public void ThePreviewChipIsADropdown()
        {
            var doc = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            foreach (string name in new[] { "PreviewTitleBtn", "PreviewModeBtn" })
            {
                var chip = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);
                var glyphs = chip.Descendants().Select(e => (string?)e.Attribute("Text")).ToList();
                Assert.Contains("\uE890", glyphs);   // eye
                Assert.Contains("\uE70D", glyphs);   // chevron
            }
            string src = File.ReadAllText(Path.Combine(Root(), "Shell", "Preview.cs"));
            int start = src.IndexOf("private void PreviewModeBtn_Click", StringComparison.Ordinal);
            string body = src.Substring(start, src.IndexOf("menu.IsOpen = true;", start, StringComparison.Ordinal) - start);
            Assert.DoesNotContain("CyclePreviewMode", body);
            foreach (string key in new[] { "Ctrl+Shift+F6", "Ctrl+Shift+F7", "Ctrl+Shift+F8", "AttachBelowRight" })
                Assert.Contains(key, body);
        }

        /// <summary>A disabled chip or segment dims like a disabled quick button. The graph's Lock
        /// chip on a note not written yet is disabled, and it looked like it would work.</summary>
        [Fact]
        public void DisabledChipsAndSegmentsDim() => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var merged = app.Resources.MergedDictionaries;
            var saved = merged.ToList();
            merged.Clear();
            merged.Add(ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark)));
            merged.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/Controls/Controls.xaml", UriKind.Relative) });
            try
            {
                foreach (var control in new System.Windows.Controls.Primitives.ToggleButton[]
                         { new System.Windows.Controls.Primitives.ToggleButton { Content = "Lock" }, new RadioButton { Content = "Full" } })
                {
                    control.Style = (Style)app.FindResource(control is RadioButton ? "CardSegment" : "CardToggle");
                    control.IsEnabled = false;
                    control.Measure(new Size(200, 40));
                    control.Arrange(new Rect(0, 0, 200, 40));
                    var bd = (Border)control.Template.FindName("Bd", control);
                    Assert.Equal(0.4, bd.Opacity, 2);
                }
            }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
        });

        /// <summary>Every quick-button caption sits on one line and every chip and segment row
        /// fits, in every language, at the card's own width. "Column right" wrapped to two lines
        /// beside one-line neighbours, which read as a mistake.</summary>
        [Fact]
        public void EveryCaptionFitsOnOneLineInEveryLanguage() => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var merged = app.Resources.MergedDictionaries;
            var saved = merged.ToList();
            var (minWidth, maxWidth) = CardWidths();
            var problems = new System.Collections.Generic.List<string>();
            try
            {
                foreach (string file in Directory.GetFiles(Path.Combine(Root(), "Strings"), "*.xaml"))
                {
                    string lang = Path.GetFileNameWithoutExtension(file);
                    merged.Clear();
                    merged.Add(ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark)));
                    foreach (string r in new[] { "Controls/Controls.xaml", $"Strings/{lang}.xaml", "Controls/AppStyles.xaml" })
                        merged.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + r, UriKind.Relative) });

                    var menu = LoadCard(showAll: true);
                    var rows = menu.Items.Cast<object>().ToList();
                    menu.Items.Clear();
                    var panel = new StackPanel();
                    foreach (var row in rows) panel.Children.Add((UIElement)row);
                    // Sized the way the popup sizes it: natural width, held between the card's
                    // MinWidth and MaxWidth.
                    var host = new Border { MinWidth = minWidth, MaxWidth = maxWidth, Padding = new Thickness(6), Child = panel };
                    host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    host.Arrange(new Rect(host.DesiredSize));
                    host.UpdateLayout();
                    if (lang == "en-US" && host.ActualWidth > minWidth + 0.5)
                        problems.Add($"en-US: the card grew to {host.ActualWidth:0} past its {minWidth:0}");

                    foreach (var caption in Descendants(host).OfType<TextBlock>().Where(t => t.Name == "Caption"))
                        if (caption.ActualHeight > caption.FontSize * 1.8)
                            problems.Add($"{lang}: caption '{caption.Text}' wraps");
                    foreach (var slot in panel.Children.OfType<MenuItem>())
                        if (slot.Header is FrameworkElement header && header.DesiredSize.Width > header.ActualWidth + 0.5)
                            problems.Add($"{lang}: row '{System.Windows.Automation.AutomationProperties.GetName(slot)}' runs past the card");
                }
            }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        });

        /// <summary>The editor card's own width range: its MinWidth and MaxWidth less the
        /// template's 22px halo on each side.</summary>
        private static (double Min, double Max) CardWidths() =>
            ((double)EditorCard().Attribute("MinWidth")! - 44, (double)EditorCard().Attribute("MaxWidth")! - 44);

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        /// <summary>The card's XAML from MainWindow.xaml, without its event wiring, every part shown.</summary>
        private static Menu LoadCard(bool showAll)
        {
            var card = new XElement(EditorCard());
            foreach (var a in card.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName == "Click").ToList()) a.Remove();
            if (showAll)
                foreach (var a in card.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName == "Visibility").ToList()) a.Remove();
            card.SetAttributeValue(XNamespace.Xmlns + "x", X.NamespaceName);
            card.Attribute("Style")?.Remove();
            card.Name = XName.Get("Menu", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            return (Menu)XamlReader.Parse(card.ToString());
        }
    }
}

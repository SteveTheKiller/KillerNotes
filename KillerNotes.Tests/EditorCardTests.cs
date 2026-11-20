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

        [Fact]
        public void ThePreviewPickerUsesTheFamilyDropdownAndSynchronizesBothLocations() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var picker = Assert.IsType<ComboBox>(window.FindName("PreviewModeBtn"));
            var title = Assert.IsType<ComboBox>(window.FindName("PreviewTitleBtn"));
            Assert.Same(Application.Current.FindResource(typeof(ComboBox)), picker.Style);
            Assert.Equal(3, picker.Items.Count);
            var source = (ComboBoxItem)picker.Items[0];
            Assert.Equal("</>", ((StackPanel)source.Content).Children.OfType<TextBlock>().First().Text);
            var editor = (RichTextBox)window.FindName("Editor");
            foreach (string mode in new[] { "Rendered", "Split", "Source" })
            {
                picker.SelectedValue = mode;
                Assert.Equal(mode, title.SelectedValue);
                Assert.Equal(mode == "Rendered" ? Visibility.Collapsed : Visibility.Visible, editor.Visibility);
            }
        });

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
        public void EveryCaptionFitsItsTileInEveryLanguage() => Sta.RunApplication(() =>
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

                    // The kit caps a tile caption's width so a long one wraps inside its tile instead of widening
                    // the card (KillerUI card rule 1); it must still fit the two lines the caption shows.
                    foreach (var caption in Descendants(host).OfType<TextBlock>().Where(t => t.Name == "Caption"))
                    {
                        var ft = new FormattedText(caption.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch), caption.FontSize,
                            Brushes.Black, null, TextFormattingMode.Ideal, 1.0) { MaxTextWidth = Math.Max(1, caption.ActualWidth) };
                        if (ft.Height > caption.MaxHeight + 0.5)
                            problems.Add($"{lang}: caption '{caption.Text}' needs more than two lines");
                    }
                    foreach (var slot in panel.Children.OfType<MenuItem>())
                        if (slot.Header is FrameworkElement header && header.DesiredSize.Width - header.Margin.Left - header.Margin.Right > header.ActualWidth + 0.5)
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

        /// <summary>One left edge: the labels, the strips' rules, the separators and the row icons
        /// all start on the icon column. The labels sat 16px further in, past an icon gutter
        /// they do not use, and the strips' rules ran 8px further out than everything else.</summary>
        [Fact]
        public void EverythingStartsOnOneLeftEdge() => Sta.RunApplication(() =>
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
                var menu = LoadCard(showAll: true);
                var rows = menu.Items.Cast<object>().ToList();
                menu.Items.Clear();
                var panel = new StackPanel();
                foreach (var row in rows) panel.Children.Add((UIElement)row);
                var host = new Border { Width = 320, Child = panel };
                host.Measure(new Size(320, double.PositiveInfinity));
                host.Arrange(new Rect(host.DesiredSize));
                host.UpdateLayout();
                double Left(FrameworkElement e) => e.TransformToAncestor(host).Transform(new Point(0, 0)).X;

                var cardLabel = (Style)app.FindResource("CardLabel");
                var edges = new System.Collections.Generic.List<(string What, double X)>();
                foreach (var d in Descendants(host).OfType<FrameworkElement>())
                {
                    if (d is TextBlock { Style: var st } t && ReferenceEquals(st, cardLabel)) edges.Add(("label " + t.Text, Left(d)));
                    else if (d is Border { Child: System.Windows.Controls.Primitives.UniformGrid }) edges.Add(("strip rule", Left(d)));
                    else if (d is Separator) edges.Add(("separator", Left(d)));
                    else if (d.Name == "IconPresenter" && d.ActualWidth > 0) edges.Add(("row icon", Left(d)));
                }
                Assert.Contains(edges, e => e.What.StartsWith("label"));
                Assert.Contains(edges, e => e.What == "strip rule");
                Assert.Contains(edges, e => e.What == "row icon");
                double column = edges.First(e => e.What == "row icon").X;
                var off = edges.Where(e => Math.Abs(e.X - column) > 0.5).Select(e => $"{e.What} at {e.X:0.#}").ToList();
                Assert.True(off.Count == 0, $"icon column {column:0.#}; off it: " + string.Join(", ", off));
            }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
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

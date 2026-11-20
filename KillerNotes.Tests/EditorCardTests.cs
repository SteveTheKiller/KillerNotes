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
    /// The editor's right-click menu is a card: 1.7.0 built it as strips, chips and segments in XAML, and 1.7.1
    /// makes it a KillerUI tile card built in EditorCard.cs. The shortcut table names its XAML surfaces, so a
    /// renamed button silently drops the key from its tooltip; these tests hold the names, the shape and the look.
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

        /// <summary>1.7.1: the editor card is a KillerUI tile card built in EditorCard.cs. The XAML element is an
        /// empty card, and the builder names every tile the old strips carried.</summary>
        [Fact]
        public void TheEditorMenuIsATileCard()
        {
            var card = EditorCard();
            Assert.Equal("{StaticResource CardMenu}", (string?)card.Attribute("Style"));
            Assert.Empty(card.Elements());
            string src = File.ReadAllText(Path.Combine(Root(), "Shell", "EditorCard.cs"));
            Assert.Contains("NoteCards.Editable(EditorCard, \"Editor\"", src);
            foreach (string id in new[] { "cut", "copy", "paste", "link", "list", "play", "rowabove", "tablestyle",
                         "wrap", "syntax", "spell", "width", "preview", "deltable" })
                Assert.Contains("\"" + id + "\"", src);
        }

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

        /// <summary>Every editor tile caption fits the two lines a tile shows, in every language, on the kit's
        /// three-across tile grid. "Column right" wrapped past its tile in 1.7.0, which read as a mistake.</summary>
        [Fact]
        public void EveryCaptionFitsItsTileInEveryLanguage() => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var merged = app.Resources.MergedDictionaries;
            var saved = merged.ToList();
            // The caption a tile shows: its Shown key where it has one (a shorter word), else its caption key.
            string src = File.ReadAllText(Path.Combine(Root(), "Shell", "EditorCard.cs"));
            var keys = src.Split('\n').SelectMany(line =>
                {
                    var shown = Regex.Match(line, @"Shown = Loc\(""(Str_[A-Za-z0-9_]+)""\)");
                    return shown.Success ? new[] { shown.Groups[1].Value }
                        : Regex.Matches(line, @"""(Str_(?:Ctx|Card|Kb|KS)_[A-Za-z0-9]+)""").Cast<Match>().Select(m => m.Groups[1].Value);
                }).Distinct().ToList();
            Assert.NotEmpty(keys);
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

                    var tiles = keys.Select(k => new KillerFamily.Controls.CardTile("", (app.TryFindResource(k) as string) ?? k, "Ctrl+Shift+F10", () => { }))
                        .ToList();
                    var card = Shell.NoteCards.Menu();
                    var grid = Shell.NoteCards.Grid(card, tiles);
                    var header = (FrameworkElement)grid.Header;
                    grid.Header = null;
                    var host = new Border { Width = Shell.NoteCards.Width, Child = header };
                    host.SetResourceReference(Border.BackgroundProperty, "MenuBackgroundBrush");
                    host.Measure(new Size(Shell.NoteCards.Width, double.PositiveInfinity));
                    host.Arrange(new Rect(host.DesiredSize));
                    host.UpdateLayout();
                    if (host.ActualHeight <= 0) problems.Add($"{lang}: the card laid out empty");
                    foreach (var caption in Descendants(host).OfType<TextBlock>().Where(t => t.Name == "Caption" && !string.IsNullOrEmpty(t.Text)))
                    {
                        var ft = new FormattedText(caption.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch), caption.FontSize,
                            Brushes.Black, null, TextFormattingMode.Ideal, 1.0) { MaxTextWidth = Math.Max(1, caption.ActualWidth) };
                        if (!double.IsInfinity(caption.MaxHeight) && ft.Height > caption.MaxHeight + 0.5)
                            problems.Add($"{lang}: caption '{caption.Text}' needs more than two lines");
                    }
                }
            }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }
    }
}

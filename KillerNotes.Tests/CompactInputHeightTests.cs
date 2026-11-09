using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using Xunit;
using Xunit.Abstractions;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Single-line text fields share the dropdown's height, so a form row of text boxes and
    /// combo boxes lines up instead of the text boxes standing taller than everything beside them.
    /// </summary>
    [Collection("Application resources")]
    public sealed class CompactInputHeightTests
    {
        private readonly ITestOutputHelper _output;
        public CompactInputHeightTests(ITestOutputHelper output) => _output = output;

        // A plain panel lays out without a window; an unshown Window measures to nothing.
        private static double Height(FrameworkElement element)
        {
            var root = new StackPanel { Width = 300, Children = { element } };
            root.Measure(new Size(300, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
            root.Children.Clear();
            return element.ActualHeight;
        }

        // Compact stands in for 98SE through the three tokens it sets. Loading the real 98SE
        // dictionary in a test host leaves the process running after the tests finish.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SingleLineFieldsMatchTheDropdownHeight(bool compact) => WithResources("Themes/Black.xaml", app =>
        {
            var tokens = new ResourceDictionary();
            if (compact)
            {
                tokens["UseDialogCaption"] = true;
                tokens["CompactInputMinHeight"] = 22.0;
                tokens["CompactInputPadding"] = new Thickness(2, 1, 2, 1);
            }
            app.Resources.MergedDictionaries.Add(tokens);
            try { Check(app, compact); }
            finally { app.Resources.MergedDictionaries.Remove(tokens); }
        });

        private void Check(Application app, bool compact)
        {
            string theme = compact ? "compact" : "Black";
            var combo = new ComboBox { Items = { "Sample" }, SelectedIndex = 0 };
            double target = Height(combo);
            Assert.True(target > 10, $"ComboBox measured {target}");
            var fields = new List<(string Name, Control Field)>
            {
                ("implicit TextBox", new TextBox { Text = "Sample" }),
            };
            foreach (string key in new[] { "DarkTextBox", "EditFieldTextBox", "EditFieldPasswordBox", "DialogPickerTextBox" })
            {
                var style = (Style)app.FindResource(key);
                Control field = style.TargetType == typeof(PasswordBox) ? new PasswordBox() : new TextBox { Text = "Sample" };
                field.Style = style;
                fields.Add((key, field));
            }
            var heights = fields.Select(f => (f.Name, Height: Height(f.Field))).ToArray();
            _output.WriteLine($"{theme}: ComboBox {target}; " + string.Join("; ", heights.Select(h => $"{h.Name} {h.Height}")));
            // A compact theme keeps its era field (CompactInputMinHeight; the 14px DarkTextBox
            // text stands it 22.62). The picker's compact rule follows its dialog's RootBorder,
            // which is not here, so it stays at 26.
            Assert.All(heights, h =>
            {
                double expected = compact && h.Name != "DialogPickerTextBox" ? 22 : target;
                Assert.True(h.Height >= expected - 0.5 && h.Height < expected + (compact ? 1 : 0.5), $"{h.Name} is {h.Height}, expected {expected}");
            });
            var multi = new TextBox { Text = "One\nTwo", AcceptsReturn = true, Height = 80, Style = (Style)app.FindResource("DarkTextBox") };
            Assert.Equal(80, Height(multi));
        }

        // The title-bar search box lives in a 24px field (22 inside its border), shorter than the
        // 26px floor. Its text must still sit in the middle of the field and not be cut off.
        [Fact]
        public void TheTitleSearchTextSitsInTheMiddleOfItsField() => WithResources("Themes/Black.xaml", app =>
        {
            var field = (Border)XamlReader.Parse(TitleSearchField());
            var host = new Grid { Width = 308, Height = 24, Children = { field } };
            host.Measure(new Size(308, 24));
            host.Arrange(new Rect(0, 0, 308, 24));
            host.UpdateLayout();
            var box = (TextBox)field.FindName("SearchBox");
            box.ApplyTemplate();
            host.UpdateLayout();
            var content = (FrameworkElement)box.Template.FindName("PART_ContentHost", box);
            double middle = content.TransformToAncestor(host).Transform(new Point(0, content.ActualHeight / 2)).Y;
            _output.WriteLine($"search box {box.ActualHeight}, content middle {middle}, clip {System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(box)?.Bounds}");
            Assert.InRange(middle, 11.5, 12.5);
            var clip = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(box);
            Assert.True(clip == null || clip.Bounds.Height >= content.ActualHeight, $"clipped to {clip?.Bounds}");
        });

        // The tag rename box keeps its whole border.
        [Fact]
        public void TheTagRenameBoxIsNotCutOff() => WithResources("Themes/Black.xaml", app =>
        {
            var box = KillerNotes.Controls.TagsDialog.CreateRenameBox("tag");
            var row = new Grid { Width = 300, Children = { box } };
            row.Measure(new Size(300, double.PositiveInfinity));
            row.Arrange(new Rect(row.DesiredSize));
            row.UpdateLayout();
            _output.WriteLine($"rename box {box.ActualHeight}");
            Assert.Null(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(box));
            Assert.True(box.ActualHeight >= 26 - 0.5, $"rename box {box.ActualHeight}");
        });

        // The search field's own markup, so the test measures what ships. Event handlers are
        // dropped and the local namespaces gain their assembly, which a loose XAML parse needs.
        private static string TitleSearchField()
        {
            string root = Root();
            var doc = XDocument.Load(Path.Combine(root, "Shell", "MainWindow.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var grid = doc.Descendants().First(e => (string?)e.Attribute(x + "Name") == "TitleNoteSearch");
            var field = new XElement(grid.Elements().First(e => e.Name.LocalName == "Border"));
            foreach (var a in field.DescendantsAndSelf().SelectMany(e => e.Attributes()).ToList())
                if (Regex.IsMatch(a.Name.LocalName, "(Click|Changed|Opening|KeyDown|MouseDown|Loaded)$")) a.Remove();
            foreach (var ns in doc.Root!.Attributes().Where(a => a.IsNamespaceDeclaration)) field.SetAttributeValue(ns.Name, ns.Value);
            string text = field.ToString();
            return Regex.Replace(text, "clr-namespace:([A-Za-z.]+)\"", "clr-namespace:$1;assembly=KillerNotes\"");
        }

        private static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj"))) return dir.FullName;
            throw new InvalidOperationException("KillerNotes.csproj was not found above the test output.");
        }

        private static void WithResources(string theme, Action<Application> check) => Sta.RunApplication(() =>
        {
            Application app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var resources = new List<ResourceDictionary>();
            try
            {
                foreach (string path in new[] { theme, "Controls/Controls.xaml", "Strings/en-US.xaml",
                    "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                {
                    var dictionary = new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + path, UriKind.Relative) };
                    app.Resources.MergedDictionaries.Add(dictionary);
                    resources.Add(dictionary);
                }
                check(app);
            }
            finally
            {
                foreach (var dictionary in resources) app.Resources.MergedDictionaries.Remove(dictionary);
            }
        });
    }
}

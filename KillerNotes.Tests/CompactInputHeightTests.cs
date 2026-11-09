using System;
using System.Collections.Generic;
using System.Linq;
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

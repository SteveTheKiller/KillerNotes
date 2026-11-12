using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A menu row's Icon set to a bare glyph string (Icon="&amp;#xE74D;", the form used all over
    /// MainWindow.xaml) has to draw in the icon font. It rendered in the menu's text font instead,
    /// which has no such glyph, so every one of those rows showed an empty gutter (SN01, 1.7.0
    /// lab check). The family template's icon slot now sets Segoe MDL2 Assets for its content.
    /// </summary>
    [Collection("Application resources")]
    public sealed class MenuIconFontTests
    {
        [Fact]
        public void AGlyphStringIconDrawsInTheIconFont() => Sta.RunApplication(() =>
        {
            var tb = RenderIcon("\uE74D");
            Assert.Equal("Segoe MDL2 Assets", tb.FontFamily.Source);
            Assert.Equal("\uE74D", tb.Text);
        });

        [Fact]
        public void AnIconElementKeepsItsOwnFont() => Sta.RunApplication(() =>
        {
            var own = new TextBlock { Text = "x", FontFamily = new FontFamily("Arial") };
            var tb = RenderIcon(own);
            Assert.Same(own, tb);
            Assert.Equal("Arial", tb.FontFamily.Source);
        });

        private static TextBlock RenderIcon(object icon)
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var previous = app.Resources.MergedDictionaries.ToArray();
            try
            {
                foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/KillerNotes;component/" + resource, UriKind.Relative)
                    });
                var item = new MenuItem { Header = "Delete note", Icon = icon };
                var menu = new ContextMenu();
                menu.Items.Add(item);
                item.ApplyTemplate();
                item.Measure(new Size(400, 100));
                item.Arrange(new Rect(0, 0, 400, 30));
                item.UpdateLayout();
                var presenter = (ContentPresenter)item.Template.FindName("IconPresenter", item);
                return First<TextBlock>(presenter)
                       ?? throw new InvalidOperationException("The icon slot drew no text");
            }
            finally
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in previous)
                    app.Resources.MergedDictionaries.Add(dictionary);
            }
        }

        private static T? First<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                if (First<T>(child) is T deeper) return deeper;
            }
            return null;
        }
    }
}

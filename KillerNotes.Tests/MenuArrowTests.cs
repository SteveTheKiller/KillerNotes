using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A row that opens a submenu shows its arrow at the row's right edge. In a menu sized to its
    /// rows that is the same place it always was, but on the note card (a fixed 360 wide) the
    /// arrow sat right after the shortcut text, stranded in the middle of the Tags and Group rows.
    /// </summary>
    [Collection("Application resources")]
    public sealed class MenuArrowTests
    {
        [Fact]
        public void TheSubmenuArrowSitsAtTheRowsRightEdge() => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var previous = app.Resources.MergedDictionaries.ToArray();
            try
            {
                foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/KillerNotes;component/" + resource, UriKind.Relative)
                    });
                var row = new MenuItem { Header = "Tags", InputGestureText = "Alt+K", HorizontalContentAlignment = HorizontalAlignment.Stretch };
                row.Items.Add(new MenuItem { Header = "Code" });
                var host = new StackPanel { Width = 360 };
                Grid.SetIsSharedSizeScope(host, true);
                host.Children.Add(row);
                host.Measure(new Size(360, 100));
                host.Arrange(new Rect(0, 0, 360, 40));
                host.UpdateLayout();

                var arrow = (TextBlock)row.Template.FindName("SubArrow", row);
                Assert.Equal(Visibility.Visible, arrow.Visibility);
                double right = arrow.TransformToAncestor(row).Transform(new Point(arrow.ActualWidth, 0)).X;
                Assert.InRange(row.ActualWidth - right, 0, row.Padding.Right + 1);
            }
            finally
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in previous) app.Resources.MergedDictionaries.Add(dictionary);
            }
        });
    }
}

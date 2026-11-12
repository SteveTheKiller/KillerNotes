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

        /// <summary>
        /// The shortcut sits beside the arrow at the right edge, not in the middle of the card, and a
        /// row with no submenu (Delete note) puts its shortcut in the same column as one that has one.
        /// </summary>
        [Fact]
        public void TheShortcutSitsNextToTheRightEdge() => Sta.RunApplication(() =>
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
                var tags = new MenuItem { Header = "Tags", InputGestureText = "Alt+K", HorizontalContentAlignment = HorizontalAlignment.Stretch };
                tags.Items.Add(new MenuItem { Header = "Code" });
                var delete = new MenuItem { Header = "Delete note", InputGestureText = "Alt+K", HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var host = new StackPanel { Width = 360 };
                Grid.SetIsSharedSizeScope(host, true);
                host.Children.Add(tags);
                host.Children.Add(delete);
                host.Measure(new Size(360, 200));
                host.Arrange(new Rect(0, 0, 360, 80));
                host.UpdateLayout();

                double GestureRight(MenuItem row)
                {
                    var gesture = (TextBlock)row.Template.FindName("Gesture", row);
                    Assert.Equal(Visibility.Visible, gesture.Visibility);
                    return gesture.TransformToAncestor(row).Transform(new Point(gesture.ActualWidth, 0)).X;
                }

                // Arrow glyph plus its 12px margin plus the row's right padding, with slack for rounding.
                Assert.InRange(tags.ActualWidth - GestureRight(tags), 0, 40);
                Assert.Equal(GestureRight(tags), GestureRight(delete), 1);
            }
            finally
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in previous) app.Resources.MergedDictionaries.Add(dictionary);
            }
        });
    }
}

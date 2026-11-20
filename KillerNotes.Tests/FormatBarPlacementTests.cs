using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class FormatBarPlacementTests
    {
        private static void Open(MainWindow window)
        {
            MarkdownAuthoringTests.Ready(window);
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
        }

        /// <summary>The bar card's Docked or Floating tile (1.7.1: the bar's own right-click is a tile card).</summary>
        private static Button PlacementTile(MainWindow window)
        {
            var bar = (Border)window.FindName("FormatBar");
            bar.RaiseEvent(new ContextMenuEventArgsShim(bar).Args);
            return Assert.IsType<Button>(NoteCards.Tile(bar.ContextMenu!, "toolmenu"));
        }

        [Fact]
        public void TheFormatBarFloatsByDefaultAndDocksFromItsMenu() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Open(window);
            var dock = (FormatBarDock)window.FindName("FormatBarHost");
            var bar = (Border)window.FindName("FormatBar");
            var title = (TextBox)window.FindName("TitleBox");
            Assert.False(MainWindow.FormatBarDocked);
            Assert.False(dock.Docked);
            Assert.Equal("Docked toolbar", System.Windows.Automation.AutomationProperties.GetName(PlacementTile(window)));

            PlacementTile(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.True(MainWindow.FormatBarDocked);
            Assert.Equal("1", App.GetSetting("FmtBarDocked"));
            Assert.True(dock.Docked);
            Assert.Equal(HorizontalAlignment.Stretch, bar.HorizontalAlignment);
            Assert.Null(bar.Effect);
            Assert.Equal(12, title.Margin.Left);
            Assert.Equal(12, title.Margin.Right);
            Assert.Equal("Floating toolbar", System.Windows.Automation.AutomationProperties.GetName(PlacementTile(window)));

            window.ToggleFormatBarPlacement();
            window.UpdateLayout();
            Assert.False(MainWindow.FormatBarDocked);
            Assert.False(dock.Docked);
            Assert.NotEqual(HorizontalAlignment.Stretch, bar.HorizontalAlignment);
        });

        [Fact]
        public void DockedTheBarCannotBeMinimizedAndComesBackMinimizedWhenFloatedAgain() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Open(window);
            var buttons = (WrapPanel)window.FindName("FmtButtons");
            window.ToggleFormatBar(animate: false);
            Assert.True(window.FormatBarMinimized);

            window.ToggleFormatBarPlacement();
            window.UpdateLayout();
            Assert.False(window.FormatBarMinimized);
            Assert.Equal(Visibility.Visible, buttons.Visibility);
            window.ToggleFormatBar(animate: false);   // F6 and the chevron do nothing while docked
            Assert.False(window.FormatBarMinimized);
            Assert.Equal("1", App.GetSetting("FmtBarMin"));

            window.ToggleFormatBarPlacement();
            window.UpdateLayout();
            Assert.True(window.FormatBarMinimized);
        });

        // Opens the bar's own right-click menu the way WPF does, so its items are built.
        private sealed class ContextMenuEventArgsShim
        {
            public RoutedEventArgs Args { get; }
            public ContextMenuEventArgsShim(FrameworkElement source)
            {
                var ctor = typeof(ContextMenuEventArgs).GetConstructor(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(object), typeof(bool) }, null)!;
                Args = (RoutedEventArgs)ctor.Invoke(new object[] { source, true });
                Args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
                Args.Source = source;
            }
        }
    }
}

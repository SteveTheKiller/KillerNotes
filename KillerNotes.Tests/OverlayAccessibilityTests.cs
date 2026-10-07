using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using KillerNotes.Controls;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class OverlayAccessibilityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static string SourcePath()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj")))
                    return Path.Combine(dir.FullName, "Shell", "MainWindow.xaml");
            throw new InvalidOperationException("Cannot locate application sources");
        }

        private static Style NavigationStyle()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = XDocument.Load(SourcePath()).Descendants().Single(e =>
                e.Name.LocalName == "Style" && (string?)e.Attribute(x + "Key") == "ShortcutNavigationList");
            var style = new XElement(source);
            style.Attribute(x + "Key")!.Remove();
            style.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
            return (Style)XamlReader.Parse(style.ToString());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ClosingReleasesTheBackgroundAndRetainsControlOrHyperlinkOrigin(bool link) => Sta.Run(() =>
        {
            var root = new Grid();
            var background = new Button();
            var decorative = new Border { IsHitTestVisible = false };
            var overlay = new Grid();
            root.Children.Add(background);
            root.Children.Add(decorative);
            root.Children.Add(overlay);
            IInputElement origin = link ? new Hyperlink(new Run("Help")) : new TextBox { Text = "Unchanged" };
            var scope = new OverlayFocusScope();
            scope.Open(root, overlay, origin);
            Assert.False(background.IsHitTestVisible);
            Assert.True(background.IsEnabled);

            Assert.Same(origin, scope.Close(overlay));
            Assert.Null(scope.ActiveOverlay);
            Assert.Equal(Visibility.Collapsed, overlay.Visibility);
            Assert.True(background.IsHitTestVisible);
            Assert.False(decorative.IsHitTestVisible);
            Assert.Null(scope.Close(overlay));
        });

        [Fact]
        public void OverlayTransitionsDoNotLeaveStaleBlockedControls() => Sta.Run(() =>
        {
            var root = new Grid();
            var background = new Button();
            var first = new Grid();
            var second = new Grid();
            root.Children.Add(background); root.Children.Add(first); root.Children.Add(second);
            var scope = new OverlayFocusScope();
            for (int i = 0; i < 3; i++)
            {
                first.Visibility = Visibility.Visible;
                scope.Open(root, first, background);
                second.Visibility = Visibility.Visible;
                scope.Open(root, second, background);
                Assert.Equal(Visibility.Collapsed, first.Visibility);
                Assert.False(background.IsHitTestVisible);
                Assert.True(second.IsHitTestVisible);
                Assert.Same(background, scope.Close(second));
                Assert.True(background.IsHitTestVisible);
            }
        });

        [Fact]
        public void HyperlinksInsideTheOverlayAreAllowedButBackgroundControlsAreNot() => Sta.Run(() =>
        {
            var root = new Grid();
            var overlay = new Grid();
            var text = new TextBlock();
            var run = new Run("Online help");
            var link = new Hyperlink(run);
            text.Inlines.Add(link);
            overlay.Children.Add(text);
            var outside = new Button();
            root.Children.Add(overlay); root.Children.Add(outside);
            var scope = new OverlayFocusScope();
            scope.Open(root, overlay, outside);
            Assert.True(scope.Contains(link));
            Assert.True(scope.Contains(run));
            Assert.False(scope.Contains(outside));
            scope.Close(overlay);
            Assert.False(scope.Contains(link));
        });

        [Theory]
        [InlineData(Key.N, ModifierKeys.Control, false)]
        [InlineData(Key.Delete, ModifierKeys.None, false)]
        [InlineData(Key.B, ModifierKeys.Control, false)]
        [InlineData(Key.E, ModifierKeys.Alt, false)]
        [InlineData(Key.Tab, ModifierKeys.Shift, true)]
        [InlineData(Key.Down, ModifierKeys.None, true)]
        [InlineData(Key.Enter, ModifierKeys.None, true)]
        public void OverlayNavigationDoesNotPermitNotebookEditingKeys(Key key, ModifierKeys modifiers, bool allowed)
            => Assert.Equal(allowed, OverlayFocusScope.IsNavigationKey(key, modifiers));

        [Fact]
        public void InitialSelectionSkipsCategoryHeadersAndHandlesAnEmptyList() => Sta.Run(() =>
        {
            var list = new ListBox();
            list.Items.Add(new ListBoxItem { IsEnabled = false, Focusable = false, Content = "File" });
            var action = new ListBoxItem { Content = "New note" };
            list.Items.Add(action);
            Assert.Same(action, OverlayFocusScope.FirstAction(list));
            Assert.Same(action, list.SelectedItem);
            list.Items.Clear();
            Assert.Null(OverlayFocusScope.FirstAction(list));
        });

        [Fact]
        public void ShortcutPeersAnnounceSectionGestureAndLocalizedDescription() => Sta.Run(() =>
        {
            var item = (ListBoxItem)typeof(MainWindow).GetMethod("CreateShortcutItem",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                new object[] { new Grid(), "File: Ctrl+N: New note" })!;
            var peer = UIElementAutomationPeer.CreatePeerForElement(item)!;
            Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
            Assert.Equal("File: Ctrl+N: New note", peer.GetName());
        });

        private sealed class TestSource : PresentationSource
        {
            public override Visual RootVisual { get; set; } = new DrawingVisual();
            public override bool IsDisposed => false;
            protected override CompositionTarget GetCompositionTargetCore() => null!;
        }

        [Fact]
        public void MainWindowKeyGateStopsBeforeNotebookHandlersWithoutConstructingTheApp() => Sta.Run(() =>
        {
            var window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            var overlay = new Grid();
            var scope = new OverlayFocusScope();
            var root = new Grid(); root.Children.Add(overlay);
            scope.Open(root, overlay, null);
            void Field(string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
            Field("_overlayFocus", scope);
            Field("ShortcutOverlay", overlay);
            Field("AboutOverlay", new Grid());
            Field("ShortcutListHost", new Grid { Visibility = Visibility.Collapsed });
            Field("KsViewListBtn", new Button());
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, new TestSource(), 0, Key.Delete)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            typeof(MainWindow).GetMethod("Shortcuts_PreviewKeyDown", Private)!.Invoke(window, new object[] { window, e });
            Assert.True(e.Handled);
            Assert.Same(overlay, scope.ActiveOverlay);
        });

        [Fact]
        public void OverlayCloseGlyphsHaveLocalizedButtonNames() => Sta.Run(() =>
        {
            var document = XDocument.Load(SourcePath());
            foreach (string handler in new[] { "AboutClose_Click", "ShortcutClose_Click" })
            {
                var source = document.Descendants().Single(e => (string?)e.Attribute("Click") == handler);
                var element = new XElement(source.Name,
                    source.Attribute("AutomationProperties.Name"), new XAttribute("Content", "\uE711"));
                var button = (Button)XamlReader.Parse(element.ToString());
                button.Resources["Str_Sys_Close"] = "Close";
                var peer = new ButtonAutomationPeer(button);
                Assert.Equal("Close", peer.GetName());
                button.Resources["Str_Sys_Close"] = "Fermer";
                Assert.Equal("Fermer", peer.GetName());
            }
        });

        private static FrameworkElement Row(bool heading, bool first = false)
        {
            if (heading) return new TextBlock { Text = "File", FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkMagenta, Margin = new Thickness(0, first ? 0 : 10, 0, 6) };
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = "Ctrl+Shift+F1", FontFamily = new FontFamily("Consolas"),
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkGreen });
            var description = new TextBlock { Text = "A longer localized description that wraps onto several lines",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black };
            Grid.SetColumn(description, 1); row.Children.Add(description);
            return row;
        }

        private static byte[] Pixels(FrameworkElement column, double width, out Size desired)
        {
            column.Measure(new Size(width, double.PositiveInfinity)); desired = column.DesiredSize;
            column.Arrange(new Rect(0, 0, width, 350)); column.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, 350, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(column);
            var pixels = new byte[(int)width * 350 * 4]; bitmap.CopyPixels(pixels, (int)width * 4, 0);
            return pixels;
        }

        [Theory]
        [InlineData(180)]
        [InlineData(300)]
        public void NativeRowsPreservePanelPixelsAndWrappingEvenWhenSelected(int width) => Sta.Run(() =>
        {
            var before = new StackPanel();
            var after = new ListBox { Style = NavigationStyle() };
            for (int i = 0; i < 5; i++)
            {
                bool heading = i == 0 || i == 3;
                before.Children.Add(Row(heading, i == 0));
                after.Items.Add(new ListBoxItem { Content = Row(heading, i == 0), IsEnabled = !heading, Focusable = !heading });
            }
            var baseline = Pixels(before, width, out var oldSize);
            OverlayFocusScope.FirstAction(after);
            var current = Pixels(after, width, out var newSize);
            Assert.Equal(oldSize, newSize);
            Assert.Equal(baseline, current);
        });

        [Fact]
        public void KeyboardFocusShowsTheAccentOutlineWithoutChangingRowGeometry() => Sta.Run(() =>
        {
            var list = new ListBox { Style = NavigationStyle() };
            list.Resources["PrimaryBrush"] = Brushes.Magenta;
            var item = new ListBoxItem { Content = Row(false) };
            list.Items.Add(item);
            var idle = Pixels(list, 300, out var oldSize);
            var focusKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusWithinPropertyKey",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            item.SetValue(focusKey, true);
            var focused = Pixels(list, 300, out var newSize);
            var border = (Border)item.Template.FindName("KeyboardFocus", item);
            Assert.Same(Brushes.Magenta, border.BorderBrush);
            Assert.Equal(oldSize, newSize);
            Assert.False(idle.SequenceEqual(focused));
        });
    }
}

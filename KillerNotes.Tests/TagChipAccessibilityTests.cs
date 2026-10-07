using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using KillerNotes.Models;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class TagChipAccessibilityTests
    {
        private static string MainWindowMarkup()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "KillerNotes.csproj")))
                    return Path.Combine(dir.FullName, "Shell", "MainWindow.xaml");
            throw new InvalidOperationException("Cannot locate application sources");
        }

        private static Button CreateTagButton()
        {
            var source = XDocument.Load(MainWindowMarkup()).Descendants().Single(e =>
                e.Name.LocalName == "Button" && (string?)e.Attribute("Click") == "TagChip_Click");
            var markup = new XElement(source);
            markup.Attribute("Click")!.Remove();
            var button = (Button)XamlReader.Parse(markup.ToString());
            button.Resources["SmallCornerRadius"] = new CornerRadius(3);
            button.Resources["PrimaryBrush"] = Brushes.Teal;
            button.Resources["Str_TT_TagChip"] = "Filter by tag";
            button.DataContext = new TagChip
            {
                Name = "Priority",
                Background = Brushes.Gold,
                Foreground = Brushes.Black
            };
            return button;
        }

        [Fact]
        public void SidebarTagChipExposesItsTagAndCanBeInvoked() => Sta.Run(() =>
        {
            var button = CreateTagButton();
            var host = new Grid();
            host.Children.Add(button);
            host.Measure(new Size(200, 40));
            host.Arrange(new Rect(0, 0, 200, 40));
            host.UpdateLayout();
            button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            BindingOperations.GetBindingExpression(button, AutomationProperties.NameProperty)!.UpdateTarget();
            button.Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));

            var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
            Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
            Assert.Equal("Priority", peer.GetName());
            Assert.Equal("Filter by tag", peer.GetHelpText());
            Assert.True(button.Focusable);
            Assert.True(button.IsTabStop);

            int clicks = 0;
            button.Click += (_, _) => clicks++;
            var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
            invoke.Invoke();
            button.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            Assert.Equal(1, clicks);
        });

        private static byte[] Render(FrameworkElement element, out Size desired)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            desired = element.DesiredSize;
            element.Arrange(new Rect(new Point(), desired));
            element.UpdateLayout();
            int width = (int)Math.Ceiling(desired.Width);
            int height = (int)Math.Ceiling(desired.Height);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }

        [Fact]
        public void TagButtonKeepsLegacyPillGeometryAndFocusAddsOnlyAnOutline() => Sta.Run(() =>
        {
            var legacy = new Border
            {
                Background = Brushes.Gold,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 3, 4, 0),
                Child = new TextBlock
                {
                    Text = "Priority",
                    Foreground = Brushes.Black,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold
                }
            };
            var button = CreateTagButton();
            button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            button.Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));

            var oldPixels = Render(legacy, out var oldSize);
            var idlePixels = Render(button, out var idleSize);
            Assert.Equal(oldSize, idleSize);
            Assert.Equal(oldPixels, idlePixels);

            var focusKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            button.SetValue(focusKey, true);
            var focusedPixels = Render(button, out var focusedSize);
            var ring = (Border)button.Template.FindName("KeyboardFocus", button);
            Assert.Same(Brushes.Teal, ring.BorderBrush);
            Assert.Equal(idleSize, focusedSize);
            Assert.False(idlePixels.SequenceEqual(focusedPixels));
        });
    }
}

using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class AccessibilityNameTests
    {
        private static void Describe(FrameworkElement surface, string label)
        {
            surface.Resources["ActionLabel"] = label;
            var style = new Style(surface.GetType());
            style.Setters.Add(new Setter(AutomationProperties.NameProperty,
                new Binding(nameof(FrameworkElement.ToolTip)) { RelativeSource = RelativeSource.Self }));
            surface.Style = style;
            typeof(MainWindow).GetMethod("DescribeSurface", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { surface, "ActionLabel", "F11" });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GlyphButtonsExposeActionNamesInsteadOfRichTooltipObjects(bool toggle) => Sta.Run(() =>
        {
            ButtonBase button = toggle ? new ToggleButton() : new Button();
            button.Content = "\uE711";
            Describe(button, "Close");
            var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;

            Assert.Equal("Close", peer.GetName());
            Assert.Equal("F11", peer.GetAcceleratorKey());
            Assert.IsType<StackPanel>(button.ToolTip);
        });

        [Fact]
        public void ActionNameFollowsLocalizedResourceChanges() => Sta.Run(() =>
        {
            var button = new Button { Content = "\uE790" };
            Describe(button, "Theme");
            var peer = new ButtonAutomationPeer(button);
            Assert.Equal("Theme", peer.GetName());

            button.Resources["ActionLabel"] = "Tema";

            Assert.Equal("Tema", peer.GetName());
            Assert.Equal("F11", peer.GetAcceleratorKey());
        });
    }
}

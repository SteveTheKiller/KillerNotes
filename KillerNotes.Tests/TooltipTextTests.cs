using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using KillerFamily.Layout;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class TooltipTextTests
    {
        [Fact]
        public void StructuredHoverCardsWrapDescriptionAndShortcutRows() => Sta.Run(() =>
        {
            var description = new TextBlock { Text = new string('x', 200) };
            var shortcut = new TextBlock { Text = "Alt+W" };
            var rows = new StackPanel(); rows.Children.Add(description); rows.Children.Add(shortcut);
            var tip = new ToolTip { Content = rows };
            TooltipTextConverter.SetWrapContent(tip, true);
            Assert.Equal(300, description.MaxWidth);
            Assert.Equal(TextWrapping.Wrap, description.TextWrapping);
            Assert.Equal(300, shortcut.MaxWidth);
        });

        [Theory]
        [InlineData("Hide format bar (F6)", "Hide format bar\nF6")]
        [InlineData("Heading: step through levels (Alt+1, 2, 3; Alt+0 for normal text)", "Heading: step through levels\nAlt+1, 2, 3; Alt+0 for normal text")]
        [InlineData("Text size (scroll to change)", "Text size (scroll to change)")]
        [InlineData("Copy file (Alt+C)", "Copy file\nAlt+C")]
        public void SeparatesShortcutSuffixWithoutChangingOtherParentheses(string input, string expected)
            => Assert.Equal(expected, new TooltipTextConverter().Convert(input, typeof(string), null!, CultureInfo.InvariantCulture));

        // With hints off the shortcut goes, but a label that is itself a key name (the Delete button
        // of a confirm dialog) must stay, or the hover card shows up empty.
        [Theory]
        [InlineData("Delete (Enter)", "Delete")]
        [InlineData("Copy file (Alt+C)", "Copy file")]
        [InlineData("Alt+W", "")]
        public void HiddenHintsNeverEmptyAWordedTooltip(string input, string expected)
            => Assert.Equal(expected, new TooltipTextConverter().Convert(input, typeof(string), false, CultureInfo.InvariantCulture));
    }
}

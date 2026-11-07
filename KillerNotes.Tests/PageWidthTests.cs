using System.Windows;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The page width presets (#31): a cap on the text column that the editor turns into
    /// side padding, so the column stays centered and shrinks with the window.</summary>
    public sealed class PageWidthTests
    {
        private static readonly Thickness Base = new(5, 0, 5, 0);

        [Fact]
        public void FullWidthKeepsTheEditorsOwnPadding()
        {
            Assert.Equal(Base, PageWidth.Padding(PageWidthMode.Full, 1600, Base));
        }

        [Theory]
        [InlineData("Wide", 1600, 250)]
        [InlineData("Medium", 1600, 350)]
        [InlineData("Narrow", 1600, 450)]
        public void APresetCentersItsColumnInsideAWiderViewport(string name, double viewport, double side)
        {
            var mode = (PageWidthMode)System.Enum.Parse(typeof(PageWidthMode), name);

            Thickness padding = PageWidth.Padding(mode, viewport, Base);

            Assert.Equal(side, padding.Left);
            Assert.Equal(side, padding.Right);
            Assert.Equal(Base.Top, padding.Top);
            Assert.Equal(Base.Bottom, padding.Bottom);
        }

        [Fact]
        public void AViewportNarrowerThanThePresetKeepsTheEditorsOwnPadding()
        {
            Assert.Equal(Base, PageWidth.Padding(PageWidthMode.Narrow, 600, Base));
            Assert.Equal(Base, PageWidth.Padding(PageWidthMode.Wide, 1100, Base));
        }

        [Fact]
        public void TheSettingRoundTripsAndAnUnknownValueMeansFull()
        {
            Assert.Equal(PageWidthMode.Medium, PageWidth.Parse(PageWidth.Serialize(PageWidthMode.Medium)));
            Assert.Equal(PageWidthMode.Full, PageWidth.Parse(null));
            Assert.Equal(PageWidthMode.Full, PageWidth.Parse("banana"));
        }

        [Fact]
        public void TheShortcutCyclesFullWideMediumNarrowAndBack()
        {
            Assert.Equal(PageWidthMode.Wide, PageWidth.Next(PageWidthMode.Full));
            Assert.Equal(PageWidthMode.Medium, PageWidth.Next(PageWidthMode.Wide));
            Assert.Equal(PageWidthMode.Narrow, PageWidth.Next(PageWidthMode.Medium));
            Assert.Equal(PageWidthMode.Full, PageWidth.Next(PageWidthMode.Narrow));
        }
    }
}

using System.Windows.Media;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class MenuHoverTextTests
    {
        [Theory]
        [InlineData("#DD504B", "#404040")]   // Dark, red accent
        [InlineData("#22BB56", "#404040")]   // Dark, green accent
        [InlineData("#EAD900", "#EAD900")]   // Ectoplasm: yellow text on its yellow hover
        [InlineData("#1B5E20", "#B2B2B2")]   // Light
        public void HoverTextReadsOnTheHoverFill(string accent, string fill)
        {
            var a = (Color)ColorConverter.ConvertFromString(accent);
            var f = (Color)ColorConverter.ConvertFromString(fill);
            Assert.True(ColorContrast.Contrast(ColorContrast.ReadableOn(a, f, 4.5), f) >= 4.5);
        }

        [Fact]
        public void AnAccentThatAlreadyReadsIsKeptAsIs()
        {
            var a = (Color)ColorConverter.ConvertFromString("#00FF66");
            var f = (Color)ColorConverter.ConvertFromString("#121212");
            Assert.Equal(a, ColorContrast.ReadableOn(a, f, 4.5));
        }
    }
}

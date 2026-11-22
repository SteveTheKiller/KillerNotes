using System.Windows.Media;
using KillerNotes.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Recoloring a tag whose stored color is not valid hex opens the picker on the
    /// default tag blue instead of throwing.</summary>
    public sealed class TagColorFallbackTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("zzz")]
        [InlineData("#GGGGGG")]
        [InlineData(null)]
        public void ABadStoredColorFallsBackToTheDefault(string? hex)
        {
            Assert.Equal(Color.FromRgb(0x50, 0xAE, 0xE8), TagsDialog.PickerStartColor(hex));
        }

        [Fact]
        public void AGoodStoredColorIsKept()
        {
            Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), TagsDialog.PickerStartColor("#123456"));
        }
    }
}

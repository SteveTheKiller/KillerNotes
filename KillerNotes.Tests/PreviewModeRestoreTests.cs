using System.Reflection;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The mode a detected note opens in: the one last picked, so someone who lives in
    /// Rendered does not re-pick it on every note. A blank note is the exception, because it has
    /// nothing to read and the editor is where it gets written.</summary>
    public sealed class PreviewModeRestoreTests
    {
        private static string Opening(string? saved, bool blank = false) =>
            typeof(MainWindow).GetMethod("OpeningPreviewMode", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object?[] { saved, blank })!.ToString()!;

        [Theory]
        [InlineData("Rendered", "Rendered")]
        [InlineData("Split", "Split")]
        [InlineData("Source", "Source")]
        public void ANoteOpensInTheModeLastPicked(string saved, string expected) => Assert.Equal(expected, Opening(saved));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("rendered-ish")]
        [InlineData("7")]
        public void ASettingThatIsNotAModeOpensInSource(string? saved) => Assert.Equal("Source", Opening(saved));

        [Fact]
        public void ABlankNoteOpensInSourceSoThereIsSomewhereToType() =>
            Assert.Equal("Source", Opening("Rendered", blank: true));
    }
}

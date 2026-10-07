using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class SidebarSortTests
    {
        [Theory]
        [InlineData("created-asc", "created", true)]
        [InlineData("created-desc", "created", false)]
        [InlineData("title-asc", "title", true)]
        [InlineData("title-desc", "title", false)]
        [InlineData("custom", "custom", false)]
        public void RestoresSavedModeAndDirection(string saved, string field, bool ascending)
        {
            Assert.Equal((field, ascending), MainWindow.ParseSortPreference(saved));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("title")]
        [InlineData("unknown-asc")]
        public void MissingOrInvalidPreferenceUsesNewestFirst(string? saved)
        {
            Assert.Equal(("created", false), MainWindow.ParseSortPreference(saved));
        }
    }
}

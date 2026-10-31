using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    // The search box parses on every keystroke, so nothing typed there may throw.
    public class SearchQueryDateTests
    {
        [Theory]
        [InlineData("before:9999-12-31")]
        [InlineData("created:9999-12-31")]
        [InlineData("created:9999-12")]
        [InlineData("created:9999")]
        public void TheLastDayOfTheCalendarIsAValidFilter(string text)
        {
            var query = SearchQuery.Parse(text);

            Assert.Empty(query.Words);
        }
    }
}

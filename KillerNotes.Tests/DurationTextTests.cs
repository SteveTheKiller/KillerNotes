using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Recording lengths on the pad, the chip and in Markdown export share one format,
    /// and a take past an hour keeps its hours.</summary>
    public sealed class DurationTextTests
    {
        [Theory]
        [InlineData(0L, "0:00")]
        [InlineData(65_000L, "1:05")]
        [InlineData(3_599_000L, "59:59")]
        [InlineData(3_600_000L, "1:00:00")]
        [InlineData(3_900_000L, "1:05:00")]
        [InlineData(36_005_000L, "10:00:05")]
        public void DurationsKeepTheirHours(long ms, string expected) =>
            Assert.Equal(expected, DurationText.Format(ms));
    }
}

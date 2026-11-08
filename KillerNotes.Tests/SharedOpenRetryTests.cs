using System;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class SharedOpenRetryTests
    {
        [Fact]
        public void CompletedAndInProgressOpensStillSuppressDuplicates()
        {
            var opens = new RecentOpens();
            var now = new DateTime(2026, 11, 8, 10, 0, 0);
            Assert.True(opens.TryOpen("shared.knote", "hash", now, () =>
            {
                Assert.False(opens.TryOpen("shared.knote", "hash", now.AddMinutes(2), () => throw new Exception("duplicate")));
                return true;
            }));
            Assert.False(opens.TryOpen("shared.knote", "hash", now.AddSeconds(1), () => throw new Exception("duplicate")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnUnfinishedOpenCanBeRetriedImmediately(bool throws)
        {
            var opens = new RecentOpens();
            var now = new DateTime(2026, 11, 8, 10, 0, 0);
            if (throws) Assert.Throws<InvalidOperationException>(() =>
                opens.TryOpen("shared.knote", "hash", now, () => throw new InvalidOperationException("read failed")));
            else Assert.True(opens.TryOpen("shared.knote", "hash", now, () => false));
            int attempted = 0;

            Assert.True(opens.TryOpen("shared.knote", "hash", now.AddSeconds(1), () => { attempted++; return true; }));

            Assert.Equal(1, attempted);
        }
    }
}

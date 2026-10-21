using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class NoteSearchTests
    {
        [Fact]
        public void SearchFindsEachWhitespaceWordInDocumentOrder()
        {
            var hits = NoteSearch.FindMatches("Cat dog cat", "dog\tcat");

            Assert.Equal(new[] { (0, 3), (4, 3), (8, 3) }, hits);
        }

        [Fact]
        public void RepeatedAndOverlappingWordsCountEachVisibleSpanOnce()
        {
            var hits = NoteSearch.FindMatches("abc abcd aaaa", "ab bc CD AB aa");

            Assert.Equal(new[] { (0, 3), (4, 4), (9, 2), (11, 2) }, hits);
        }

        [Fact]
        public void WordOccurrenceCountsRemainSeparateFromMergedHighlights()
        {
            Assert.Equal(2, NoteSearch.CountMatches("abc", "ab bc"));
            Assert.Equal(new[] { (0, 3) }, NoteSearch.FindMatches("abc", "ab bc"));
            Assert.Equal(2, NoteSearch.CountMatches("abc", "ab bc AB"));
            Assert.Equal(2, NoteSearch.CountMatches("abab", "ab"));
            Assert.Equal(new[] { (0, 2), (2, 2) }, NoteSearch.FindMatches("abab", "ab"));
        }

        [Fact]
        public void EmbeddedObjectsCannotBecomePartOfAMatch()
        {
            var hits = NoteSearch.FindMatches("ab\uFFFCcd abcd", "ab\uFFFCcd abcd");

            Assert.Equal(new[] { (6, 4) }, hits);
        }

        [Fact]
        public void UnicodeCaseAndWhitespaceUseTheSameWordMatchingRules()
        {
            var hits = NoteSearch.FindMatches("CAF\u00C9 caf\u00E9 \u03A9mega", "caf\u00E9\u00A0\u03C9MEGA CAF\u00C9");

            Assert.Equal(new[] { (0, 4), (5, 4), (10, 5) }, hits);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" \t\r\n\u00A0")]
        public void EmptySearchHasNoMatches(string query)
        {
            Assert.Empty(NoteSearch.FindMatches("cat dog", query));
        }

        [Fact]
        public void RichTextLoadsWithFormattingWithoutChangingTheStoredBytes() => Sta.Run(() =>
        {
            var paragraph = new Paragraph(new Run("Cat "));
            paragraph.Inlines.Add(new Bold(new Run("dog")));
            var source = new FlowDocument(paragraph);
            using var bytes = new MemoryStream();
            new TextRange(source.ContentStart, source.ContentEnd).Save(bytes, DataFormats.XamlPackage);
            byte[] blob = bytes.ToArray();
            byte[] before = (byte[])blob.Clone();

            var loaded = NoteSearch.LoadDocument(blob, 0);

            Assert.Equal("Cat dog\r\n", new TextRange(loaded.ContentStart, loaded.ContentEnd).Text);
            Assert.Equal(FontWeights.Bold, ((Paragraph)loaded.Blocks.FirstBlock).Inlines.LastInline.FontWeight);
            Assert.Equal(before, blob);
            Assert.Equal("Cat dog\r\n", new TextRange(source.ContentStart, source.ContentEnd).Text);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MarkdownLoadsFromPackagesAndLegacyRawBytesWithoutMutation(bool legacy) => Sta.Run(() =>
        {
            const string source = "# Cat\n\ndog";
            byte[] blob = legacy ? Encoding.UTF8.GetBytes(source) : MarkdownBlob.Encode(source);
            byte[] before = (byte[])blob.Clone();

            var loaded = NoteSearch.LoadDocument(blob, 1);

            Assert.Equal("# Cat\r\n\r\ndog\r\n", new TextRange(loaded.ContentStart, loaded.ContentEnd).Text);
            Assert.Equal(before, blob);
        });

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void MissingAndEmptyContentLoadAsEmptyNotes(int format) => Sta.Run(() =>
        {
            foreach (byte[]? blob in new byte[]?[] { null, Array.Empty<byte>() })
            {
                var loaded = NoteSearch.LoadDocument(blob, format);

                Assert.True(string.IsNullOrWhiteSpace(new TextRange(loaded.ContentStart, loaded.ContentEnd).Text));
            }
        });

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void DamagedPackagesReportAFailureInsteadOfCountingRawBytes(int format) => Sta.Run(() =>
        {
            byte[] damaged = { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02 };

            Assert.Throws<ArgumentException>(() => NoteSearch.LoadDocument(damaged, format));
        });
    }
}

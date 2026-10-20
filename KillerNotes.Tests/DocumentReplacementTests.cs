using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public class DocumentReplacementTests
    {
        [Theory]
        [InlineData(0, 4, "Xdef\r\n")]
        [InlineData(3, 4, "abcX\r\n")]
        [InlineData(3, 1, "abcXdef\r\n")]
        public void ReplaceConsumesMatchesWithParagraphSeparatorEndpoints(int start, int length, string expected) => Sta.Run(() =>
        {
            var document = new FlowDocument(new Paragraph(new Run("abc")));
            document.Blocks.Add(new Paragraph(new Run("def")));
            var editor = new RichTextBox(document);
            Assert.Equal(1, DocumentReplacement.Apply(editor, new[] { (start, length, "X") }));
            Assert.Equal(expected, new TextRange(document.ContentStart, document.ContentEnd).Text);
        });

        [Fact]
        public void ReplaceAllResolvesManyFormattedRunsWithoutOffsetDrift() => Sta.Run(() =>
        {
            const int count = 2048;
            var paragraph = new Paragraph();
            var edits = new (int, int, string)[count];
            for (int i = 0; i < count; i++)
            {
                paragraph.Inlines.Add(new Bold(new Run("ab ")));
                edits[i] = (i * 3, 2, "X");
            }
            var editor = new RichTextBox(new FlowDocument(paragraph));
            DocumentReplacement.Apply(editor, edits);
            Assert.Equal(string.Concat(System.Linq.Enumerable.Repeat("X ", count)) + "\r\n",
                new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
        });

        [Theory]
        [InlineData(2, false)]
        [InlineData(3, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void ReplacementCannotConsumeAnEmbeddedObject(int length, bool recording) => Sta.Run(() =>
        {
            var paragraph = new Paragraph(new Run("a"));
            System.Windows.FrameworkElement image = recording ? new Button() : new Image();
            var container = new InlineUIContainer(image);
            paragraph.Inlines.Add(container);
            paragraph.Inlines.Add(new Run("b"));
            var editor = new RichTextBox(new FlowDocument(paragraph));
            Assert.Equal(0, DocumentReplacement.Apply(editor, new[] { (0, length, "X") }));
            Assert.Same(container, image.Parent);
            Assert.Contains(container, System.Linq.Enumerable.Cast<Inline>(paragraph.Inlines));
            Assert.Contains("a", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
            Assert.Contains("b", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
            Assert.Equal(1, DocumentReplacement.Apply(editor, new[] { (2, 1, "B") }));
            Assert.Contains("B", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
        });

        [Theory]
        [InlineData("ab", "(a)(?=b)", 0, 1, "$1$1", "aa")]
        [InlineData("ab", "(?<=a)(b)", 1, 1, "$1$1", "bb")]
        [InlineData("abc", "b", 1, 1, "$`-$&-$'-$_", "a-b-c-abc")]
        [InlineData("ab", "^(a)(?=b)", 0, 1, "$1!", "a!")]
        [InlineData("ab", "(?<=a)(b)$", 1, 1, "$1!", "b!")]
        public void RegexReplacementKeepsTheOriginalInputContext(string input, string pattern,
            int start, int length, string replacement, string expected)
        {
            Assert.Equal(expected, DocumentReplacement.Substitute(
                new System.Text.RegularExpressions.Regex(pattern), input, start, length, replacement));
        }

        [Fact]
        public void ReplaceCanCrossParagraphAndHyperlinkBoundaries() => Sta.Run(() =>
        {
            var doc = new FlowDocument(new Paragraph(new Run("before ab")));
            doc.Blocks.Add(new Paragraph(new Hyperlink(new Run("cd after"))));
            var editor = new RichTextBox(doc);
            DocumentReplacement.Apply(editor, new[] { (7, 5, "X") });
            Assert.Equal("before X after\r\n", new TextRange(doc.ContentStart, doc.ContentEnd).Text);
        });

        [Fact]
        public void ReplaceAcrossFormattingConsumesTheWholeMatchAndUndoesOnce() => Sta.Run(() =>
        {
            var paragraph = new Paragraph(new Run("hel"));
            paragraph.Inlines.Add(new Bold(new Run("lo")));
            paragraph.Inlines.Add(new Span(new Run(" hello")));
            var editor = new RichTextBox(new FlowDocument(paragraph));
            editor.ApplyTemplate();
            editor.Measure(new System.Windows.Size(600, 400));
            editor.Arrange(new System.Windows.Rect(0, 0, 600, 400));
            DocumentReplacement.Apply(editor, new[] { (0, 5, "X"), (6, 5, "Y") });
            Assert.Equal("X Y\r\n", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
            editor.Undo();
            Assert.Equal("hello hello\r\n", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
        });
    }
}

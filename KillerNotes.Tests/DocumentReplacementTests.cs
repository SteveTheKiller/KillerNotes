using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public class DocumentReplacementTests
    {
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

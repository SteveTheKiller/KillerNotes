using System.Linq;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// HtmlConvert reads an HTML note for the preview pane. It is a reader for the structure a note
    /// carries, so these check what the reader makes of tricky markup, not how it is spaced.
    /// </summary>
    public sealed class HtmlConvertTests
    {
        [Fact]
        public void AnHtmlRuleIsARuleToEveryReader() => Sta.Run(() =>
        {
            var doc = HtmlConvert.ToDocument("<p>above</p><hr><p>below</p>", 14);

            string markdown = MarkdownWriter.FromDocument(doc, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kn-rule.md")).Markdown;

            Assert.Contains("---", markdown);
        });

        [Fact]
        public void ALinkTakesItsHrefNotAnAttributeThatEndsInHref() => Sta.Run(() =>
        {
            var doc = HtmlConvert.ToDocument("<a data-href=\"https://other.example/\" href=\"https://shown.example/\">label</a>", 14);

            var link = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()).Single();

            Assert.Equal("shown.example", link.NavigateUri.Host);
        });

        [Fact]
        public void AListStartsAtItsStartNotAtADataStart() => Sta.Run(() =>
        {
            var doc = HtmlConvert.ToDocument("<ol data-start=\"9\" start=\"3\"><li>x</li></ol>", 14);

            Assert.Equal(3, doc.Blocks.OfType<List>().Single().StartIndex);
        });

        [Fact]
        public void ALessThanSignBeforeASpaceIsText() => Sta.Run(() =>
        {
            var doc = HtmlConvert.ToDocument("<p>if x &lt; y and y > 3 then</p><p>or x < y and y > 3</p>", 14);

            string text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;

            Assert.Contains("if x < y and y > 3 then", text);
            Assert.Contains("or x < y and y > 3", text);
        });
    }
}

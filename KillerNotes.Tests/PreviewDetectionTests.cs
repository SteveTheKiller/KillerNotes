using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public class PreviewDetectionTests
    {
        private static string Detect(string text) => typeof(MainWindow)
            .GetMethod("DetectDocKind", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { text })!.ToString()!;

        [Theory]
        [InlineData("# Research Notes\n\n## Glossary\n\n- **API:** Use <tenant>, <clientid>, and <secret>.", "Markdown")]
        [InlineData("# HTML example\n\n```html\n<div><p>Example</p></div>\n```", "Markdown")]
        [InlineData("# HTML example\n\n`<div><p>Example</p></div>`", "Markdown")]
        [InlineData("<DIV><P>**literal text**</P></DIV>", "Html")]
        [InlineData("<tenant> <clientid> <secret>", "None")]
        [InlineData("- First task\n1. Second task", "None")]
        public void ChoosesTheCorrectRenderer(string source, string expected)
            => Assert.Equal(expected, Detect(source));

        [Fact]
        public void TechnicalMarkdownRendersHeadingsAndPreservesPlaceholders() => Sta.Run(() =>
        {
            const string source = "# Research Notes\n\n## Glossary\n\n- **API:** Use <tenant>, <clientid>, and <secret>.";
            Assert.Equal("Markdown", Detect(source));
            var doc = PreviewDocument.Render(source, 13);
            var heading = doc.Blocks.OfType<Paragraph>().First();
            Assert.Equal(FontWeights.Bold, heading.FontWeight);
            Assert.True(heading.FontSize > 13);
            Assert.Single(doc.Blocks.OfType<List>());
            string text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            Assert.Contains("<tenant>, <clientid>, and <secret>", text);
            Assert.DoesNotContain("**", text);
        });

        [Fact]
        public void MarkdownAndHtmlRenderTogether() => Sta.Run(() =>
        {
            var doc = PreviewDocument.Render("# Heading\n\nMarkdown **bold** and <em>HTML italic</em>.\n\n<div><h2>HTML heading</h2><p>HTML body</p></div>\n\n- Markdown list", 13);
            string text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            Assert.Contains("Heading", text);
            Assert.Contains("HTML body", text);
            Assert.Contains("Markdown list", text);
            Assert.DoesNotContain("**", text);
            Assert.DoesNotContain("<em>", text);
            Assert.Single(doc.Blocks.OfType<List>());
            Assert.True(doc.Blocks.OfType<Paragraph>().Count(p => p.FontSize > 13) >= 2);
            Assert.Contains(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()), r => r.FontStyle == FontStyles.Italic);
        });

        [Theory]
        [InlineData("```html\n<div><p>Example</p></div>\n```", "<div><p>Example</p></div>")]
        [InlineData("~~~html\n<div><p>Example</p></div>\n~~~", "<div><p>Example</p></div>")]
        [InlineData("`<tenant>`", "<tenant>")]
        public void CodeExamplesRemainLiteral(string source, string expected) => Sta.Run(() =>
        {
            var doc = PreviewDocument.Render(source, 13);
            Assert.Contains(expected, new TextRange(doc.ContentStart, doc.ContentEnd).Text);
        });

        [Fact]
        public void MixedPreviewSkipsScriptsAndUnsafeLinks() => Sta.Run(() =>
        {
            var doc = PreviewDocument.Render("# Safe\n\n<script>alert('bad')</script>\n\n<a href=\"javascript:alert(1)\">label</a>", 13);
            string text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            Assert.DoesNotContain("alert", text);
            Assert.Contains("label", text);
            Assert.Empty(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()));
        });
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Rich text copied from the Claude desktop app (and other sources that put only CF_HTML
    /// and plain text on the clipboard) goes through KillerNotes' own HTML paste whenever it
    /// carries a link. That converter used to keep only paragraphs, line breaks, links and
    /// bold/italic/underline, so headings, lists and code blocks arrived flat. The paste runs
    /// into a RichTextBox that is never shown, so nothing appears on screen.
    /// </summary>
    public sealed class HtmlPasteStructureTests
    {
        private const string Fragment =
            "<h2>Release notes</h2>" +
            "<p>Read the <a href=\"https://example.com/notes\">full notes</a> and <strong>back up</strong> first.</p>" +
            "<ul><li>First item</li><li>Second item</li></ul>" +
            "<pre><code>line one\nline two\n  line three\n</code></pre>" +
            "<p>Done.</p>";

        private static string CfHtml(string fragment) =>
            "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\n" +
            "StartFragment:0000000000\r\nEndFragment:0000000000\r\n" +
            "<html><body><!--StartFragment-->" + fragment + "<!--EndFragment--></body></html>";

        [Fact]
        public void PastedHtmlKeepsHeadingsListsCodeBoldAndLinks()
        {
            RunSta(() =>
            {
                var box = new RichTextBox { FontSize = Headings.DefaultBase };
                box.Document.Blocks.Clear();
                box.Document.Blocks.Add(new Paragraph());
                box.CaretPosition = box.Document.ContentStart;

                Assert.True(MainWindow.PasteHtmlInto(box, CfHtml(Fragment)));

                var doc = box.Document;
                var paragraphs = Walk(doc.Blocks).OfType<Paragraph>().ToList();

                var heading = paragraphs.Single(p => Text(p) == "Release notes");
                Assert.Equal(2, Headings.LevelOf(heading, Headings.DefaultBase));

                var list = Walk(doc.Blocks).OfType<List>().Single();
                Assert.Equal(TextMarkerStyle.Disc, list.MarkerStyle);
                Assert.Equal(new[] { "First item", "Second item" },
                    list.ListItems.Select(li => string.Concat(((Paragraph)li.Blocks.FirstBlock).Inlines.OfType<Run>().Select(r => r.Text))).ToArray());

                var code = paragraphs.Single(p => Text(p).Contains("line one"));
                Assert.Equal(2, code.Inlines.OfType<LineBreak>().Count());
                Assert.Equal("line one\r\nline two\r\n  line three", Text(code).TrimEnd());
                Assert.Contains("Consolas", new TextRange(code.ContentStart, code.ContentEnd)
                    .GetPropertyValue(TextElement.FontFamilyProperty)?.ToString() ?? "");

                var bold = Walk(doc.Blocks).OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>()
                    .Single(r => r.Text.Contains("back up"));
                Assert.Equal(FontWeights.Bold, bold.FontWeight);

                var link = paragraphs.SelectMany(p => p.Inlines).OfType<Hyperlink>().Single();
                Assert.Equal(new Uri("https://example.com/notes"), link.NavigateUri);
                Assert.Equal("full notes", new TextRange(link.ContentStart, link.ContentEnd).Text);

                // Blocks from the source are a blank line apart, the way a note is typed.
                int h = doc.Blocks.ToList().IndexOf(heading);
                Assert.True(doc.Blocks.ElementAt(h + 1) is Paragraph gap && Text(gap).Length == 0);
            });
        }

        [Fact]
        public void PasteWithoutLinksIsLeftToTheNativePath()
        {
            RunSta(() =>
            {
                var box = new RichTextBox();
                Assert.False(MainWindow.PasteHtmlInto(box, CfHtml("<h2>No links</h2><ul><li>x</li></ul>")));
            });
        }

        private static string Text(Paragraph p) => new TextRange(p.ContentStart, p.ContentEnd).Text;

        private static System.Collections.Generic.IEnumerable<Block> Walk(BlockCollection blocks)
        {
            foreach (var b in blocks)
            {
                yield return b;
                var inner = b switch
                {
                    Section s => Walk(s.Blocks),
                    List l => l.ListItems.SelectMany(li => Walk(li.Blocks)),
                    _ => Enumerable.Empty<Block>(),
                };
                foreach (var i in inner) yield return i;
            }
        }

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(30)), "HTML paste test timed out");
            if (failure != null) throw failure;
        }
    }
}

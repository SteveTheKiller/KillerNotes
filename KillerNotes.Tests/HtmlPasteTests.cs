using System;
using System.Linq;
using System.Threading;
using System.Windows.Documents;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The CF_HTML paste converter runs synchronously on the UI thread, so every malformed
    /// fragment must finish promptly. A script or style close tag missing its final '>' used to
    /// reset the scan to the start of the input and loop forever.
    /// </summary>
    public sealed class HtmlPasteTests
    {
        [Theory]
        [InlineData("<a href=\"https://example.com\">x</a><script>bad()</script")]
        [InlineData("<style>p{}</style")]
        [InlineData("text<script>")]
        [InlineData("<script></")]
        [InlineData("<!-- open comment")]
        [InlineData("<b><i><a href='https://a'>a")]
        [InlineData("<")]
        [InlineData("")]
        public void MalformedFragmentsFinishPromptly(string html)
        {
            ConvertWithin(html, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void TextAroundAnUnterminatedScriptCloseIsKept()
        {
            var (links, text) = ConvertWithin("<a href=\"https://example.com\">site</a> before<script>x</script", TimeSpan.FromSeconds(3));
            Assert.Equal(1, links);
            Assert.Contains("before", text);
            Assert.DoesNotContain("x", text.Replace("before", ""));
        }

        /// <summary>Converts on an STA thread with a deadline and summarizes there, since the
        /// inlines belong to that thread: the hyperlink count and the plain run text.</summary>
        private static (int Links, string Text) ConvertWithin(string html, TimeSpan limit)
        {
            (int, string) result = default;
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    var inlines = MainWindow.HtmlFragmentToInlines(html);
                    result = (inlines.OfType<Hyperlink>().Count(), string.Concat(inlines.OfType<Run>().Select(r => r.Text)));
                }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(limit), $"HTML paste conversion did not finish within {limit.TotalSeconds:0}s for: {html}");
            if (failure != null) throw failure;
            return result;
        }
    }
}

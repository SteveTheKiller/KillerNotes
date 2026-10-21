using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Stored hyperlinks reach HTML through Export note (standalone page) and rich copy (CF_HTML).
    /// A shared or imported note can carry any URI, so both sinks must emit exactly the anchor's
    /// own attributes and only the schemes the app itself will open; anything else keeps its
    /// label as plain text.
    /// </summary>
    public sealed class HtmlLinkOutputTests
    {
        [Theory]
        [InlineData("x\" data-review=\"1")]
        [InlineData("x' onclick='1")]
        [InlineData("a<b>&c")]
        [InlineData("&quot; data-review=&quot;1")]
        public void QuotedRelativeLinksNeverAddAttributes(string target) => Sta.Run(() =>
        {
            foreach (string html in Outputs(Link(new Uri(target, UriKind.Relative), "label")))
            {
                Assert.DoesNotContain("data-review", AnchorTags(html));
                Assert.DoesNotContain("onclick", AnchorTags(html));
                Assert.Contains("label", html);
            }
        });

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("JavaScript:alert(1)")]
        [InlineData("file:///C:/Windows/notepad.exe")]
        [InlineData("data:text/html,hi")]
        [InlineData("vbscript:x")]
        [InlineData("customapp://do-something")]
        public void UnsupportedSchemesKeepOnlyTheLabel(string target) => Sta.Run(() =>
        {
            foreach (string html in Outputs(Link(new Uri(target, UriKind.Absolute), "label")))
            {
                Assert.Empty(Anchors(html));
                Assert.Contains("label", html);
            }
        });

        [Theory]
        [InlineData("https://example.com/a?b=1&c=2", "https://example.com/a?b=1&amp;c=2")]
        [InlineData("HTTP://Example.com/x\"y", "http://example.com/x%22y")]
        [InlineData("mailto:someone@example.com", "mailto:someone@example.com")]
        public void AllowedLinksKeepExactlyHrefTargetAndRel(string target, string expectedHref) => Sta.Run(() =>
        {
            foreach (string html in Outputs(Link(new Uri(target, UriKind.Absolute), "label")))
            {
                var anchor = Assert.Single(Anchors(html));
                var names = Regex.Matches(anchor, "\\s([A-Za-z-]+)\\s*=").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
                Assert.Equal(new[] { "href", "target", "rel" }, names);
                Assert.Contains("href=\"" + expectedHref + "\"", anchor);
            }
        });

        /// <summary>A one-link note after the XamlPackage save/load every stored note goes through.</summary>
        private static FlowDocument Link(Uri uri, string label)
        {
            var doc = new FlowDocument(new Paragraph(new Hyperlink(new Run(label)) { NavigateUri = uri }));
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            var loaded = new FlowDocument();
            ms.Position = 0;
            new TextRange(loaded.ContentStart, loaded.ContentEnd).Load(ms, DataFormats.XamlPackage);
            return loaded;
        }

        private static string[] Outputs(FlowDocument doc) => new[]
        {
            HtmlExport.FromDocument(doc, "t", "#000", "#fff", "#f00", "#333"),
            HtmlExport.ClipboardEnvelope(HtmlExport.FragmentFromDocument(doc)),
        };

        private static string[] Anchors(string html) =>
            Regex.Matches(html, "<a\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Value).ToArray();

        private static string AnchorTags(string html) => string.Join("\n", Anchors(html));
    }
}

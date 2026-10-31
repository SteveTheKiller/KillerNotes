using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The syntax tokenizer on its own (Shell/SyntaxHighlighting.cs). It is a static method of the
    /// main window that never touches a window instance, so these drive it directly and open
    /// nothing.
    /// </summary>
    public sealed class SyntaxTokenizerTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly Type Window = typeof(MainWindow);

        private static List<(int Start, int Length, Color Color)> Tokens(string text, string language) =>
            (List<(int Start, int Length, Color Color)>)Window.GetMethod("CollectSyntaxTokens", Hidden)!.Invoke(null, new object[]
            {
                text,
                Enum.Parse(Window.GetNestedType("CodeLanguage", BindingFlags.NonPublic)!, language),
                false
            })!;

        private static Color Palette(string name) => (Color)Window.GetProperty(name, Hidden)!.GetValue(null)!;

        private static (int Start, int Length)[] Spans(List<(int Start, int Length, Color Color)> tokens, Color color) =>
            tokens.Where(t => t.Color == color).Select(t => (t.Start, t.Length)).OrderBy(t => t.Start).ToArray();

        [Theory]
        [InlineData("Html")]
        [InlineData("Xaml")]
        [InlineData("Xml")]
        [InlineData("Vue")]
        [InlineData("Markdown")]
        public void AnApostropheBeforeACommentInMarkupDoesNotSwallowIt(string language)
        {
            const string text = "<p>it's</p> <!-- note --> <p>that's</p>";
            int comment = text.IndexOf("<!--", StringComparison.Ordinal);

            var tokens = Tokens(text, language);

            Assert.Equal(new[] { (comment, "<!-- note -->".Length) }, Spans(tokens, Palette("SynComment")));
            Assert.DoesNotContain(tokens, t => t.Color == Palette("SynString") && t.Start < comment + "<!-- note -->".Length && comment < t.Start + t.Length);
        }

        [Fact]
        public void AQuotedStringInMarkupStillReadsAsAString()
        {
            const string text = "<a href=\"x\"> <!-- note -->";

            var tokens = Tokens(text, "Html");

            Assert.Equal(new[] { (text.IndexOf('"'), 3) }, Spans(tokens, Palette("SynString")));
        }

        [Fact]
        public void ASlashPairInsideAStringInCodeStaysPartOfTheString()
        {
            const string text = "var s = \"a // b\"; // real";

            var tokens = Tokens(text, "CSharp");

            Assert.Equal(new[] { (text.IndexOf('"'), "\"a // b\"".Length) }, Spans(tokens, Palette("SynString")));
            Assert.Equal(new[] { (text.LastIndexOf("//", StringComparison.Ordinal), "// real".Length) }, Spans(tokens, Palette("SynComment")));
        }

        [Fact]
        public void AQuoteInsideACommentInCodeStaysPartOfTheComment()
        {
            const string text = "x = 1; // it's fine";

            var tokens = Tokens(text, "JavaScript");

            Assert.Equal(new[] { (text.IndexOf("//", StringComparison.Ordinal), "// it's fine".Length) }, Spans(tokens, Palette("SynComment")));
            Assert.Empty(Spans(tokens, Palette("SynString")));
        }
    }
}

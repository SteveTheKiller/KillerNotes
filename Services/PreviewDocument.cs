using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using Markdig;

namespace KillerNotes.Services
{
    internal static class PreviewDocument
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions().Build();

        private const string Tags = "html|head|body|title|div|p|span|h[1-6]|ul|ol|li|table|thead|tbody|tfoot|tr|th|td|a|b|strong|i|em|u|s|del|strike|ins|cite|var|kbd|samp|tt|blockquote|pre|code|br|hr|img|input|section|article|header|footer|main|nav|aside|figure|figcaption|dl|dt|dd|details|summary|style|script|iframe|object|embed|svg|math|button|textarea|select";
        private static readonly Regex HtmlTag = new(@"^</?(?:" + Tags + @")(?=\s|/?>)", RegexOptions.IgnoreCase);
        private static readonly Regex Tokens = new(
            @"(?m)^ {0,3}(?<fence>`{3,}|~{3,})[^\r\n]*\r?\n[\s\S]*?^ {0,3}\k<fence>[^\r\n]*$|(?<ticks>`+)[^`]*(?:`(?!\k<ticks>)[^`]*)*\k<ticks>|</?[a-zA-Z][a-zA-Z0-9]*(?:\s[^<>]*)?/?>");

        public static bool HasHtml(string source)
        {
            foreach (Match token in Tokens.Matches(source))
                if (HtmlTag.IsMatch(token.Value)) return true;
            return false;
        }

        public static FlowDocument Render(string source, double fontSize)
        {
            // Preserve technical placeholders while leaving code examples untouched.
            string prepared = Tokens.Replace(source, token => token.Value.StartsWith("<") &&
                !HtmlTag.IsMatch(token.Value) ? WebUtility.HtmlEncode(token.Value) : token.Value);
            return HtmlConvert.ToDocument(Markdown.ToHtml(prepared, Pipeline), fontSize);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using KillerNotes.Models;

namespace KillerNotes.Services
{
    internal static class BodyTags
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation().Build();
        private const string Name = @"[\p{L}\p{N}_][\p{L}\p{N}_.-]*";
        private static readonly Regex Group = new(
            @"(?<!\[)\[\s*#" + Name + @"(?:\s*,\s*#" + Name + @")*\s*\](?![\](])",
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
        private static readonly Regex Tag = new("#(" + Name + ")", RegexOptions.Compiled);
        private static readonly Regex PartialGroup = new(
            @"^\[[ \t]*(?:#" + Name + @"[ \t]*,[ \t]*)*#(?<query>[\p{L}\p{N}_.-]*)$",
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

        internal static List<(int Start, int Length, string Name)> Spans(string? text)
        {
            var result = new List<(int, int, string)>();
            if (string.IsNullOrEmpty(text)) return result;
            try
            {
                var groups = Group.Matches(text);
                if (groups.Count == 0) return result;
                var code = CodeSpans(text);
                foreach (Match group in groups)
                {
                    int escapes = 0;
                    for (int i = group.Index - 1; i >= 0 && text[i] == '\\'; i--) escapes++;
                    if (escapes % 2 != 0 || code.Any(s => group.Index <= s.End && group.Index + group.Length > s.Start))
                        continue;
                    foreach (Match tag in Tag.Matches(group.Value))
                        result.Add((group.Index + tag.Index, tag.Length, tag.Groups[1].Value));
                }
            }
            catch (RegexMatchTimeoutException) { }
            return result;
        }

        private static List<SourceSpan> CodeSpans(string text) =>
            Markdown.Parse(text, Pipeline).Descendants().Where(n => n is CodeBlock || n is CodeInline)
                .Select(n => new SourceSpan(n.Span.Start, CodeEnd(n, text))).ToList();

        internal static string? CompletionQuery(string text, int offset)
        {
            if (offset < 0 || offset > text.Length) return null;
            if (offset < text.Length && Regex.IsMatch(text[offset].ToString(), @"[\p{L}\p{N}_.-]")) return null;
            int open = offset == 0 ? -1 : text.LastIndexOf('[', offset - 1);
            if (open < 0 || (open > 0 && text[open - 1] == '[')) return null;
            int escapes = 0;
            for (int i = open - 1; i >= 0 && text[i] == '\\'; i--) escapes++;
            if (escapes % 2 != 0) return null;
            try
            {
                var match = PartialGroup.Match(text.Substring(open, offset - open));
                if (!match.Success || CodeSpans(text).Any(s => open <= s.End && offset > s.Start)) return null;
                return match.Groups["query"].Value;
            }
            catch (RegexMatchTimeoutException) { return null; }
        }

        internal static string[] Suggestions(IEnumerable<string> tags, string query) =>
            tags.Where(t => Regex.IsMatch(t, "^" + Name + "$") && t.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();

        private static int CodeEnd(MarkdownObject node, string text)
        {
            if (node is not CodeBlock block || block.Lines.Count == 0) return node.Span.End;
            // An unfinished fence may end before its final content line in the syntax tree.
            int position = block.Lines.Lines[block.Lines.Count - 1].Position;
            int end = text.IndexOf('\n', position);
            return Math.Max(node.Span.End, end < 0 ? text.Length - 1 : end);
        }

        internal static string[] Parse(string? text) =>
            Spans(text).Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        internal static string? At(string text, int offset) =>
            Spans(text).Where(s => offset >= s.Start && offset < s.Start + s.Length)
                .Select(s => s.Name).FirstOrDefault();

        internal static string Merge(string manual, string body) =>
            string.Join(", ", NoteStore.SplitTags(manual).Concat(NoteStore.SplitTags(body))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        internal static List<Note> RecentNotes(IEnumerable<Note> notes, string tag, long currentId) =>
            notes.Where(n => n.Id != currentId && !n.IsDeleted && TagManager.HasTag(n, tag))
                .OrderByDescending(n => n.Modified).ThenByDescending(n => n.Id).Take(5).ToList();
    }
}

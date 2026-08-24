// ═══════════════════════════════════════════════════════════
//  WIKILINKS  -  [[Note title]] between notes
// ═══════════════════════════════════════════════════════════
//
// The foundation the backlinks panel and the graph both read. A note is not a second brain
// because it is markdown; it is one because notes point at each other, and this is the pointing.
//
// PARSED FROM THE STORED PLAIN TEXT, never from the document. The plain projection already exists
// at every save, it is identical for rich and markdown notes, and it means a link works the same
// whether it was typed into rich text, pasted, imported from a vault folder, or written by
// another editor into a .md file that was imported back. Nothing has to be turned into a
// Hyperlink element for a link to count, so the stored bytes are untouched by this feature.
//
// Resolution is BY TITLE, case-insensitively, and is deliberately late: a link to a note that
// does not exist yet is kept as an unresolved edge rather than dropped. That is what makes
// "write the link first, create the note later" work, and it is what feeds an unlinked-mentions
// view later. The graph draws unresolved targets differently rather than hiding them.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KillerNotes.Services
{
    internal static class WikiLinks
    {
        // [[target]] or [[target|label]]. The label half is accepted so a note written elsewhere
        // reads correctly here, but only the TARGET is indexed.
        //
        // No newlines inside a link: an unclosed "[[" at the end of a line would otherwise swallow
        // the rest of the note looking for its partner. Non-greedy for the same reason, so
        // "[[a]] and [[b]]" is two links rather than one enormous one.
        private static readonly Regex LinkRx = new(
            @"\[\[([^\[\]\r\n|]{1,200})(?:\|[^\[\]\r\n]{0,200})?\]\]",
            RegexOptions.Compiled);

        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

        /// <summary>Every distinct target in the text, trimmed, in first-appearance order.
        /// Duplicates collapse: a note linking the same target five times is ONE edge, because
        /// the graph and the backlinks list both count notes, not mentions.</summary>
        public static List<string> Parse(string? text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Match m in Regex.Matches(text, LinkRx.ToString(), RegexOptions.None, Timeout))
                {
                    string t = m.Groups[1].Value.Trim();
                    if (t.Length == 0) continue;
                    if (seen.Add(t)) found.Add(t);
                }
            }
            catch (RegexMatchTimeoutException) { /* a pathological note simply has no links */ }
            return found;
        }

        /// <summary>Whether the caret offset sits inside a link, and what it points at. Used by
        /// the editor to turn a click into a jump without the document carrying any link markup.
        /// Returns null when the offset is ordinary text.</summary>
        public static string? TargetAt(string? text, int offset)
        {
            if (string.IsNullOrEmpty(text) || offset < 0 || offset > text!.Length) return null;
            try
            {
                foreach (Match m in Regex.Matches(text, LinkRx.ToString(), RegexOptions.None, Timeout))
                    if (offset >= m.Index && offset <= m.Index + m.Length)
                    {
                        string t = m.Groups[1].Value.Trim();
                        return t.Length == 0 ? null : t;
                    }
            }
            catch (RegexMatchTimeoutException) { }
            return null;
        }

        /// <summary>The spans of every link in the text, for painting them as links without
        /// writing anything into the document - the same "colour is a view" rule the syntax
        /// highlighter follows.</summary>
        public static List<(int Start, int Length, string Target)> Spans(string? text)
        {
            var spans = new List<(int, int, string)>();
            if (string.IsNullOrEmpty(text)) return spans;
            try
            {
                foreach (Match m in Regex.Matches(text, LinkRx.ToString(), RegexOptions.None, Timeout))
                {
                    string t = m.Groups[1].Value.Trim();
                    if (t.Length > 0) spans.Add((m.Index, m.Length, t));
                }
            }
            catch (RegexMatchTimeoutException) { }
            return spans;
        }

        /// <summary>Wraps a title as a link, for the autocomplete and for "link to this note".</summary>
        public static string Wrap(string title) => "[[" + (title ?? "").Trim() + "]]";
    }
}

// ═══════════════════════════════════════════════════════════
//  TEMPLATE TEXT  -  the placeholders a template can carry
// ═══════════════════════════════════════════════════════════
//
// A template is an ordinary note in the templates group (Shell/Templates.cs). When a new note
// is made from it, these tokens are filled in wherever they appear in its title or body:
//
//   {date}      2026-09-01            {weekday}   Tuesday (in the interface language)
//   {time}      14:05                 {year}      2026
//   {datetime}  2026-09-01 14:05      {month}     09
//                                     {day}       01
//
// Case does not matter. Anything else in braces is left exactly as typed, so a template that
// holds a code snippet with braces in it is not rewritten. In a rich-text template a token
// is only recognized inside one run: a placeholder with a formatting change in the middle of
// it stays as it is, which is the honest outcome for "{da**te**}".

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    internal static class TemplateText
    {
        private static readonly Regex Token = new(@"\{(date|time|datetime|weekday|year|month|day)\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool HasPlaceholders(string text) => text.Length > 0 && Token.IsMatch(text);

        /// <summary>Fills every recognized token in the text with a value from `now`.</summary>
        public static string Expand(string text, DateTime now)
        {
            if (text.IndexOf('{') < 0) return text;
            var inv = CultureInfo.InvariantCulture;
            return Token.Replace(text, m => m.Groups[1].Value.ToLowerInvariant() switch
            {
                "date"     => now.ToString("yyyy-MM-dd", inv),
                "time"     => now.ToString("HH:mm", inv),
                "datetime" => now.ToString("yyyy-MM-dd HH:mm", inv),
                "weekday"  => CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(now.DayOfWeek),
                "year"     => now.ToString("yyyy", inv),
                "month"    => now.ToString("MM", inv),
                "day"      => now.ToString("dd", inv),
                _          => m.Value,
            });
        }

        /// <summary>Fills the tokens in every run of a document, in place.</summary>
        public static void ExpandDocument(FlowDocument doc, DateTime now)
        {
            foreach (var block in doc.Blocks.ToList()) ExpandBlock(block, now);
        }

        // Every collection is snapshotted (ToList) before the walk: assigning a Run's Text bumps
        // the document's version and invalidates every live enumerator over it, blocks included,
        // as every other document walker in the app has found.
        private static void ExpandBlock(Block block, DateTime now)
        {
            switch (block)
            {
                case Paragraph p:
                    foreach (var inline in p.Inlines.ToList()) ExpandInline(inline, now);
                    break;
                case List list:
                    foreach (var li in list.ListItems.ToList())
                        foreach (var b in li.Blocks.ToList()) ExpandBlock(b, now);
                    break;
                case Section s:
                    foreach (var b in s.Blocks.ToList()) ExpandBlock(b, now);
                    break;
                case Table t:
                    foreach (var g in t.RowGroups.ToList())
                        foreach (var row in g.Rows.ToList())
                            foreach (var cell in row.Cells.ToList())
                                foreach (var b in cell.Blocks.ToList()) ExpandBlock(b, now);
                    break;
            }
        }

        private static void ExpandInline(Inline inline, DateTime now)
        {
            switch (inline)
            {
                case Run r:
                    if (r.Text.IndexOf('{') >= 0) r.Text = Expand(r.Text, now);
                    break;
                case Span sp:   // Bold, Italic, Underline and Hyperlink are all spans
                    foreach (var i in sp.Inlines.ToList()) ExpandInline(i, now);
                    break;
            }
        }
    }
}

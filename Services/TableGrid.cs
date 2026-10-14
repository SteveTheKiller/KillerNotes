// ═══════════════════════════════════════════════════════════
//  TABLE GRID  -  clipboard data read as rows and cells
// ═══════════════════════════════════════════════════════════
//
// The editor's table paste (Shell/Editor.TableEdit.cs) works from a plain grid of cell strings,
// whatever the source was:
//
//   HTML   A copied table from a browser, Excel or Word. Excel's fragment starts AFTER its
//          <table> tag, so a fragment of bare <tr> rows counts as a table too. colspan cells
//          are padded with empty cells so the columns stay lined up.
//   Text   Tab separated (Excel's plain text), CSV with quoted fields, or columns lined up
//          with spaces. Single spaces only split when there are several lines, so a sentence
//          pasted into one cell stays a sentence.
//
// Every row comes back padded to the same width. A result of one cell is reported as no grid,
// so the ordinary paste handles it.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace KillerNotes.Services
{
    internal static class TableGrid
    {
        private const char Break = '\u0001';   // an explicit br or block break inside a cell

        private static readonly Regex Token = new(
            @"<!--.*?-->|<(?<close>/)?(?<tag>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>[^>]*)>|(?<text>[^<]+)|<",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex ColSpan = new(
            @"colspan\s*=\s*[""']?(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Skipped = new(
            @"<(style|script|head|title)\b.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>The first table in an HTML fragment as rows of cell text, or null when the
        /// fragment has no table. With <paramref name="tableOnly"/> set, a fragment that also
        /// carries text outside the table returns null, so a copied document that merely
        /// contains a table keeps the native paste with its paragraphs.</summary>
        public static List<string[]>? FromHtml(string html, bool tableOnly)
        {
            if (string.IsNullOrEmpty(html)) return null;
            html = Skipped.Replace(html, "");
            int start = IndexOfTag(html, "table");
            if (start < 0) start = IndexOfTag(html, "tr");
            if (start < 0) return null;

            if (tableOnly)
            {
                int end = Math.Max(LastEndOf(html, "table"), LastEndOf(html, "tr"));
                if (end < start) return null;
                string outside = html[..start] + " " + html[end..];
                if (!string.IsNullOrWhiteSpace(Decode(StripTags(outside)))) return null;
            }

            var rows = new List<List<string>>();
            List<string>? row = null;
            StringBuilder? cell = null;
            int depth = 0;          // table nesting; only the outer table's rows and cells count
            bool outerSeen = false; // Excel's fragment has no <table> tag at all
            bool finished = false;
            int span = 1;

            void CloseCell()
            {
                if (cell == null || row == null) return;
                row.Add(Clean(cell.ToString()));
                for (int i = 1; i < span; i++) row.Add("");
                cell = null;
                span = 1;
            }
            void CloseRow()
            {
                CloseCell();
                if (row != null && row.Count > 0) rows.Add(row);
                row = null;
            }

            foreach (Match m in Token.Matches(html[start..]))
            {
                if (m.Groups["text"].Success) { cell?.Append(m.Groups["text"].Value); continue; }
                if (!m.Groups["tag"].Success) continue;
                string tag = m.Groups["tag"].Value.ToLowerInvariant();
                bool close = m.Groups["close"].Success;
                bool outer = depth <= 1;

                switch (tag)
                {
                    case "table":
                        if (close)
                        {
                            if (depth <= 1) { CloseRow(); finished = outerSeen; }
                            depth = Math.Max(0, depth - 1);
                        }
                        else
                        {
                            depth++;
                            if (depth == 1) outerSeen = true;
                        }
                        break;
                    case "tr" when outer:
                        CloseRow();
                        if (!close) row = [];
                        break;
                    case "td" when outer:
                    case "th" when outer:
                        CloseCell();
                        if (!close)
                        {
                            row ??= [];
                            cell = new StringBuilder();
                            Match sp = ColSpan.Match(m.Groups["attrs"].Value);
                            span = sp.Success ? Math.Max(1, Math.Min(50, int.Parse(sp.Groups["n"].Value))) : 1;
                        }
                        break;
                    case "br":
                        cell?.Append(Break);
                        break;
                    case "p": case "div": case "li":
                    case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                        if (close) cell?.Append(Break);
                        break;
                    default:
                        // A nested table's cells become text inside the outer cell.
                        if (!outer && cell != null && (tag == "td" || tag == "th") && !close) cell.Append(' ');
                        if (!outer && cell != null && tag == "tr" && !close) cell.Append(Break);
                        break;
                }
                if (finished) break;
            }
            CloseRow();
            return Finish([.. rows.Select(r => r.ToArray())]);
        }

        /// <summary>Plain clipboard text as rows of cells: tab separated, CSV, or columns
        /// separated by spaces. Null when the text is a single cell's worth.</summary>
        public static List<string[]>? FromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = text.Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0) return null;

            if (lines.Any(l => l.IndexOf('\t') >= 0))
                return Finish([.. lines.Select(l => l.Split('\t').Select(c => c.Trim()).ToArray())]);

            if (lines.All(l => l.IndexOf(',') >= 0))
                return Finish(ParseCsv(text));

            bool wide = lines.Any(l => Regex.IsMatch(l.Trim(), @"\s{2,}"));
            if (!wide && lines.Count == 1) return null;
            var split = new Regex(wide ? @"\s{2,}" : @"\s+");
            return Finish([.. lines.Select(l => split.Split(l.Trim()))]);
        }

        /// <summary>RFC 4180 style: quoted fields may hold commas, doubled quotes and line
        /// breaks. Unquoted fields are trimmed so "a, b" reads as two clean cells.</summary>
        internal static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, wasQuoted = false;

            void EndField()
            {
                row.Add(wasQuoted ? field.ToString() : field.ToString().Trim());
                field.Clear();
                wasQuoted = false;
            }
            void EndRow()
            {
                EndField();
                if (row.Count > 1 || row[0].Length > 0) rows.Add([.. row]);
                row = [];
            }

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else quoted = false;
                    }
                    else field.Append(ch);
                }
                else if (ch == '"' && field.ToString().Trim().Length == 0)
                {
                    field.Clear();
                    quoted = wasQuoted = true;
                }
                else if (ch == ',') EndField();
                else if (ch == '\n') EndRow();
                else field.Append(ch);
            }
            EndRow();
            return rows;
        }

        private static List<string[]>? Finish(List<string[]> rows)
        {
            rows = [.. rows.Where(r => r.Length > 0)];
            if (rows.Count == 0) return null;
            int cols = rows.Max(r => r.Length);
            if (cols == 0 || (rows.Count == 1 && cols == 1)) return null;
            return [.. rows.Select(r => r.Length == cols ? r : [.. r.Concat(Enumerable.Repeat("", cols - r.Length))])];
        }

        private static int IndexOfTag(string html, string tag)
        {
            Match m = Regex.Match(html, "<" + tag + @"\b", RegexOptions.IgnoreCase);
            return m.Success ? m.Index : -1;
        }

        private static int LastEndOf(string html, string tag)
        {
            int i = html.LastIndexOf("</" + tag, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return -1;
            int gt = html.IndexOf('>', i);
            return gt < 0 ? html.Length : gt + 1;
        }

        private static string StripTags(string html) => Token.Replace(html, m => m.Groups["text"].Success ? m.Value : " ");

        private static string Decode(string s) => WebUtility.HtmlDecode(s).Replace(' ', ' ');

        /// <summary>Cell text: entities decoded, source whitespace (line breaks in the HTML
        /// source included) collapsed the way a browser shows it, and the explicit breaks from
        /// br and block tags kept as line breaks.</summary>
        private static string Clean(string raw)
        {
            var lines = Regex.Replace(raw, @"\s+", " ").Split(Break)
                .Select(l => Regex.Replace(Decode(l), @"\s+", " ").Trim())
                .Where(l => l.Length > 0);
            return string.Join("\n", lines);
        }
    }
}

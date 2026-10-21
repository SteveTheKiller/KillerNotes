// ═══════════════════════════════════════════════════════════
//  SEARCH QUERY  -  what the search box asks for
// ═══════════════════════════════════════════════════════════
//
// 1.6.0, KN-P06. The search box takes words, "quoted phrases" and filters:
//
//   tag:work            a tag, matched whole
//   group:"Q4 plans"    a group at any level, with its subgroups; / between levels for a path
//   title:draft         a fragment of the title
//   after:2026-01-05    changed on or after the day      before:2026-06-30  changed on or before it
//   created:2026-10     made in that year, month or day   in:trash           only the trash
//
// Words keep the substring contract: every word must appear somewhere in the title, the text
// or the tags, folded by TextFold (case and how a letter was typed do not matter, accents do).
// Anything that only looks like a filter - an unknown key, a date that does not parse, a URL - is
// an ordinary word, so nothing typed is silently dropped. The sidebar (NoteStore.List) and the
// CLI build their SQL here; NoteSearch reads Words for counts and highlights; cross-note Replace
// replaces FreeText.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace KillerNotes.Services
{
    internal sealed class SearchQuery
    {
        public List<string> Words { get; } = [];
        public List<string> Tags { get; } = [];
        public List<string> Groups { get; } = [];
        public List<string> TitleWords { get; } = [];
        public DateTime? After { get; private set; }
        public DateTime? Before { get; private set; }               // exclusive: the day after the one typed
        public (DateTime From, DateTime To)? Created { get; private set; }
        public bool InTrash { get; private set; }
        private string _createdText = "";

        /// <summary>The words and phrases, the part cross-note Replace replaces.</summary>
        public string FreeText => string.Join(" ", Words);

        public bool IsEmpty => Words.Count == 0 && Tags.Count == 0 && Groups.Count == 0 && TitleWords.Count == 0 &&
                               After == null && Before == null && Created == null && !InTrash;

        public static SearchQuery Parse(string? text)
        {
            var query = new SearchQuery();
            foreach (var (key, value, raw) in Tokens(text ?? ""))
            {
                if (key == null || value.Length == 0 || !query.TryFilter(key, value)) query.Words.Add(key == null ? value : raw);
            }
            return query;
        }

        private bool TryFilter(string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "tag": Tags.Add(value.TrimStart('#')); return true;
                case "group": Groups.Add(value); return true;
                case "title": TitleWords.Add(value); return true;
                case "after" when Day(value) is DateTime day: After = day; return true;
                case "before" when Day(value) is DateTime day: Before = day.AddDays(1); return true;
                case "created" when Period(value) is { } period: Created = period; _createdText = value; return true;
                case "in" when string.Equals(value, "trash", StringComparison.OrdinalIgnoreCase): InTrash = true; return true;
                default: return false;
            }
        }

        private static DateTime? Day(string value) =>
            DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

        private static (DateTime, DateTime)? Period(string value)
        {
            if (Day(value) is DateTime day) return (day, day.AddDays(1));
            if (DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                return (month, month.AddMonths(1));
            if (value.Length == 4 && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int year) && year >= 1)
                return (new DateTime(year, 1, 1), new DateTime(year, 1, 1).AddYears(1));
            return null;
        }

        /// <summary>Splits on whitespace, keeping "quoted phrases" and key:"quoted values" whole.
        /// Key is null for a plain word or phrase; raw is the token as typed.</summary>
        private static IEnumerable<(string? Key, string Value, string Raw)> Tokens(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) yield break;
                int start = i;
                if (text[i] == '"')
                {
                    int close = text.IndexOf('"', i + 1);
                    int end = close < 0 ? text.Length : close;
                    string phrase = text.Substring(i + 1, end - i - 1).Trim();
                    i = close < 0 ? text.Length : close + 1;
                    if (phrase.Length > 0) yield return (null, phrase, text.Substring(start, i - start));
                    continue;
                }
                int colon = -1;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    if (text[i] == ':' && colon < 0)
                    {
                        colon = i;
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            int close = text.IndexOf('"', i + 2);
                            i = close < 0 ? text.Length : close + 1;
                            break;
                        }
                    }
                    i++;
                }
                string raw = text.Substring(start, i - start);
                if (colon > start && IsKey(text.Substring(start, colon - start)))
                {
                    string value = text.Substring(colon + 1, i - colon - 1).Trim('"').Trim();
                    yield return (text.Substring(start, colon - start), value, raw);
                }
                else yield return (null, raw, raw);
            }
        }

        private static bool IsKey(string key) =>
            key.Equals("tag", StringComparison.OrdinalIgnoreCase) || key.Equals("group", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("title", StringComparison.OrdinalIgnoreCase) || key.Equals("after", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("before", StringComparison.OrdinalIgnoreCase) || key.Equals("created", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("in", StringComparison.OrdinalIgnoreCase);

        // ---- SQL ----

        /// <summary>The WHERE clause for this query, its parameters added to the command. kn_fold must
        /// be registered on the connection. noteFold says whether the note_fold table (1.6.0) is
        /// there to read a body's stored fold from; without it the body is folded on the fly.</summary>
        public string BuildWhere(SqliteCommand cmd, bool noteFold)
        {
            var clauses = new List<string> { InTrash ? "deleted <> ''" : "deleted = ''" };
            string body = noteFold
                ? "COALESCE((SELECT f.plain_fold FROM note_fold f WHERE f.id = notes.id AND f.modified = notes.modified), kn_fold(plain))"
                : "kn_fold(plain)";
            for (int i = 0; i < Words.Count; i++)
            {
                clauses.Add($"(instr(kn_fold(title), $w{i}) > 0 OR instr(kn_fold(tags), $w{i}) > 0 OR instr({body}, $w{i}) > 0)");
                cmd.Parameters.AddWithValue($"$w{i}", TextFold.Fold(Words[i]));
            }
            for (int i = 0; i < TitleWords.Count; i++)
            {
                clauses.Add($"instr(kn_fold(title), $t{i}) > 0");
                cmd.Parameters.AddWithValue($"$t{i}", TextFold.Fold(TitleWords[i]));
            }
            // A tag is matched whole: the stored "a, b" list is compared as ",A,B,".
            for (int i = 0; i < Tags.Count; i++)
            {
                clauses.Add($"instr(',' || replace(replace(kn_fold(tags), ', ', ','), ' ,', ',') || ',', $g{i}) > 0");
                cmd.Parameters.AddWithValue($"$g{i}", "," + TextFold.Fold(Tags[i].Trim()) + ",");
            }
            // A group at any level of the path, and so its subgroups too.
            for (int i = 0; i < Groups.Count; i++)
            {
                string path = string.Join(NoteStore.GroupSep, Groups[i].Split('/').Select(p => p.Trim()).Where(p => p.Length > 0));
                clauses.Add($"instr($sep || kn_fold(notebook) || $sep, $p{i}) > 0");
                cmd.Parameters.AddWithValue($"$p{i}", NoteStore.GroupSep + TextFold.Fold(path) + NoteStore.GroupSep);
            }
            if (Groups.Count > 0) cmd.Parameters.AddWithValue("$sep", NoteStore.GroupSep);
            if (After is DateTime after) { clauses.Add("modified >= $after"); cmd.Parameters.AddWithValue("$after", Stamp(after)); }
            if (Before is DateTime before) { clauses.Add("modified < $before"); cmd.Parameters.AddWithValue("$before", Stamp(before)); }
            if (Created is { } created)
            {
                clauses.Add("created >= $cfrom AND created < $cto");
                cmd.Parameters.AddWithValue("$cfrom", Stamp(created.From));
                cmd.Parameters.AddWithValue("$cto", Stamp(created.To));
            }
            return string.Join(" AND ", clauses);
        }

        private static string Stamp(DateTime when) => when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        // ---- Explaining it ----

        /// <summary>What was searched, for the status line and the result count's tooltip:
        /// "titles, text and tags · tag:work · not the trash".</summary>
        public string Describe(Func<string, string> loc)
        {
            var parts = new List<string> { loc("Str_Search_ScopeAll") };
            parts.AddRange(Tags.Select(t => "tag:" + Quote(t)));
            parts.AddRange(Groups.Select(g => "group:" + Quote(g)));
            parts.AddRange(TitleWords.Select(t => "title:" + Quote(t)));
            if (After is DateTime after) parts.Add("after:" + after.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (Before is DateTime before) parts.Add("before:" + before.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (Created != null) parts.Add("created:" + _createdText);
            parts.Add(loc(InTrash ? "Str_Search_ScopeTrash" : "Str_Search_ScopeNoTrash"));
            return string.Join(" · ", parts);
        }

        private static string Quote(string value) => value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
    }
}

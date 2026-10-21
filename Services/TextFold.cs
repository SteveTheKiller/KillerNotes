using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KillerNotes.Services
{
    /// <summary>
    /// The one case- and composition-insensitive form text is compared in for library search and
    /// wikilink resolution: precomposed (NFC), then upper-cased by the invariant culture. So "café",
    /// "CAFÉ" and "cafe" plus a combining accent are one word, Greek and Cyrillic fold like Latin,
    /// and there is no Turkish dotless-i rule. SQLite's own NOCASE folds ASCII only, which is why
    /// "café" used to miss "CAFÉ". Registered on the connection as kn_fold() and COLLATE KNFOLD.
    ///
    /// Accents still count: "cafe" does not find "café". In Vietnamese, Polish or Czech a mark can
    /// change the word, so it is never dropped (1.6.0, KN-P06).
    ///
    /// FindAll is the same rule for places that need positions in the ORIGINAL text - Find in this
    /// note, the title-bar counts and highlights (NoteSearch) - so they agree with the sidebar.
    /// </summary>
    internal static class TextFold
    {
        internal static string Fold(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string composed;
            try { composed = text!.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { composed = text!; }   // unpaired surrogate: fold case only
            return composed.ToUpperInvariant();
        }

        /// <summary>Every place the needle occurs in text under the fold, as spans of the original
        /// text: a decomposed "e" plus accent is found by "é" and the span covers both. With
        /// caseSensitive, composition is still ignored but case is not; with overlapping, "aa" is
        /// found twice in "aaa", as a text editor's Find counts it.</summary>
        internal static List<(int Start, int Length)> FindAll(string text, string needle, bool caseSensitive = false, bool overlapping = false)
        {
            var hits = new List<(int Start, int Length)>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return hits;
            string target = caseSensitive ? Compose(needle) : Fold(needle);
            if (target.Length == 0 || target.Length > text.Length * 4) return hits;
            // Nearly all text is already precomposed, and invariant upper-casing keeps every
            // character in place, so the folded text lines up with the original one to one and no
            // map is needed. Only text holding a letter and its mark apart pays for Map.
            bool composed;
            try { composed = text.IsNormalized(NormalizationForm.FormC); }
            catch (ArgumentException) { composed = false; }
            string folded;
            int[]? starts = null, ends = null;
            if (composed) folded = caseSensitive ? text : text.ToUpperInvariant();
            else (folded, starts, ends) = Map(text, caseSensitive);
            int at = 0;
            while (at <= folded.Length - target.Length)
            {
                int hit = folded.IndexOf(target, at, StringComparison.Ordinal);
                if (hit < 0) break;
                int start = starts?[hit] ?? hit;
                int end = ends?[hit + target.Length - 1] ?? hit + target.Length;
                hits.Add((start, end - start));
                at = overlapping ? hit + 1 : hit + target.Length;
            }
            return hits;
        }

        private static string Compose(string text)
        {
            try { return text.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return text; }
        }

        /// <summary>The text folded one character cluster at a time (a base and the marks that follow
        /// it), with where each folded character came from. Cluster by cluster is the same as folding
        /// the whole string, because composition never reaches across a base character - except for
        /// Hangul jamo, which are grouped with their syllable here for that reason.</summary>
        private static (string Folded, int[] Starts, int[] Ends) Map(string text, bool caseSensitive)
        {
            var sb = new StringBuilder(text.Length);
            var starts = new List<int>(text.Length);
            var ends = new List<int>(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                int j = i + (char.IsSurrogatePair(text, i) ? 2 : 1);
                while (j < text.Length && Joins(text, j)) j += char.IsSurrogatePair(text, j) ? 2 : 1;
                string piece = Compose(text.Substring(i, j - i));
                if (!caseSensitive) piece = piece.ToUpperInvariant();
                foreach (char c in piece)
                {
                    sb.Append(c);
                    starts.Add(i);
                    ends.Add(j);
                }
                i = j;
            }
            return (sb.ToString(), starts.ToArray(), ends.ToArray());
        }

        private static bool Joins(string text, int at)
        {
            char c = text[at];
            if (c >= 'ᅠ' && c <= 'ᇿ') return true;   // Hangul medial vowels and final consonants
            var category = CharUnicodeInfo.GetUnicodeCategory(text, at);
            return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
        }
    }
}

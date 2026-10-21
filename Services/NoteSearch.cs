using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    public static class NoteSearch
    {
        public static FlowDocument LoadDocument(byte[]? blob, int format)
        {
            var doc = new FlowDocument();
            if (format == 1 && !MarkdownBlob.IsPackage(blob))
            {
                MarkdownBlob.Fill(doc, MarkdownBlob.Decode(blob));
                return doc;
            }
            if (blob == null || blob.Length == 0) return doc;
            using var bytes = new MemoryStream(blob, writable: false);
            new TextRange(doc.ContentStart, doc.ContentEnd).Load(bytes, DataFormats.XamlPackage);
            if (format == 1)
            {
                string text = MarkdownBlob.TextOf(doc);
                doc = new FlowDocument();
                MarkdownBlob.Fill(doc, text);
            }
            return doc;
        }

        public static int CountMatches(string text, string query) => CollectWordMatches(text, query).Count;

        private static List<(int Start, int Length)> CollectWordMatches(string text, string query)
        {
            var hits = new List<(int Start, int Length)>();
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!words.Add(word) || word.IndexOf(DocumentReplacement.ObjectBoundary) >= 0) continue;
                int at = 0;
                while (at <= text.Length - word.Length)
                {
                    int hit = text.IndexOf(word, at, StringComparison.OrdinalIgnoreCase);
                    if (hit < 0) break;
                    hits.Add((hit, word.Length));
                    at = hit + word.Length;
                }
            }
            return hits;
        }

        public static List<(int Start, int Length)> FindMatches(string text, string query)
        {
            var hits = CollectWordMatches(text, query);
            hits.Sort((left, right) => left.Start.CompareTo(right.Start));
            var merged = new List<(int Start, int Length)>();
            foreach (var hit in hits)
            {
                if (merged.Count == 0 || hit.Start >= merged[merged.Count - 1].Start + merged[merged.Count - 1].Length)
                {
                    merged.Add(hit);
                    continue;
                }
                var previous = merged[merged.Count - 1];
                int end = Math.Max(previous.Start + previous.Length, hit.Start + hit.Length);
                merged[merged.Count - 1] = (previous.Start, end - previous.Start);
            }
            return merged;
        }
    }
}

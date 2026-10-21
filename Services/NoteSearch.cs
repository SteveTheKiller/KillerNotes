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

        /// <summary>The query's words and phrases (never its filters) found under the sidebar's own
        /// rule (TextFold), as spans of the original text.</summary>
        private static List<(int Start, int Length)> CollectWordMatches(string text, string query)
        {
            var hits = new List<(int Start, int Length)>();
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (string word in SearchQuery.Parse(query).Words)
            {
                if (!words.Add(TextFold.Fold(word)) || word.IndexOf(DocumentReplacement.ObjectBoundary) >= 0) continue;
                hits.AddRange(TextFold.FindAll(text, word));
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

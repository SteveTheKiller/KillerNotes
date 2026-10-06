using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public class GlobalReplaceTests
    {
        [Fact]
        public void MatchSpanningFormattingRunsPreservesOutsideFormatting() => Sta.Run(() =>
        {
            var prefix = new Run("before ") { FontStyle = FontStyles.Italic };
            var suffix = new Run(" after") { FontWeight = FontWeights.Bold };
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(prefix);
            paragraph.Inlines.Add(new Bold(new Run("ab")));
            paragraph.Inlines.Add(new Run("cd"));
            paragraph.Inlines.Add(suffix);
            var doc = new FlowDocument(paragraph);
            Replace(doc, "abcd", "X");
            Assert.Equal("before X after\n", Flatten(doc).Plain);
            Assert.Equal(FontStyles.Italic, prefix.FontStyle);
            Assert.Equal(FontWeights.Bold, suffix.FontWeight);
        });

        [Fact]
        public void AdjacentRepeatedHitsAcrossRunBoundariesAreAllReplaced() => Sta.Run(() =>
        {
            var paragraph = new Paragraph(new Run("a"));
            paragraph.Inlines.Add(new Bold(new Run("ba")));
            paragraph.Inlines.Add(new Run("b ab"));
            var doc = new FlowDocument(paragraph);
            Replace(doc, "ab", "XYZ");
            Assert.Equal("XYZXYZ XYZ\n", Flatten(doc).Plain);
        });

        [Fact]
        public void ReplacementCanEndAtTheLastRunBoundary() => Sta.Run(() =>
        {
            var doc = new FlowDocument(new Paragraph(new Run("ab ab")));
            Replace(doc, "ab", "");
            Assert.Equal(" \n", Flatten(doc).Plain);
        });

        [Fact]
        public void MatchCanSpanTheFlattenedParagraphNewline() => Sta.Run(() =>
        {
            var doc = new FlowDocument(new Paragraph(new Run("left ab")));
            doc.Blocks.Add(new Paragraph(new Run("cd right")));
            Replace(doc, "ab\ncd", "X");
            Assert.Equal("left X right\n", Flatten(doc).Plain);
        });

        [Fact]
        public void LineBreakRetainsExistingFlatteningSemanticsDuringReplacement() => Sta.Run(() =>
        {
            var paragraph = new Paragraph(new Run("ab"));
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run("cd tail"));
            var doc = new FlowDocument(paragraph);
            Assert.Equal("abcd tail\n", Flatten(doc).Plain);
            Replace(doc, "abcd", "X");
            Assert.Equal("X tail\n", Flatten(doc).Plain);
            Assert.Equal("X tail\r\n", new TextRange(doc.ContentStart, doc.ContentEnd).Text);
        });

        private static (string Plain, List<(int Offset, TextPointer Start, int Length)> Runs) Flatten(FlowDocument doc) =>
            ((string, List<(int, TextPointer, int)>))Invoke("FlattenDoc", doc);

        private static void Replace(FlowDocument doc, string term, string replacement)
        {
            var flattened = Flatten(doc);
            var hits = (List<(int Start, int Length)>)Invoke("LiteralHits", flattened.Plain, term);
            Invoke("ApplyHits", flattened.Runs, hits, replacement);
        }

        private static object Invoke(string name, params object[] args) =>
            typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
    }
}

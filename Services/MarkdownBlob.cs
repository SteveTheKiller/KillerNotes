// How a markdown note's source text becomes bytes on disk, and back.
//
// A markdown note is one Paragraph per source line, Margin zeroed, and its text is the file:
// what the user typed is what is stored, byte for byte. That contract is what the tests in
// MarkdownBlobTests.cs pin, and it is why the walk below counts Runs and LineBreaks by hand
// rather than reading TextRange.Text, which inserts its own paragraph separators and re-encodes
// the line endings.
//
// The bytes themselves are a XamlPackage holding that paragraph-per-line document, NOT the raw
// UTF-8 source. Raw bytes were the 1.3.0 shape and they are a live hazard to older builds: every
// release before 1.3.0 predates the notes.format column, reads every blob as a XamlPackage, and
// dies on an unhandled ArgumentException from TextRange.Load. That throw lands in OpenStartupNote
// before the window is usable, so a shared .kndb or .knote carrying one markdown note took the
// receiving app down on every launch, recoverable only by editing the registry or deleting the
// database. Wrapped in a package, the same note opens in an older build as ordinary rich text
// showing its markdown source, and can even be edited there without breaking this side, because
// the extract below reads text out of whatever paragraphs it finds.
//
// The cost is a fixed floor of roughly 1.6 KB per markdown note, which is the package container.
// Anything past about that size comes out SMALLER than the raw source, since the package deflates.
//
// Decode sniffs rather than trusting the format column, so raw blobs written by 1.3.0 builds
// before this change keep loading.

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    internal static class MarkdownBlob
    {
        /// <summary>Fills a document with one zero-margin Paragraph per source line. Line endings
        /// are normalized first: a vault file edited elsewhere can arrive with either ending, and
        /// splitting on a raw \n would otherwise leave a trailing \r on every line.</summary>
        public static void Fill(FlowDocument doc, string text)
        {
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0) });
            if (doc.Blocks.Count == 0)
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        }

        /// <summary>The document's text as it should be stored, paragraphs joined by \n.</summary>
        public static string TextOf(FlowDocument doc)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var block in doc.Blocks)
            {
                if (!first) sb.Append('\n');
                first = false;
                if (block is Paragraph p) AppendParagraph(sb, p);
            }
            return sb.ToString();
        }

        /// <summary>One paragraph's text, LineBreaks included.</summary>
        private static void AppendParagraph(StringBuilder sb, Paragraph p)
        {
            void Walk(InlineCollection inlines)
            {
                foreach (var i in inlines)
                {
                    switch (i)
                    {
                        case Run r: sb.Append(r.Text); break;
                        case LineBreak: sb.Append('\n'); break;
                        // Hyperlink derives from Span, so this catches it too, and a separate
                        // Hyperlink case below would be unreachable. Only the link's text is
                        // wanted here: a markdown note carries its own link syntax as characters
                        // in the Run, so there is no URL to recover off the element.
                        case Span s: Walk(s.Inlines); break;
                    }
                }
            }
            Walk(p.Inlines);
        }

        /// <summary>Source text as the bytes to store. The package is built from a FRESH
        /// paragraph-per-line document rather than from the editor's own, so what is written is
        /// always the canonical shape the extract above reads back. Formatting a user applied to a
        /// markdown note with Ctrl+B is dropped here, which is the documented behavior: the
        /// characters survive, the weight does not.</summary>
        public static byte[] Encode(string text)
        {
            var doc = new FlowDocument();
            Fill(doc, text);
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            return ms.ToArray();
        }

        /// <summary>A stored blob as source text. Sniffs the package signature instead of
        /// trusting the row's format, so a raw UTF-8 blob from a 1.3.0 build before the wrapper
        /// still reads. An unreadable package falls back to the raw decode rather than throwing:
        /// showing the bytes as text is recoverable, an exception on the startup path is not.</summary>
        public static string Decode(byte[]? blob)
        {
            if (blob == null || blob.Length == 0) return "";
            if (!IsPackage(blob)) return new UTF8Encoding(false).GetString(blob);
            try
            {
                var doc = new FlowDocument();
                using var ms = new MemoryStream(blob);
                new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage);
                return TextOf(doc);
            }
            catch
            {
                return new UTF8Encoding(false).GetString(blob);
            }
        }

        /// <summary>A XamlPackage is a zip, so it opens with the local file header signature.
        /// Markdown source starting with those four bytes is not a case worth chasing: "PK\x03\x04"
        /// carries two control characters no editor produces.</summary>
        public static bool IsPackage(byte[]? blob) =>
            blob != null && blob.Length >= 4 &&
            blob[0] == 0x50 && blob[1] == 0x4B && blob[2] == 0x03 && blob[3] == 0x04;
    }
}

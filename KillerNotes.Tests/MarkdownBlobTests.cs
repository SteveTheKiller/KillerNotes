using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// How a markdown note survives the trip to disk and back (MarkdownBlob.cs).
    ///
    /// Two contracts are pinned here and they pull in opposite directions.
    ///
    /// The first is fidelity. Markdown is whitespace significant: a tab is not four spaces, two
    /// trailing spaces are a hard line break, and a run of blank lines is not one blank line.
    /// So the corpus below asserts EXACT text after a round trip, not a normalized comparison.
    /// The WPF text stack is full of places that would quietly tidy any of that up.
    ///
    /// The second is that older builds must survive reading one of these notes. Every release
    /// before the notes.format column reads every blob as a XamlPackage and dies on an unhandled
    /// ArgumentException from TextRange.Load, on the startup path, before the window is usable.
    /// OlderBuildCanReadAMarkdownNote is that claim in test form: it does exactly what such a
    /// build does, with no knowledge of the format column, and requires it to come back readable.
    /// If someone ever changes Encode to write raw source again, that test is what fails.
    ///
    /// Every body runs on STA (Sta.Run): XamlPackage save and load throw on xunit's MTA thread.
    /// </summary>
    public class MarkdownBlobTests
    {
        private static string Roundtrip(string source) => MarkdownBlob.Decode(MarkdownBlob.Encode(source));

        // Non-ASCII is built from codepoints rather than typed, so this file stays ASCII on disk.
        // A literal here would make the corpus depend on the compiler guessing the file's encoding,
        // and a failure would then be the test project's rather than the code's.
        private static readonly string Accented = "caf" + (char)0x00E9;
        private static readonly string NoBreakSpace = ((char)0x00A0).ToString();
        private static readonly string Check = ((char)0x2713).ToString();
        private static readonly string Emoji = char.ConvertFromUtf32(0x1F600);   // a surrogate pair

        [Theory]
        [InlineData("# Title\nsome text")]
        [InlineData("\tindented\n\t\tdeeper\ntext\tmid")]
        [InlineData("hard break  \ntwo trailing   \nnone")]
        [InlineData("a\n\n\n\nb")]
        [InlineData("text\n\n    code()\n    more()\n\ntext")]
        [InlineData("<Run>&amp; </Paragraph> \"quotes\" 'apos'")]
        [InlineData("text\n")]
        [InlineData("\ntext")]
        [InlineData("   \n\t\n   ")]
        [InlineData(" ")]
        [InlineData("")]
        [InlineData("| a | b |\n|---|---|\n| 1 | 2 |\n\n- item\n  - nested\n* star")]
        [InlineData("  two\n   three\n    four\n\tTAB\n \t mixed ")]
        public void SourceSurvivesTheRoundTripExactly(string source) =>
            Sta.Run(() => Assert.Equal(source, Roundtrip(source)));

        [Fact]
        public void NonAsciiSurvivesIncludingSurrogatePairs() => Sta.Run(() =>
        {
            string source = Accented + " nbsp[" + NoBreakSpace + "] " + Check + " " + Emoji;
            Assert.Equal(source, Roundtrip(source));
        });

        [Fact]
        public void ALongLineSurvivesWhole() => Sta.Run(() =>
        {
            string source = new string('x', 5000);
            Assert.Equal(source, Roundtrip(source));
        });

        /// <summary>Line endings are normalized on the way in, so CRLF source comes back as LF.
        /// This is the one place the round trip is deliberately not identical: the stored form is
        /// LF, and a vault file edited elsewhere can arrive with either ending.</summary>
        [Fact]
        public void CrlfIsNormalizedToLf() => Sta.Run(() =>
            Assert.Equal("a\nb\n\nc", Roundtrip("a\r\nb\r\n\r\nc")));

        [Fact]
        public void EncodeWritesAXamlPackage() => Sta.Run(() =>
            Assert.True(MarkdownBlob.IsPackage(MarkdownBlob.Encode("# hello"))));

        /// <summary>
        /// What a build that predates the format column does with one of these notes: read the
        /// blob as a XamlPackage, knowing nothing about markdown. It must not throw, and the
        /// source must be there to read. This is the whole reason the wrapper exists.
        /// </summary>
        [Fact]
        public void OlderBuildCanReadAMarkdownNote() => Sta.Run(() =>
        {
            const string source = "# heading\n\n- one\n- two\n\n\tcode";
            byte[] blob = MarkdownBlob.Encode(source);

            var doc = new FlowDocument();
            using var ms = new MemoryStream(blob);
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.Load(ms, DataFormats.XamlPackage);   // the call that used to kill the process

            Assert.Contains("# heading", range.Text);
            Assert.Contains("- two", range.Text);
        });

        /// <summary>Blobs written before the wrapper are raw UTF-8 with no package around them.
        /// Decode sniffs for the zip signature rather than trusting the row's format, so those
        /// keep loading.</summary>
        [Fact]
        public void LegacyRawBlobsStillDecode() => Sta.Run(() =>
        {
            byte[] raw = new UTF8Encoding(false).GetBytes("# was stored raw\n\n\tindented");
            Assert.False(MarkdownBlob.IsPackage(raw));
            Assert.Equal("# was stored raw\n\n\tindented", MarkdownBlob.Decode(raw));
        });

        [Fact]
        public void DecodeOfNothingIsEmpty() => Sta.Run(() =>
        {
            Assert.Equal("", MarkdownBlob.Decode(null));
            Assert.Equal("", MarkdownBlob.Decode([]));
        });

        /// <summary>A blob that claims to be a package and is not falls back to the raw decode.
        /// Showing bytes as text is recoverable; throwing on the startup path is not.</summary>
        [Fact]
        public void ATruncatedPackageFallsBackInsteadOfThrowing() => Sta.Run(() =>
        {
            byte[] junk = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02];
            Assert.NotNull(MarkdownBlob.Decode(junk));
        });

        [Fact]
        public void FillMakesOneZeroMarginParagraphPerLine() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            MarkdownBlob.Fill(doc, "one\ntwo\n\nfour");

            var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
            Assert.Equal(4, paragraphs.Count);
            Assert.All(paragraphs, p => Assert.Equal(new Thickness(0), p.Margin));
            Assert.Equal("one\ntwo\n\nfour", MarkdownBlob.TextOf(doc));
        });

        /// <summary>An empty source still gets a paragraph, or the editor opens with no line to
        /// type on.</summary>
        [Fact]
        public void FillAlwaysLeavesSomewhereToType() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            MarkdownBlob.Fill(doc, "");
            Assert.Single(doc.Blocks);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NativeListsKeepTheirTextAcrossSaveAndReopen(bool numbered) => Sta.Run(() =>
        {
            var editor = new System.Windows.Controls.RichTextBox();
            editor.Document.Blocks.Clear();
            MarkdownBlob.Fill(editor.Document, "keep this text");
            editor.SelectAll();
            var command = numbered ? EditingCommands.ToggleNumbering : EditingCommands.ToggleBullets;
            command.Execute(null, editor);
            Assert.Single(editor.Document.Blocks.OfType<List>());
            Assert.Equal("keep this text", Roundtrip(MarkdownBlob.TextOf(editor.Document)));
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MarkdownEditorRefusesNativeListCommands(bool numbered) => Sta.Run(() =>
        {
            var editor = new System.Windows.Controls.RichTextBox();
            MarkdownEditing.Attach(editor, () => true);
            editor.Document.Blocks.Clear();
            MarkdownBlob.Fill(editor.Document, "keep this text");
            editor.SelectAll();
            var command = numbered ? EditingCommands.ToggleNumbering : EditingCommands.ToggleBullets;
            command.Execute(null, editor);
            Assert.Empty(editor.Document.Blocks.OfType<List>());
            Assert.Equal("keep this text", MarkdownBlob.TextOf(editor.Document));
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RichPasteIsPlainTextInMarkdownOnly(bool markdown) => Sta.Run(() =>
        {
            var editor = new System.Windows.Controls.RichTextBox();
            MarkdownEditing.Attach(editor, () => markdown);
            var data = new DataObject();
            data.SetData(DataFormats.Rtf, @"{\rtf1\ansi rich text}");
            data.SetData(DataFormats.UnicodeText, "first\tcell\r\nsecond row");
            var paste = new DataObjectPastingEventArgs(data, false, DataFormats.Rtf);
            editor.RaiseEvent(paste);
            Assert.Equal(markdown ? DataFormats.UnicodeText : DataFormats.Rtf, paste.FormatToApply);
            Assert.Equal("first\tcell\r\nsecond row", paste.DataObject.GetData(DataFormats.UnicodeText));
        });

        [Fact]
        public void RichTablePasteWithoutTextKeepsEveryCellAcrossSave() => Sta.Run(() =>
        {
            var source = new FlowDocument();
            var table = new Table();
            var group = new TableRowGroup();
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run("first"))));
            row.Cells.Add(new TableCell(new Section(new Paragraph(new Run("second")))));
            group.Rows.Add(row);
            table.RowGroups.Add(group);
            source.Blocks.Add(table);
            using var package = new MemoryStream();
            new TextRange(source.ContentStart, source.ContentEnd).Save(package, DataFormats.XamlPackage);
            package.Position = 0;
            var editor = new System.Windows.Controls.RichTextBox();
            MarkdownEditing.Attach(editor, () => true);
            var paste = new DataObjectPastingEventArgs(
                new DataObject(DataFormats.XamlPackage, package), false, DataFormats.XamlPackage);
            editor.RaiseEvent(paste);
            Assert.False(paste.CommandCancelled);
            Assert.Equal(DataFormats.UnicodeText, paste.FormatToApply);
            string text = (string)paste.DataObject.GetData(DataFormats.UnicodeText);
            Assert.Equal("first\tsecond", Roundtrip(text));
        });
    }
}

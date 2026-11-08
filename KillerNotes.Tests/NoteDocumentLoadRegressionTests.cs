using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class NoteDocumentLoadRegressionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OpeningMarkdownCannotUndoAndSaveTheOutgoingNote(bool outgoingMarkdown) => Sta.RunApplication(() =>
        {
            using var store = new TempStore();
            var document = new FlowDocument();
            if (outgoingMarkdown) MarkdownBlob.Fill(document, "outgoing secret");
            else document.Blocks.Add(new Paragraph(new Bold(new Run("outgoing secret"))));
            var editor = new RichTextBox(document);
            InputMethod.SetIsInputMethodEnabled(editor, false);
            var host = new Window { Content = editor, Width = 300, Height = 200, Opacity = 0, ShowInTaskbar = false };
            host.Show();
            try
            {
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = true;
                editor.CaretPosition = editor.Document.Blocks.FirstBlock.ContentEnd;
                editor.BeginChange();
                editor.CaretPosition.InsertTextInRun(" edited");
                editor.EndChange();
                Assert.True(editor.CanUndo);
                long incoming = NoteStore.Create("incoming", 1);

                using (new NoteDocumentLoad(editor))
                {
                    editor.Document.Blocks.Clear();
                    MarkdownBlob.Fill(editor.Document, "incoming text");
                }

                while (editor.CanUndo) editor.Undo();
                string saved = MarkdownBlob.TextOf(editor.Document);
                NoteStore.Save(incoming, "incoming", MarkdownBlob.Encode(saved), saved);
                Assert.Equal("incoming text", MarkdownBlob.Decode(NoteStore.LoadContent(incoming)));
                Assert.True(editor.IsUndoEnabled);
            }
            finally { host.Close(); }
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedLoadRestoresTheExistingUndoPreference(bool enabled) => Sta.RunApplication(() =>
        {
            var editor = new RichTextBox(new FlowDocument(new Paragraph(new Run("outgoing"))))
                { IsUndoEnabled = enabled };
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var load = new NoteDocumentLoad(editor);
                Assert.False(editor.IsUndoEnabled);
                editor.Document.Blocks.Clear();
                throw new InvalidOperationException("unreadable note");
            }));
            Assert.Equal(enabled, editor.IsUndoEnabled);
            Assert.False(editor.CanUndo);
        });
    }
}

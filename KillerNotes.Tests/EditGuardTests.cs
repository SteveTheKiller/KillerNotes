using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The RichTextBox's own read-only flag only stops typing and its built-in commands. Code that
    /// edits the document directly has to ask CanEditCurrentNote, or a trashed note shows an edit
    /// that MarkDirty then refuses to save.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class EditGuardTests
    {
        private static ShellProbe OpenNote(string text, bool inTrash, out RichTextBox editor, out Run run)
        {
            var shell = new ShellProbe(currentId: 1);
            run = new Run(text);
            editor = new RichTextBox(new FlowDocument(new Paragraph(run)));
            shell.Set("Editor", editor);
            shell.Set("_currentInTrash", inTrash);
            return shell;
        }

        private static string TextOf(RichTextBox editor) =>
            new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;

        private static void Enter(ShellProbe shell, params string[] keys)
        {
            var calc = new CalcEngine();
            foreach (string key in keys) calc.Input(key);
            shell.Set("_kalc", calc);
        }

        [Theory]
        [InlineData("KalcPrint")]
        [InlineData("KalcPrintEquation")]
        public void PrintingTheCalculatorIntoATrashedNoteLeavesItAlone(string command)
        {
            Sta.Run(() =>
            {
                var shell = OpenNote("kept", inTrash: true, out var editor, out _);
                Enter(shell, "4", "add", "2", "eq");

                shell.Run(command);

                Assert.Equal("kept\r\n", TextOf(editor));
                Assert.NotEqual("", shell.Status.Text);   // the note is in the trash
            });
        }

        [Fact]
        public void PrintingTheCalculatorIntoAnEditableNoteStillInsertsIt() => Sta.Run(() =>
        {
            var shell = OpenNote("kept", inTrash: false, out var editor, out _);
            Enter(shell, "4", "add", "2", "eq");

            shell.Run("KalcPrint");

            Assert.Contains("6", TextOf(editor));
        });

        [Fact]
        public void TypingAUrlIntoATrashedNoteDoesNotLinkIt() => Sta.Run(() =>
        {
            var shell = OpenNote("see https://example.com/page", inTrash: true, out var editor, out var run);
            editor.CaretPosition = run.ContentEnd;

            shell.Run("AutoLinkWordBeforeCaret");

            Assert.Empty(editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()));
        });

        [Fact]
        public void TypingAUrlIntoAnEditableNoteStillLinksIt() => Sta.Run(() =>
        {
            var shell = OpenNote("see https://example.com/page", inTrash: false, out var editor, out var run);
            editor.CaretPosition = run.ContentEnd;

            shell.Run("AutoLinkWordBeforeCaret");

            Assert.Single(editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()));
        });

        [Fact]
        public void ALoadThatThrowsDoesNotLeaveTheNoteUnsaveable() => Sta.Run(() =>
        {
            using var store = new TempStore();
            var note = new Note { Id = 7, Title = "Seven" };
            var shell = new ShellProbe();
            shell.Set("_notes", new List<Note> { note });
            shell.Set("Editor", new RichTextBox());
            // The probe has no title box, so the load fails partway, after it has begun.

            var error = shell.Run("OpenNote", 7L);

            Assert.NotNull(error);
            Assert.False(shell.Get<bool>("_loadingNote"));
            Assert.True(shell.Get<bool>("_loadFailed"));   // a half-loaded editor is never saved over the note
        });
    }
}

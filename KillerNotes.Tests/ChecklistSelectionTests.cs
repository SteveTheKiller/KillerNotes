using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A selection that covers whole lines (Shift+Down, triple-click) ends at the first position of
    /// the line after them. That line holds none of the selected text, so Alt+C leaves it alone.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ChecklistSelectionTests
    {
        [Fact]
        public void WholeLineSelectionDoesNotReachTheLineAfterIt() => Sta.Run(() =>
        {
            var (shell, editor) = Prepared(out var paras);
            editor.Selection.Select(Head(paras[0]), Head(paras[2]));   // lines one and two, in full

            EditorShell.Invoke(shell, "ToggleChecklist");

            Assert.Equal(new[] { "☐ one", "☐ two", "three" }, Lines(editor));
        });

        [Fact]
        public void ASelectionInsideTheLastLineStillChangesIt() => Sta.Run(() =>
        {
            var (shell, editor) = Prepared(out var paras);
            editor.Selection.Select(Head(paras[1]), paras[2].ContentEnd);   // line two through the end of three

            EditorShell.Invoke(shell, "ToggleChecklist");

            Assert.Equal(new[] { "one", "☐ two", "☐ three" }, Lines(editor));
        });

        [Fact]
        public void ALongLineIsReadOnlyAsFarAsItsHead() => Sta.Run(() =>
        {
            var longLine = new Paragraph(new Run("- [ ] " + new string('x', 5000)));
            var shortLine = new Paragraph(new Run("ab"));
            var doc = new FlowDocument(longLine);
            doc.Blocks.Add(shortLine);   // both sit in a document, as in the editor

            string head = (string)typeof(MainWindow).GetMethod("LeadingText", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, new object[] { longLine, 6 })!;
            string whole = (string)typeof(MainWindow).GetMethod("LeadingText", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, new object[] { shortLine, 6 })!;

            Assert.Equal("- [ ] ", head);
            Assert.Equal("ab", whole);
            GC.KeepAlive(doc);
        });

        private static (MainWindow, RichTextBox) Prepared(out Paragraph[] paras)
        {
            var (shell, editor) = EditorShell.Make();
            paras = new[] { new Paragraph(new Run("one")), new Paragraph(new Run("two")), new Paragraph(new Run("three")) };
            editor.Document = new FlowDocument();
            foreach (var p in paras) editor.Document.Blocks.Add(p);
            return (shell, editor);
        }

        private static TextPointer Head(Paragraph p) => p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);

        private static string[] Lines(RichTextBox editor) =>
            editor.Document.Blocks.OfType<Paragraph>().Select(p => new TextRange(p.ContentStart, p.ContentEnd).Text).ToArray();
    }
}

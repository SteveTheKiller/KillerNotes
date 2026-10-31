using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A note open from the trash shows its text but never saves an edit, so the find bar must not
    /// replace anything in it: the change would be shown, reported as replaced, and then lost.
    /// </summary>
    public sealed class FindReplaceTrashTests
    {
        [Fact]
        public void ReplaceAllLeavesATrashedNoteAlone() => Sta.Run(() =>
        {
            var (shell, editor) = Prepared();

            EditorShell.Invoke(shell, "ReplaceAll_Click", shell, new RoutedEventArgs());

            Assert.Equal("cat", Text(editor));
        });

        [Fact]
        public void ReplaceCurrentLeavesATrashedNoteAlone() => Sta.Run(() =>
        {
            var (shell, editor) = Prepared();
            EditorShell.Set(shell, "_findIndex", 0);

            EditorShell.Invoke(shell, "ReplaceCurrent_Click", shell, new RoutedEventArgs());

            Assert.Equal("cat", Text(editor));
        });

        [Fact]
        public void TheReplaceBarDoesNotOpenOnATrashedNote() => Sta.Run(() =>
        {
            var (shell, _) = Prepared();

            EditorShell.Invoke(shell, "OpenReplaceBar");

            Assert.False((bool)EditorShell.Get(shell, "_replaceOpen")!);
        });

        private static (MainWindow, RichTextBox) Prepared()
        {
            var (shell, editor) = EditorShell.Make();
            editor.Document = new FlowDocument(new Paragraph(new Run("cat")));
            EditorShell.Set(shell, "_currentInTrash", true);
            EditorShell.Set(shell, "_findTerm", "cat");
            EditorShell.Set(shell, "_findCase", true);
            EditorShell.Set(shell, "_findPlain", "cat");
            EditorShell.Set(shell, "_findPlainStale", false);
            EditorShell.Set(shell, "ReplaceBox", new TextBox { Text = "dog" });
            ((System.Collections.Generic.List<(int Start, int Length)>)EditorShell.Get(shell, "_findHits")!).Add((0, 3));
            return (shell, editor);
        }

        private static string Text(RichTextBox editor) =>
            new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd('\r', '\n');
    }
}

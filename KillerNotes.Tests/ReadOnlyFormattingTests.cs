using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public class ReadOnlyFormattingTests
    {
        [Theory]
        [InlineData("ToggleChecklist")]
        [InlineData("SetHeadingLevel")]
        [InlineData("Heading_Click")]
        public void FormattingCommandsLeaveReadOnlyEditorUnchanged(string command) => Sta.Run(() =>
        {
            var flag = typeof(NoteStore).GetProperty(nameof(NoteStore.IsReadOnly))!;
            bool before = NoteStore.IsReadOnly;
            try
            {
                flag.GetSetMethod(nonPublic: true)!.Invoke(null, new object[] { true });
                // A detached shell keeps startup, settings, and database access out of this test.
                var window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                typeof(MainWindow).GetField("_currentId", fields)!.SetValue(window, 1L);
                var editor = new RichTextBox(new FlowDocument(new Paragraph(new Run("unchanged")))) { IsReadOnly = true };
                typeof(MainWindow).GetField("Editor", fields)!.SetValue(window, editor);
                string text = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
                object[] args = command == "SetHeadingLevel" ? new object[] { 1 }
                    : command == "Heading_Click" ? new object[] { window, new System.Windows.RoutedEventArgs() }
                    : Array.Empty<object>();
                typeof(MainWindow).GetMethod(command, fields)!.Invoke(window, args);
                Assert.Equal(text, new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
                Assert.Equal(System.Windows.FontWeights.Normal, editor.Document.Blocks.FirstBlock.FontWeight);
                Assert.False((bool)typeof(MainWindow).GetField("_dirty", fields)!.GetValue(window)!);
            }
            finally { flag.GetSetMethod(nonPublic: true)!.Invoke(null, new object[] { before }); }
        });
    }
}

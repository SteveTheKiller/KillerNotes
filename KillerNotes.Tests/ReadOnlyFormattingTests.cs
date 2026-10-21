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

        /// <summary>The custom mutations behind Ctrl+Shift shortcuts, menus and companion
        /// windows, which bypass the RichTextBox's own read-only handling. Run once for a note
        /// in the Trash and once for a database another process holds for writing.</summary>
        [Theory]
        [InlineData("Strike_Click", true)]
        [InlineData("Mono_Click", true)]
        [InlineData("InsertRule_Click", true)]
        [InlineData("ConvertSelectionToList", true)]
        [InlineData("InsertTable", true)]
        [InlineData("PrintDictationToNote", true)]
        [InlineData("MarkDirty", true)]
        [InlineData("Strike_Click", false)]
        [InlineData("Mono_Click", false)]
        [InlineData("InsertRule_Click", false)]
        [InlineData("ConvertSelectionToList", false)]
        [InlineData("InsertTable", false)]
        [InlineData("PrintDictationToNote", false)]
        [InlineData("MarkDirty", false)]
        public void CustomMutationsLeaveAReadOnlyNoteAlone(string command, bool trash) => Sta.Run(() =>
        {
            var flag = typeof(NoteStore).GetProperty(nameof(NoteStore.IsReadOnly))!;
            bool before = NoteStore.IsReadOnly;
            try
            {
                if (!trash) flag.GetSetMethod(nonPublic: true)!.Invoke(null, new object[] { true });
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
                typeof(MainWindow).GetField("_currentId", fields)!.SetValue(window, 1L);
                typeof(MainWindow).GetField("_currentInTrash", fields)!.SetValue(window, trash);
                typeof(MainWindow).GetField("_saveTimer", fields)!.SetValue(window, new System.Windows.Threading.DispatcherTimer());
                typeof(MainWindow).GetField("_syntaxTimer", fields)!.SetValue(window, new System.Windows.Threading.DispatcherTimer());
                typeof(MainWindow).GetField("StatusText", fields)!.SetValue(window, new TextBlock());
                var editor = new RichTextBox(new FlowDocument(new Paragraph(new Run("line one")))) { IsReadOnly = true };
                editor.SelectAll();
                typeof(MainWindow).GetField("Editor", fields)!.SetValue(window, editor);
                string xaml = System.Windows.Markup.XamlWriter.Save(editor.Document);

                object[] args = command switch
                {
                    "InsertTable" => new object[] { 2, 2 },
                    "PrintDictationToNote" => new object[] { "dictated" },
                    "ConvertSelectionToList" or "MarkDirty" => Array.Empty<object>(),
                    _ => new object[] { window, new System.Windows.RoutedEventArgs() },
                };
                typeof(MainWindow).GetMethod(command, fields)!.Invoke(window, args);

                Assert.Equal(xaml, System.Windows.Markup.XamlWriter.Save(editor.Document));
                Assert.False((bool)typeof(MainWindow).GetField("_dirty", fields)!.GetValue(window)!);
            }
            finally { flag.GetSetMethod(nonPublic: true)!.Invoke(null, new object[] { before }); }
        });
    }
}

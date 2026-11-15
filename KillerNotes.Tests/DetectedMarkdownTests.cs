using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class DetectedMarkdownTests
    {
        [Fact]
        public void DetectionToggleUpdatesTheUnsavedSidebarRow() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            MarkdownAuthoringTests.Ready(window);
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = (RichTextBox)window.FindName("Editor");
            editor.Document.Blocks.Clear();
            MarkdownBlob.Fill(editor.Document, "# Unsaved Markdown");
            var toggle = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("AboutDetectMarkdown");
            var list = (ListBox)window.FindName("NotesList");
            toggle.IsChecked = false;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Collapsed, ((Note)list.SelectedItem).FormatBadgeVisibility);
            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, ((Note)list.SelectedItem).FormatBadgeVisibility);
            Assert.Equal("# Unsaved Markdown", MarkdownBlob.TextOf(editor.Document));
        });

        [Fact]
        public void ExistingRichStoredMarkdownHasASidebarBadge() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            string source = new string('x', 150) + "\n\n# Release plan\n\n**Hold**\n\n| Item | Result |\n| --- | --- |\n| Test | Passed |";
            long id = NoteStore.Create("Imported Markdown");
            NoteStore.SetFormat(id, Note.FormatRich, MarkdownBlob.Encode(source), source);
            var note = NoteStore.List().Single(n => n.Id == id);
            Assert.Equal(Note.FormatRich, note.Format);
            Assert.Equal(Visibility.Visible, note.FormatBadgeVisibility);
        });

        [Fact]
        public void ConvertingDetectedSourcePreservesMarkdownCharacters() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            MarkdownAuthoringTests.Ready(window);
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = (RichTextBox)window.FindName("Editor");
            const string source = "# Release plan\n\n**Hold the release.**\n\n| Item | Result |\n| --- | --- |\n| `Test` | [Passed](https://example.test) |";
            editor.Document.Blocks.Clear();
            MarkdownBlob.Fill(editor.Document, source);
            var note = (Note)((ListBox)window.FindName("NotesList")).SelectedItem;
            host.ShowNoteCard(new[] { note.Id }, (Button)window.FindName("NewNoteBtn"), false);
            bool confirmed = false;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog != null)
                {
                    timer.Stop();
                    confirmed = true;
                    ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (DateTime.UtcNow > deadline) { timer.Stop(); window.Close(); }
            };
            try
            {
                timer.Start();
                ((RadioButton)window.FindName("CardMarkdownSeg")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            finally { timer.Stop(); }
            Assert.True(confirmed);
            Assert.Equal(source, MarkdownBlob.TextOf(editor.Document));
            Assert.Equal(Note.FormatMarkdown, NoteStore.GetNote(note.Id)!.Format);
        });
    }
}

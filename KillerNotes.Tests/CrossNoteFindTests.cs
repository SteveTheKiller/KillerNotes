using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class CrossNoteFindTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [Fact]
        public void ForwardAndBackwardSkipTitleTagAndUnreadableResultsAndWrap() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long first = Rich("First", "cat one");
            Rich("cat title", "no body match");
            long tagged = Rich("Tagged", "no body match");
            NoteStore.SetNoteTags(tagged, "cat");
            long broken = NoteStore.Create("cat unreadable");
            NoteStore.Save(broken, "cat unreadable", Encoding.UTF8.GetBytes("not a package"), "cat");
            long last = Rich("Last", "cat two");
            var window = Search(first);

            Assert.Equal(last, Next(window, 1));
            Assert.Equal(last, Next(window, -1));
            Set(window, "_currentId", last);
            Assert.Equal(first, Next(window, 1));
            Assert.Equal(first, Next(window, -1));
        });

        [Fact]
        public void WholeWordAndCaseOptionsFilterCandidatesUsingTheSameRulesAsInNoteFind() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long first = Rich("First", "cat one");
            Rich("Substring", "concatenate");
            Rich("Case", "CAT");
            long last = Rich("Last", "a cat");
            var window = Search(first);
            Set(window, "_findWord", true);
            Set(window, "_findCase", true);
            Assert.Equal(last, Next(window, 1));
        });

        [Fact]
        public void MarkdownPackagesAndLegacyRawMarkdownParticipate() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long first = Rich("First", "cat one");
            long markdown = NoteStore.Create("Markdown", format: 1);
            NoteStore.Save(markdown, "Markdown", MarkdownBlob.Encode("# cat"), "# cat");
            long legacy = NoteStore.Create("Legacy", format: 1);
            NoteStore.Save(legacy, "Legacy", Encoding.UTF8.GetBytes("cat raw"), "cat raw");
            var window = Search(first);
            Assert.Equal(markdown, Next(window, 1));
            Set(window, "_currentId", markdown);
            Assert.Equal(legacy, Next(window, 1));
        });

        [Fact]
        public void NoOtherBodyMatchesLeavesNavigationInTheCurrentNote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long first = Rich("First", "cat one");
            Rich("cat title", "no body match");
            var window = Search(first);
            Assert.Equal(-1, Next(window, 1));
            Assert.Equal(-1, Next(window, -1));
        });

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public void ACurrentNoteOutsideTheResultSetStartsAtTheCorrespondingEnd(int delta) => Sta.Run(() =>
        {
            using var store = new TempStore();
            long first = Rich("First", "cat one");
            long last = Rich("Last", "cat two");
            var window = Search(-1);
            Assert.Equal(delta > 0 ? first : last, Next(window, delta));
        });

        private static long Rich(string title, string text)
        {
            var doc = new FlowDocument(new Paragraph(new Run(text)));
            using var bytes = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(bytes, DataFormats.XamlPackage);
            long id = NoteStore.Create(title);
            NoteStore.Save(id, title, bytes.ToArray(), text);
            return id;
        }

        private static MainWindow Search(long current)
        {
            var window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            Set(window, "_findTerm", "cat");
            Set(window, "_currentId", current);
            Set(window, "_notes", NoteStore.List("cat", "custom"));
            return window;
        }

        private static void Set(MainWindow window, string name, object value) =>
            typeof(MainWindow).GetField(name, Fields)!.SetValue(window, value);

        private static long Next(MainWindow window, int delta)
        {
            int index = window.FindNextNoteIndex(delta);
            var notes = (System.Collections.Generic.List<Models.Note>)typeof(MainWindow)
                .GetField("_notes", Fields)!.GetValue(window)!;
            return index < 0 ? -1 : notes[index].Id;
        }
    }
}

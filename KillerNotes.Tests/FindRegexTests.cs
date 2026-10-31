using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The find bar searches the whole note flattened with one newline per paragraph, so a regex
    /// anchor has to mean the start and end of a line, not of the whole note.
    /// </summary>
    public sealed class FindRegexTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [Theory]
        [InlineData(@"^TODO", new[] { 4, 11 })]
        [InlineData(@"\d+$", new[] { 2, 18 })]
        public void LineAnchorsMatchOnEveryLineOfTheNote(string pattern, int[] starts)
        {
            // Lines: "a 1", "TODO b", "TODO c 22"
            string plain = "a 1\nTODO b\nTODO c 22";
            var hits = Hits(plain, pattern);

            Assert.Equal(starts.Length, hits.Count);
            for (int i = 0; i < starts.Length; i++) Assert.Equal(starts[i], hits[i].Start);
        }

        [Fact]
        public void WholeWordAnchorsStillMatchOnLaterLines()
        {
            var hits = Hits("x\nTODO\nTODO", @"^TODO$", word: true);

            Assert.Equal(2, hits.Count);
        }

        private static List<(int Start, int Length)> Hits(string plain, string pattern, bool word = false)
        {
            var window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            Set(window, "_findTerm", pattern);
            Set(window, "_findRegex", true);
            Set(window, "_findCase", true);
            Set(window, "_findWord", word);
            var hits = new List<(int Start, int Length)>();
            window.CollectFindHits(plain, hits);
            return hits;
        }

        private static void Set(MainWindow window, string name, object value) =>
            typeof(MainWindow).GetField(name, Fields)!.SetValue(window, value);
    }

    /// <summary>
    /// F3 looks at other notes to find the next match. A pattern that times out on one of them
    /// says nothing about the open note, whose count line must not report "search too slow".
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class FindTimeoutTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [Fact]
        public void ATimeoutOnAnotherNoteDoesNotMarkTheOpenNoteSearchTooSlow() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long open = Note("Open", "aaa");
            Note("Slow", new string('a', 40) + "b");
            var window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            Set(window, "_findTerm", "(a+)+$");
            Set(window, "_findRegex", true);
            Set(window, "_findCase", true);
            Set(window, "_currentId", open);
            Set(window, "_notes", NoteStore.List(null, "custom"));

            Assert.Equal(-1, window.FindNextNoteIndex(1));

            Assert.False((bool)typeof(MainWindow).GetField("_findTimedOut", Fields)!.GetValue(window)!);
        });

        private static long Note(string title, string text)
        {
            var doc = new FlowDocument(new Paragraph(new Run(text)));
            using var bytes = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(bytes, DataFormats.XamlPackage);
            long id = NoteStore.Create(title);
            NoteStore.Save(id, title, bytes.ToArray(), text);
            return id;
        }

        private static void Set(MainWindow window, string name, object value) =>
            typeof(MainWindow).GetField(name, Fields)!.SetValue(window, value);
    }
}

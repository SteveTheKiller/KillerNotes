using System;
using System.Linq;
using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Version history (1.3.1): what the store keeps as a note is saved, and what restore does.
    /// The interval is a static knob, so each test pins it and puts it back.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class HistoryTests
    {
        private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        private static IDisposable Interval(TimeSpan t)
        {
            var was = NoteStore.HistoryInterval;
            NoteStore.HistoryInterval = t;
            return new Restore(() => NoteStore.HistoryInterval = was);
        }

        private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

        [Fact]
        public void TheFirstSaveKeepsNothingAndLaterSavesKeepTheTextTheyReplace()
        {
            using var _ = new TempStore();
            using var __ = Interval(TimeSpan.Zero);
            long id = NoteStore.Create("Draft");

            NoteStore.Save(id, "Draft", B("one"), "one");
            Assert.Empty(NoteStore.ListHistory(id));   // there was no text before this save

            NoteStore.Save(id, "Draft", B("two"), "two");
            var v = Assert.Single(NoteStore.ListHistory(id));
            Assert.Equal(B("one"), NoteStore.LoadVersion(v.Id)!.Value.Content);
            Assert.Equal("one", NoteStore.LoadVersion(v.Id)!.Value.Plain);
            Assert.Equal("Draft", v.Title);
        }

        [Fact]
        public void SavesInsideTheIntervalDoNotPileUpVersions()
        {
            using var _ = new TempStore();
            using var __ = Interval(TimeSpan.FromHours(1));
            long id = NoteStore.Create("Typing");
            NoteStore.Save(id, "Typing", B("a"), "a");
            NoteStore.Save(id, "Typing", B("ab"), "ab");    // keeps "a": nothing kept yet
            NoteStore.Save(id, "Typing", B("abc"), "abc");  // within the hour of "a": skipped
            NoteStore.Save(id, "Typing", B("abcd"), "abcd");

            var v = Assert.Single(NoteStore.ListHistory(id));
            Assert.Equal(B("a"), NoteStore.LoadVersion(v.Id)!.Value.Content);
        }

        [Fact]
        public void IdenticalTextIsNeverKeptTwice()
        {
            using var _ = new TempStore();
            using var __ = Interval(TimeSpan.Zero);
            long id = NoteStore.Create("Same");
            NoteStore.Save(id, "Same", B("x"), "x");
            NoteStore.Save(id, "Same", B("y"), "y");     // keeps x
            NoteStore.Save(id, "Same", B("y"), "y");     // y again: the newest version is y? no, x - so y is kept
            NoteStore.Save(id, "Same", B("y"), "y");     // newest is y now: skipped
            NoteStore.Save(id, "Same", B("y"), "y");
            var versions = NoteStore.ListHistory(id);
            Assert.Equal(2, versions.Count);
            Assert.Equal(B("y"), NoteStore.LoadVersion(versions[0].Id)!.Value.Content);
            Assert.Equal(B("x"), NoteStore.LoadVersion(versions[1].Id)!.Value.Content);
        }

        [Fact]
        public void OnlyTheNewestCapVersionsSurvive()
        {
            using var _ = new TempStore();
            using var __ = Interval(TimeSpan.Zero);
            long id = NoteStore.Create("Long");
            for (int i = 0; i <= NoteStore.HistoryCap + 5; i++)
            {
                NoteStore.Save(id, "Long", B("v" + i), "v" + i);
                // Distinct stamps, so the order is by time rather than by insertion accident.
                NoteStore.SetTimestamps(id, new DateTime(2026, 1, 1), new DateTime(2026, 1, 1).AddMinutes(i));
            }
            var versions = NoteStore.ListHistory(id);
            Assert.Equal(NoteStore.HistoryCap, versions.Count);
            Assert.Equal(B("v" + (NoteStore.HistoryCap + 4)), NoteStore.LoadVersion(versions[0].Id)!.Value.Content);
        }

        [Fact]
        public void RestoreSwapsTheTextInAndKeepsWhatItReplaced()
        {
            using var _ = new TempStore();
            using var __ = Interval(TimeSpan.Zero);
            long id = NoteStore.Create("Old title");
            NoteStore.Save(id, "Old title", B("first"), "first");
            NoteStore.Save(id, "New title", B("second"), "second");
            var first = NoteStore.ListHistory(id).Single();

            Assert.True(NoteStore.RestoreVersion(id, first.Id));

            Assert.Equal(B("first"), NoteStore.LoadContent(id));
            var row = NoteStore.List().Single();
            Assert.Equal("Old title", row.Title);
            // "second" went into the history before it was replaced, so the restore can be undone
            // from the same dialog.
            var versions = NoteStore.ListHistory(id);
            Assert.Equal(2, versions.Count);
            Assert.Equal(B("second"), NoteStore.LoadVersion(versions[0].Id)!.Value.Content);
        }

        [Fact]
        public void RestoreRefusesAnotherNotesVersion()
        {
            using var _ = new TempStore();
            using var __ = Interval(TimeSpan.Zero);
            long a = NoteStore.Create("A");
            long b = NoteStore.Create("B");
            NoteStore.Save(a, "A", B("a1"), "a1");
            NoteStore.Save(a, "A", B("a2"), "a2");
            NoteStore.Save(b, "B", B("b1"), "b1");
            var aVersion = NoteStore.ListHistory(a).Single();

            Assert.False(NoteStore.RestoreVersion(b, aVersion.Id));
            Assert.Equal(B("b1"), NoteStore.LoadContent(b));
            Assert.Empty(NoteStore.ListHistory(b));
        }

        [Fact]
        public void AConversionAndAReplaceAllKeepAVersionEvenInsideTheInterval()
        {
            using var _ = new TempStore();
            using var __ = Interval(TimeSpan.FromHours(1));
            long id = NoteStore.Create("Conv");
            NoteStore.Save(id, "Conv", B("rich"), "rich");
            NoteStore.SetFormat(id, 1, B("markdown"), "markdown");
            Assert.Single(NoteStore.ListHistory(id));

            NoteStore.UpdateContents([(id, B("replaced"), "replaced")]);
            var versions = NoteStore.ListHistory(id);
            Assert.Equal(2, versions.Count);
            Assert.Equal(B("markdown"), NoteStore.LoadVersion(versions[0].Id)!.Value.Content);
            Assert.Equal(1, versions[0].Format);
        }

        [Fact]
        public void DeleteForeverDropsTheHistoryWithTheNote()
        {
            using var _ = new TempStore();
            using var __ = Interval(TimeSpan.Zero);
            long id = NoteStore.Create("Gone");
            NoteStore.Save(id, "Gone", B("1"), "1");
            NoteStore.Save(id, "Gone", B("2"), "2");
            Assert.Single(NoteStore.ListHistory(id));

            NoteStore.Trash(id);
            Assert.Single(NoteStore.ListHistory(id));   // the trash keeps it
            NoteStore.DeleteForever(id);
            Assert.Empty(NoteStore.ListHistory(id));
        }
    }
}

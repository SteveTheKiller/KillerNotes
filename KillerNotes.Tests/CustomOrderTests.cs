using System.IO;
using System.Linq;
using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The storage half of reorder by drag and drop (#4, shipped 1.0.2).
    ///
    /// The drag gesture itself lives in Shell/Groups.DragDrop.cs and reads NotesList.Items, so the
    /// slot arithmetic cannot be reached without a UI. What CAN be pinned is everything underneath
    /// it, which is where a silent regression would actually land: the sort_order column, the
    /// "custom" sort key, and the two append rules that keep a hand-arranged list from being
    /// scrambled by a new or imported note.
    ///
    /// One rule here is load-bearing in a non-obvious way. SeedCustomOrderIfNeeded decides a
    /// database "was never ordered" by spotting DUPLICATE sort_order values, because a fresh
    /// column is all zeroes. If Create ever started handing out unique orders, that check would
    /// silently stop firing and the first drag would no longer preserve what was on screen. The
    /// two seed tests below pin both sides of that premise.
    ///
    /// No STA: none of this touches the WPF text stack.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class CustomOrderTests
    {
        private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

        private static long[] CustomOrderIds() =>
            NoteStore.List(null, "custom").Select(n => n.Id).ToArray();

        [Fact]
        public void NewNotesAppendToTheBottomOfTheCustomOrder()
        {
            using var store = new TempStore();
            long a = NoteStore.Create("a");
            long b = NoteStore.Create("b");
            long c = NoteStore.Create("c");

            Assert.Equal(new[] { a, b, c }, CustomOrderIds());
        }

        [Fact]
        public void SetNoteOrdersRearrangesTheWholeSequence()
        {
            using var store = new TempStore();
            long a = NoteStore.Create("a");
            long b = NoteStore.Create("b");
            long c = NoteStore.Create("c");

            // What a drag of c to the top produces: the full list renumbered from 1.
            NoteStore.SetNoteOrders(new[] { (c, 1), (a, 2), (b, 3) });

            Assert.Equal(new[] { c, a, b }, CustomOrderIds());
        }

        [Fact]
        public void ACustomArrangementSurvivesClosingAndReopening()
        {
            using var store = new TempStore();
            long a = NoteStore.Create("a");
            long b = NoteStore.Create("b");
            NoteStore.SetNoteOrders(new[] { (b, 1), (a, 2) });

            NoteStore.Close();
            NoteStore.Open(null);

            Assert.Equal(new[] { b, a }, CustomOrderIds());
        }

        /// <summary>
        /// Why the drag path has to FORCE its seed, written down so nobody removes the force again.
        ///
        /// SeedCustomOrderIfNeeded detects "never arranged by hand" by looking for duplicate
        /// sort_order values, on the reasoning that the column defaults to 0. But Create assigns
        /// MAX+1, so notes made since 1.0.2 are already unique and that check never fires for
        /// them. Unforced, the drag path would therefore skip the seed and drop the note relative
        /// to an order the user cannot see. This test is the fact that makes the guard useless
        /// there; if it ever fails, the drag path's force is no longer load-bearing and the
        /// reasoning in Groups.DragDrop.cs needs revisiting.
        /// </summary>
        [Fact]
        public void CreateHandsOutUniqueOrdersSoTheDuplicateGuardCannotSpotAFreshDatabase()
        {
            using var store = new TempStore();
            NoteStore.Create("a");
            NoteStore.Create("b");
            NoteStore.Create("c");

            var orders = NoteStore.List(null, "custom").Select(n => n.SortOrder).ToList();
            Assert.Equal(orders.Count, orders.Distinct().Count());
        }

        /// <summary>The button path's guard still has something to protect: a saved arrangement
        /// has unique orders, so rounding back to custom through another sort leaves it alone.</summary>
        [Fact]
        public void ASavedArrangementHasUniqueOrdersSoTheSortButtonLeavesItAlone()
        {
            using var store = new TempStore();
            long a = NoteStore.Create("a");
            long b = NoteStore.Create("b");
            NoteStore.SetNoteOrders(new[] { (b, 1), (a, 2) });

            var orders = NoteStore.List(null, "custom").Select(n => n.SortOrder).ToList();
            Assert.Equal(orders.Count, orders.Distinct().Count());
            Assert.Equal(new[] { b, a }, CustomOrderIds());
        }

        /// <summary>The seed itself: renumbering from an arbitrary on-screen arrangement makes
        /// that arrangement the stored one. This is what the drag path does before positioning a
        /// drop, and it must work regardless of what the orders were beforehand.</summary>
        [Fact]
        public void SeedingFromAVisibleArrangementMakesItTheStoredOrder()
        {
            using var store = new TempStore();
            long zeb = NoteStore.Create("zeb");
            long ant = NoteStore.Create("ant");
            long mid = NoteStore.Create("mid");

            // What the user sees in A-Z sort, which the drag path seeds from.
            var onScreen = NoteStore.List(null, "title-asc").Select(n => n.Id).ToArray();
            Assert.Equal(new[] { ant, mid, zeb }, onScreen);

            NoteStore.SetNoteOrders(onScreen.Select((id, i) => (id, i + 1)));

            Assert.Equal(onScreen, CustomOrderIds());
        }

        /// <summary>An imported note must land at the bottom. If it arrived at 0 it would jump to
        /// the top of a hand-arranged list, which is exactly the annoyance #4 was asking to fix.</summary>
        [Fact]
        public void AnImportedNoteAppendsInsteadOfLandingAtTheTop()
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (new TempStore())
                {
                    long id = NoteStore.Create("incoming");
                    NoteStore.Save(id, "incoming", Utf8("<blob>"), "incoming");
                    NoteStore.ExportNote(id, knote, password: null);
                }

                using (new TempStore())
                {
                    long a = NoteStore.Create("a");
                    long b = NoteStore.Create("b");
                    NoteStore.SetNoteOrders(new[] { (a, 1), (b, 2) });

                    Assert.Equal(1, NoteStore.ImportNotes(knote, password: null));

                    var ids = CustomOrderIds();
                    Assert.Equal(3, ids.Length);
                    Assert.Equal(new[] { a, b }, ids.Take(2).ToArray());
                    Assert.DoesNotContain(ids[2], new[] { a, b });
                }
            }
            finally { try { File.Delete(knote); } catch { /* best effort */ } }
        }

        /// <summary>Ties break by id, so an unordered database still has one stable arrangement
        /// rather than whatever SQLite feels like returning.</summary>
        [Fact]
        public void EqualOrdersBreakTheTieByIdSoTheListIsStable()
        {
            using var store = new TempStore();
            long a = NoteStore.Create("a");
            long b = NoteStore.Create("b");
            NoteStore.SetNoteOrders(new[] { (a, 5), (b, 5) });

            Assert.Equal(new[] { a, b }, CustomOrderIds());
        }
    }
}

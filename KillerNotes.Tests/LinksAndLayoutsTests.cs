using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The 1.3.0 storage surface: the note_links cache, the graph_layouts table, and the
    /// snapshot machinery (CaptureRow / UpdateContents / RestoreContents) that replace-all and
    /// rename propagation both stand on. All of it against a real file through the store's own
    /// API, because these are the paths a release must not be able to corrupt.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class LinksAndLayoutsTests : IDisposable
    {
        private readonly TempStore _store = new();

        public void Dispose() => _store.Dispose();

        // ── note_links ───────────────────────────────────────────────────────────────────

        [Fact]
        public void SetLinksReplacesWholesale()
        {
            long a = NoteStore.Create("A");
            NoteStore.Create("B");

            NoteStore.SetLinks(a, new[] { "B", "Ghost" });
            Assert.Equal(a, Assert.Single(NoteStore.Backlinks("B")).Id);
            Assert.Empty(NoteStore.Backlinks("C"));

            // The table is a cache of what the note SAYS NOW. Removing a link from the text and
            // re-deriving must remove the edge, not merge it with history.
            NoteStore.SetLinks(a, new[] { "C" });
            Assert.Empty(NoteStore.Backlinks("B"));
            Assert.Single(NoteStore.Backlinks("C"));
        }

        [Fact]
        public void BacklinksMatchTitlesCaseInsensitively()
        {
            long src = NoteStore.Create("Runbook");
            NoteStore.SetLinks(src, new[] { "Server Rack" });
            // A link written [[server rack]] still counts for the note titled "Server Rack",
            // because that is how the editor resolves it on Ctrl+Click.
            Assert.Single(NoteStore.Backlinks("SERVER RACK"));
        }

        [Fact]
        public void ATitleNobodyOwnsStillHasBacklinks()
        {
            // The rename propagation contract, and the bug that nearly shipped: after a rename
            // the OLD title belongs to no note, and the self-exclusion in the query compared
            // n.id against a NULL subquery - never true - so every backlink vanished exactly
            // when the rename dialog needed them. A ghost title's linkers hit the same hole.
            long target = NoteStore.Create("Old Title");
            long linker = NoteStore.Create("Runbook");
            NoteStore.SetLinks(linker, new[] { "Old Title" });

            NoteStore.Save(target, "New Title", Encoding.UTF8.GetBytes("x"), "renamed");

            var back = NoteStore.Backlinks("Old Title");
            Assert.Single(back);
            Assert.Equal(linker, back[0].Id);
        }

        [Fact]
        public void UnlinkedMentionsExcludeLinkersAndTheNoteItself()
        {
            long target = NoteStore.Create("Firewall");
            NoteStore.Save(target, "Firewall", Encoding.UTF8.GetBytes("x"), "the Firewall note itself");

            long namer = NoteStore.Create("Day note");
            NoteStore.Save(namer, "Day note", Encoding.UTF8.GetBytes("x"), "touched the Firewall today");

            long linker = NoteStore.Create("Network map");
            NoteStore.Save(linker, "Network map", Encoding.UTF8.GetBytes("x"), "see [[Firewall]] for rules");
            NoteStore.SetLinks(linker, new[] { "Firewall" });

            var mentions = NoteStore.UnlinkedMentions("Firewall");
            // The namer shows. The linker is excluded because its connection already exists as an
            // edge, and the target is excluded because a note is not a mention of itself.
            Assert.Single(mentions);
            Assert.Equal(namer, mentions[0].Id);
        }

        // ── graph_layouts ────────────────────────────────────────────────────────────────

        [Fact]
        public void ALayoutRoundTripsPositionsAndLocks()
        {
            long a = NoteStore.Create("A");
            long b = NoteStore.Create("B");

            NoteStore.SaveGraphLayout("desk", new[]
            {
                new NoteStore.GraphNodePos(a, 12.5, -3.25, locked: true),
                new NoteStore.GraphNodePos(b, 400.0, 220.75, locked: false),
            });

            var rows = NoteStore.LoadGraphLayout("desk").OrderBy(r => r.NoteId).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(12.5, rows[0].X);
            Assert.Equal(-3.25, rows[0].Y);
            Assert.True(rows[0].Locked);
            Assert.False(rows[1].Locked);
        }

        [Fact]
        public void SavingANameAgainReplacesItsRows()
        {
            long a = NoteStore.Create("A");
            long b = NoteStore.Create("B");

            NoteStore.SaveGraphLayout("desk", new[] { new NoteStore.GraphNodePos(a, 1, 1, false),
                                                      new NoteStore.GraphNodePos(b, 2, 2, false) });
            // The second save is a SNAPSHOT of one node. If the other survived, applying the
            // layout later would mix two arrangements.
            NoteStore.SaveGraphLayout("desk", new[] { new NoteStore.GraphNodePos(a, 9, 9, true) });

            var rows = NoteStore.LoadGraphLayout("desk");
            Assert.Single(rows);
            Assert.Equal(9, rows[0].X);
            Assert.True(rows[0].Locked);
        }

        [Fact]
        public void TheRememberedLayoutIsNeverListedAsAChoice()
        {
            long a = NoteStore.Create("A");
            NoteStore.SaveGraphLayout("", new[] { new NoteStore.GraphNodePos(a, 1, 1, false) });
            NoteStore.SaveGraphLayout("desk", new[] { new NoteStore.GraphNodePos(a, 2, 2, false) });

            // "" is the layout the window was last left in, not something the user saved, so the
            // Arrange menu must never offer it - and it still loads by its own name.
            Assert.Equal(new[] { "desk" }, NoteStore.ListGraphLayouts());
            Assert.Single(NoteStore.LoadGraphLayout(""));
        }

        [Fact]
        public void ADeletedNoteVanishesFromLoadedLayouts()
        {
            long a = NoteStore.Create("A");
            long b = NoteStore.Create("B");
            NoteStore.SaveGraphLayout("desk", new[] { new NoteStore.GraphNodePos(a, 1, 1, false),
                                                      new NoteStore.GraphNodePos(b, 2, 2, false) });

            NoteStore.Delete(b);

            // The join hides the dead row immediately, so a stale layout cannot resurrect a
            // deleted note as a node. The row itself is swept on the next save, and the sweep
            // deliberately does NOT run at delete time - delete is undoable.
            Assert.Single(NoteStore.LoadGraphLayout("desk"));
            NoteStore.SaveGraphLayout("other", new[] { new NoteStore.GraphNodePos(a, 3, 3, false) });
            Assert.Single(NoteStore.LoadGraphLayout("desk"));
        }

        [Fact]
        public void LayoutNamesCollideCaseInsensitively()
        {
            long a = NoteStore.Create("A");
            NoteStore.SaveGraphLayout("Desk", new[] { new NoteStore.GraphNodePos(a, 1, 1, false) });
            // The overwrite prompt keys off this: saving "desk" would land on "Desk"'s rows, so
            // Exists must say so however the user cased it.
            Assert.True(NoteStore.GraphLayoutExists("desk"));
            NoteStore.DeleteGraphLayout("DESK");
            Assert.Empty(NoteStore.LoadGraphLayout("Desk"));
        }

        [Fact]
        public void AKnoteCarriesNoLayoutsAndNoLinks()
        {
            long a = NoteStore.Create("A");
            NoteStore.Save(a, "A", Encoding.UTF8.GetBytes("x"), "links to [[B]]");
            NoteStore.SetLinks(a, new[] { "B" });
            NoteStore.SaveGraphLayout("", new[] { new NoteStore.GraphNodePos(a, 1, 1, true) });

            string dest = Path.Combine(Path.GetTempPath(), "KillerNotesTests",
                                       Guid.NewGuid().ToString("N") + ".knote");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try
            {
                NoteStore.ExportNote(a, dest, password: null);

                using var db = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = dest }.ConnectionString);
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                var tables = new List<string>();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) tables.Add(r.GetString(0));

                // Coordinates mean nothing without the notebook they belong to, so the layout
                // table must not exist in a shared note at all. note_links DOES exist (it is in
                // the shared schema) but travels empty: the receiver re-derives links from the
                // text on the first save, and rows for notes it never had would be wrong.
                Assert.DoesNotContain("graph_layouts", tables);
                Assert.Contains("note_links", tables);
                using var count = db.CreateCommand();
                count.CommandText = "SELECT count(*) FROM note_links";
                Assert.Equal(0L, (long)count.ExecuteScalar()!);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(dest); } catch { /* best effort */ }
            }
        }

        // ── the snapshot machinery under replace-all and rename propagation ──────────────

        [Fact]
        public void CaptureUpdateRestoreIsALosslessRoundTrip()
        {
            long id = NoteStore.Create("Doc");
            byte[] original = Encoding.UTF8.GetBytes("original bytes");
            NoteStore.Save(id, "Doc", original, "original plain");

            var before = NoteStore.CaptureRow(id);
            Assert.NotNull(before);

            NoteStore.UpdateContents(new[] { (id, Encoding.UTF8.GetBytes("rewritten"), "rewritten plain") });
            Assert.Equal(Encoding.UTF8.GetBytes("rewritten"), NoteStore.LoadContent(id));

            // Undo hands back the captured rows. Content AND the modified stamp must return,
            // or an undone rename leaves every touched note pretending it was just edited.
            NoteStore.RestoreContents(new[] { before! });
            Assert.Equal(original, NoteStore.LoadContent(id));
            var after = NoteStore.CaptureRow(id);
            Assert.Equal(before!.Modified, after!.Modified);
            Assert.Equal(before.Plain, after.Plain);
        }
    }

    /// <summary>
    /// The wikilink parser on its own. No store, no collection: pure string in, targets out.
    /// These pin the exact rules the rename propagation and the graph both assume.
    /// </summary>
    public sealed class WikiLinkParseTests
    {
        [Fact]
        public void TargetsComeBackTrimmedDistinctAndInOrder()
        {
            var found = WikiLinks.Parse("see [[ Alpha ]] then [[Beta]] then [[alpha]] again");
            // Alpha appears twice in two casings and is ONE target: the graph and the backlinks
            // list both count notes, not mentions. First appearance sets the order.
            Assert.Equal(new[] { "Alpha", "Beta" }, found);
        }

        [Fact]
        public void ALabelledLinkIndexesTheTargetNotTheLabel()
        {
            Assert.Equal(new[] { "Real Note" }, WikiLinks.Parse("read [[Real Note|the good part]]"));
        }

        [Fact]
        public void ALinkNeverCrossesALineEnding()
        {
            // An unclosed [[ must not swallow the rest of the note hunting for its partner.
            Assert.Empty(WikiLinks.Parse("broken [[half\nof a link]]"));
        }

        [Fact]
        public void NeighboringLinksStaySeparate()
        {
            Assert.Equal(new[] { "a", "b" }, WikiLinks.Parse("[[a]] and [[b]]"));
        }

        [Fact]
        public void EmptyBracketsAreNotALink()
        {
            Assert.Empty(WikiLinks.Parse("nothing here [[]] or [[  ]]"));
        }

        [Fact]
        public void WrapProducesWhatParseAccepts()
        {
            // The autocomplete and the mention chip both write links with Wrap; if Parse ever
            // refused its output, a link the app itself created would not count as one.
            string wrapped = WikiLinks.Wrap("  My Note  ");
            Assert.Equal("[[My Note]]", wrapped);
            Assert.Equal(new[] { "My Note" }, WikiLinks.Parse(wrapped));
        }
    }
}

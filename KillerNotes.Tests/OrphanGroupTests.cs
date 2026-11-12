using System;
using System.IO;
using System.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A note can carry a group path that has no row in the groups table (imported from another
    /// notepad, or written by an older build). The sidebar draws those paths as groups, but every
    /// Group menu lists the groups table, so they were missing from the note card, New note in
    /// group, Quick note and the search filters. Opening the notepad now gives each such path a
    /// real group row, its parents included, so every list matches the sidebar.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class OrphanGroupTests : IDisposable
    {
        private readonly string _dir;

        public OrphanGroupTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            NoteStore.Close();
            NoteStore.DbDirOverride = _dir;
            NoteStore.DemoDbFile = "orphans.db";
            NoteStore.Open();
        }

        public void Dispose()
        {
            NoteStore.Close();
            NoteStore.DbDirOverride = null;
            NoteStore.DemoDbFile = null;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void AGroupThatExistsOnlyOnNotesGetsARowWithItsParents()
        {
            string sep = NoteStore.GroupSep;
            NoteStore.AddGroup("App Ideas");
            long a = NoteStore.Create("UPS runtimes");
            long b = NoteStore.Create("Chrome extension");
            NoteStore.SetNoteGroup(a, "App Ideas" + sep + "Hardware");
            NoteStore.SetNoteGroup(b, "Killer Apps" + sep + "Browser");

            NoteStore.Close();
            NoteStore.Open();

            var tree = NoteStore.ListGroupTree();
            Assert.Contains(tree, g => g.Path == "App Ideas" + sep + "Hardware" && g.Parent == "App Ideas");
            Assert.Contains(tree, g => g.Path == "Killer Apps" && g.Parent == "");
            Assert.Contains(tree, g => g.Path == "Killer Apps" + sep + "Browser" && g.Parent == "Killer Apps");
            Assert.Single(tree, g => g.Path == "App Ideas");
        }
    }
}

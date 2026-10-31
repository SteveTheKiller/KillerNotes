using System;
using System.IO;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    // A database is known by an id stored inside it, not by its name: held edits and tools follow
    // the file through a rename or a move, and never land in a stranger that took its name.
    [Collection(NoteStoreCollection.Name)]
    public class DatabaseIdentityTests
    {
        [Fact]
        public void ADatabaseKeepsItsIdAcrossReopens()
        {
            using var store = new TempStore();
            string? id = NoteStore.DatabaseId;
            Assert.False(string.IsNullOrEmpty(id));

            NoteStore.Close();
            Assert.Null(NoteStore.DatabaseId);
            NoteStore.Open();

            Assert.Equal(id, NoteStore.DatabaseId);
        }

        [Fact]
        public void RenamingOrMovingTheFileKeepsTheId()
        {
            using var store = new TempStore();
            string? id = NoteStore.DatabaseId;
            string moved = Path.Combine(NoteStore.DbDir, "elsewhere");
            Directory.CreateDirectory(moved);
            NoteStore.Close();
            File.Move(store.DbPath, Path.Combine(moved, "renamed.db"));

            NoteStore.DbDirOverride = moved;
            NoteStore.DemoDbFile = "renamed.db";
            NoteStore.Open();

            Assert.Equal(id, NoteStore.DatabaseId);
        }

        [Fact]
        public void AnotherFileUnderTheSameNameIsAnotherDatabase()
        {
            using var store = new TempStore();
            string? id = NoteStore.DatabaseId;
            string other = Path.Combine(NoteStore.DbDir, "other-folder");
            Directory.CreateDirectory(other);
            NoteStore.Close();

            NoteStore.DbDirOverride = other;
            NoteStore.Open();

            Assert.Equal("test.db", NoteStore.ActiveDbFile);
            Assert.NotEqual(id, NoteStore.DatabaseId);
        }

        [Fact]
        public void ReplacingTheFileAtThePathIsAnotherDatabase()
        {
            using var store = new TempStore();
            string? id = NoteStore.DatabaseId;
            string path = store.DbPath;
            NoteStore.Close();
            File.Delete(path);

            NoteStore.Open();

            Assert.NotEqual(id, NoteStore.DatabaseId);
        }

        // A copy made before a password went on must not answer to the protected file's id:
        // opening that plain copy would otherwise hand over edits held for the protected one.
        [Fact]
        public void ChangingThePasswordGivesTheFileANewId()
        {
            using var store = new TempStore();
            string? before = NoteStore.DatabaseId;

            NoteStore.SetPassword("hunter2");

            Assert.False(string.IsNullOrEmpty(NoteStore.DatabaseId));
            Assert.NotEqual(before, NoteStore.DatabaseId);
            string? after = NoteStore.DatabaseId;
            NoteStore.Close();
            NoteStore.Open("hunter2");
            Assert.Equal(after, NoteStore.DatabaseId);
        }
    }
}

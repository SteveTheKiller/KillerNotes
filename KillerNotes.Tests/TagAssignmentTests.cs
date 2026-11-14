using System;
using System.IO;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class TagAssignmentTests
    {
        [Fact]
        public void ReadOnlyAssignmentLeavesTheDisplayedNoteAndStoredTagsUnchanged()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Read-only note");
            var note = NoteStore.GetNote(id)!;
            using var readOnly = new HeldReadOnlyStore();
            Assert.True(NoteStore.IsReadOnly, "The real writer reservation must force a read-only connection.");

            TagManager.SetAssigned(note, "Priority", true);

            Assert.Equal("", note.Tags);
            Assert.Empty(note.Chips);
            Assert.Equal("", NoteStore.GetNote(id)!.Tags);
        }

        internal sealed class HeldReadOnlyStore : IDisposable
        {
            private readonly string _directory = NoteStore.DbDir;
            private readonly FileStream _reservation;

            public HeldReadOnlyStore()
            {
                string path = NoteStore.DbPath;
                NoteStore.Close();
                _reservation = new FileStream(path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                try
                {
                    // Extended local paths use the store's network reservation path without
                    // depending on SMB, a mapped drive or a second machine.
                    NoteStore.DbDirOverride = @"\\?\" + _directory;
                    NoteStore.Open();
                }
                catch
                {
                    _reservation.Dispose();
                    NoteStore.DbDirOverride = _directory;
                    throw;
                }
            }

            public void Dispose()
            {
                NoteStore.Close();
                _reservation.Dispose();
                NoteStore.DbDirOverride = _directory;
            }
        }
    }
}

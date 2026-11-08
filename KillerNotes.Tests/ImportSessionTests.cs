using System;
using System.IO;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class ImportSessionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void APreparedImportCannotWriteIntoAnotherOpenSession(bool samePath)
        {
            using var store = new TempStore();
            // The actual connection is an opaque ownership token, never used by the importer.
            object session = NoteStore.OpenSession!;
            string folder = NoteStore.DbDir;
            NoteStore.Close();
            if (!samePath) NoteStore.DbDirOverride = Path.Combine(folder, "other");
            NoteStore.Open();
            var prepared = new PreparedImport { Title = "Private", Plain = "private body",
                Created = DateTime.Now, Modified = DateTime.Now, SourceKey = "source", SourceHash = "hash" };

            Assert.Throws<InvalidOperationException>(() => NoteStore.ImportBatch(new[] { prepared }, session));

            Assert.Empty(NoteStore.List());
        }
    }
}

using System;
using System.IO;
using System.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// An export that fails must leave whatever was already at the destination exactly as it was,
    /// and must not leave a half-written file behind under the real name or beside it.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ExportAtomicityTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kn-export-" + Guid.NewGuid().ToString("N"));

        public ExportAtomicityTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void AFailedShareExportKeepsThePreviousFile()
        {
            using var store = new TempStore();
            string dest = Path.Combine(_dir, "shared.knote");
            File.WriteAllBytes(dest, new byte[] { 1, 2, 3 });

            Assert.ThrowsAny<Exception>(() => NoteStore.ExportNote(424242, dest, password: null));

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dest));
            Assert.Single(Directory.GetFiles(_dir));
        }

        [Fact]
        public void ASuccessfulShareExportReplacesThePreviousFile()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("fresh");
            string dest = Path.Combine(_dir, "shared.knote");
            File.WriteAllBytes(dest, new byte[] { 1, 2, 3 });

            NoteStore.ExportNote(id, dest, password: null);

            Assert.NotEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dest));
            Assert.Single(Directory.GetFiles(_dir));
        }

        [Fact]
        public void ExportingOntoTheOpenDatabaseIsRefused()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("n");
            long before = new FileInfo(store.DbPath).Length;
            Assert.Throws<IOException>(() => NoteStore.ExportNote(id, store.DbPath, password: null));
            Assert.Single(NoteStore.List());
            Assert.Equal(before, new FileInfo(store.DbPath).Length);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AWriterThatFailsPartwayLeavesNothingBehind(bool existing)
        {
            string dest = Path.Combine(_dir, "note.html");
            if (existing) File.WriteAllText(dest, "previous");

            Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(dest, temp =>
            {
                File.WriteAllText(temp, "half");
                throw new InvalidOperationException("disk full");
            }));

            if (existing) Assert.Equal("previous", File.ReadAllText(dest));
            else Assert.False(File.Exists(dest));
            Assert.Equal(existing ? 1 : 0, Directory.GetFiles(_dir).Length);
        }

        [Fact]
        public void AWriterThatFinishesPublishesItsFile()
        {
            string dest = Path.Combine(_dir, "note.txt");
            File.WriteAllText(dest, "previous");
            AtomicFile.Write(dest, temp => File.WriteAllText(temp, "new"));
            Assert.Equal("new", File.ReadAllText(dest));
            Assert.Equal(new[] { dest }, Directory.GetFiles(_dir));
        }
    }
}

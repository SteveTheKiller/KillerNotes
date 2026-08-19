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
    /// Points NoteStore at a throwaway folder for one test, and restores everything on
    /// Dispose. NoteStore is static global state, so these tests must not run against the
    /// real data folder and must clean up after themselves; xunit runs the facts within a
    /// single class sequentially, which is what keeps the static store race-free.
    /// </summary>
    internal sealed class TempStore : IDisposable
    {
        private readonly string _dir;

        public TempStore(string? password = null)
        {
            _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            NoteStore.DbDirOverride = _dir;
            NoteStore.DemoDbFile = "test.db";
            NoteStore.Open(password);
        }

        public string DbPath => NoteStore.DbPath;

        public void Dispose()
        {
            NoteStore.Close();
            NoteStore.DbDirOverride = null;
            NoteStore.DemoDbFile = null;
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }
    }

    public sealed class NoteStoreTests
    {
        private static readonly byte[] Blob = Encoding.UTF8.GetBytes("not a real XamlPackage, just bytes");

        // ---- CRUD / listing ----

        [Fact]
        public void CreateSaveAndLoadRoundTrip()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("First note");
            NoteStore.Save(id, "First note", Blob, "line one\nline two");

            Assert.Equal(Blob, NoteStore.LoadContent(id));
            var row = Assert.Single(NoteStore.List());
            Assert.Equal(id, row.Id);
            Assert.Equal("First note", row.Title);
            Assert.Equal("line one", row.Snippet);   // snippet is the FIRST line of plain
        }

        [Fact]
        public void ListNewestFirstBreaksCreatedTiesById()
        {
            using var _ = new TempStore();
            long a = NoteStore.Create("A");
            long b = NoteStore.Create("B");
            // Same stored timestamp: without the id tie-break a brand-new note could sort
            // below an older tied row in newest-first mode.
            var ts = new DateTime(2026, 8, 19, 12, 0, 0);
            NoteStore.SetTimestamps(a, ts, ts);
            NoteStore.SetTimestamps(b, ts, ts);

            var rows = NoteStore.List(sort: "created-desc");
            Assert.Equal(new[] { b, a }, rows.Select(n => n.Id).ToArray());
        }

        [Fact]
        public void DeleteRemovesTheRow()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Doomed");
            NoteStore.Delete(id);
            Assert.Empty(NoteStore.List());
        }

        // ---- Search (#12: substring, mid-token) ----

        [Fact]
        public void SearchMatchesMidTokenSubstrings()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Ticket");
            NoteStore.Save(id, "Ticket", Blob, "signature A/002/45 in the body");

            // FTS5 prefix matching only hit whole tokens from their start, so "02" never
            // found "A/002/45". The LIKE scan must.
            Assert.Single(NoteStore.List(search: "02"));
            Assert.Single(NoteStore.List(search: "2"));
            Assert.Empty(NoteStore.List(search: "zzz"));
        }

        [Fact]
        public void SearchRequiresEveryWordAcrossColumns()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Grocery run");
            NoteStore.Save(id, "Grocery run", Blob, "milk and eggs");

            Assert.Single(NoteStore.List(search: "grocery milk"));   // title word + body word
            Assert.Empty(NoteStore.List(search: "grocery bread"));   // one word missing = no match
        }

        [Fact]
        public void SearchTreatsLikeWildcardsAsLiterals()
        {
            using var _ = new TempStore();
            long plain = NoteStore.Create("Plain");
            NoteStore.Save(plain, "Plain", Blob, "nothing special");
            long pct = NoteStore.Create("Progress");
            NoteStore.Save(pct, "Progress", Blob, "50% done");

            // An unescaped % would match every row.
            var hits = NoteStore.List(search: "%");
            Assert.Equal(pct, Assert.Single(hits).Id);
        }

        // ---- Sketch payloads ----

        [Fact]
        public void SketchesRoundTripByOrdinal()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Sketchy");
            var payload = new Dictionary<int, byte[]>
            {
                [0] = new byte[] { 1, 2, 3 },
                [2] = new byte[] { 9, 8, 7, 6 },
            };
            NoteStore.SaveSketches(id, payload);

            var loaded = NoteStore.LoadSketches(id);
            Assert.Equal(2, loaded.Count);
            Assert.Equal(payload[0], loaded[0]);
            Assert.Equal(payload[2], loaded[2]);

            // Rewritten whole on every save: a smaller set replaces the old rows.
            NoteStore.SaveSketches(id, new Dictionary<int, byte[]> { [0] = new byte[] { 5 } });
            Assert.Single(NoteStore.LoadSketches(id));
        }

        // ---- SQLCipher: open / key check ----

        [Fact]
        public void PlaintextFileProbesAsNotEncrypted()
        {
            using var store = new TempStore();
            NoteStore.Create("anything");
            NoteStore.Close();
            Assert.False(NoteStore.IsEncryptedFile(store.DbPath));
        }

        [Fact]
        public void WrongPasswordThrowsInsteadOfReadingPlaintext()
        {
            // Regression for the pooling trap: a connection keyed with a raw PRAGMA after
            // Open() could come back from the pool already keyed and silently ignore a
            // wrong password. The key lives in the connection string and Open() forces a
            // sqlite_master read, so a wrong key must throw right there.
            using var store = new TempStore(password: "correct horse");
            long id = NoteStore.Create("secret");
            NoteStore.Save(id, "secret", Blob, "battery staple");
            NoteStore.Close();

            Assert.True(NoteStore.IsEncryptedFile(store.DbPath));
            Assert.Throws<SqliteException>(() => NoteStore.Open("wrong password"));
            Assert.False(NoteStore.IsOpen);   // a failed open must not report IsOpen

            NoteStore.Open("correct horse");
            Assert.Equal("secret", Assert.Single(NoteStore.List()).Title);
        }

        // ---- SQLCipher: SetPassword (sqlcipher_export + file swap, issue #3) ----

        [Fact]
        public void SetPasswordEncryptsAPlaintextDatabase()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("keep me");
            NoteStore.Save(id, "keep me", Blob, "body");

            NoteStore.SetPassword("hunter2");
            Assert.True(NoteStore.IsOpen);
            Assert.True(NoteStore.HasPassword);
            Assert.Equal("keep me", Assert.Single(NoteStore.List()).Title);   // data survived the rewrite
            Assert.Equal(Blob, NoteStore.LoadContent(id));

            NoteStore.Close();
            Assert.True(NoteStore.IsEncryptedFile(store.DbPath));
            Assert.False(File.Exists(store.DbPath + ".rekey"));   // no debris from the swap
            Assert.False(File.Exists(store.DbPath + ".bak"));
        }

        [Fact]
        public void SetPasswordChangesAnExistingKey()
        {
            using var store = new TempStore(password: "old key");
            NoteStore.Create("survivor");

            NoteStore.SetPassword("new key");
            NoteStore.Close();

            Assert.Throws<SqliteException>(() => NoteStore.Open("old key"));
            NoteStore.Open("new key");
            Assert.Equal("survivor", Assert.Single(NoteStore.List()).Title);
        }

        [Fact]
        public void SetPasswordNullDecryptsBackToPlaintext()
        {
            using var store = new TempStore(password: "temporary");
            NoteStore.Create("freed");

            NoteStore.SetPassword(null);
            Assert.False(NoteStore.HasPassword);
            NoteStore.Close();

            Assert.False(NoteStore.IsEncryptedFile(store.DbPath));
            NoteStore.Open();   // no key needed
            Assert.Equal("freed", Assert.Single(NoteStore.List()).Title);
        }

        [Fact]
        public void CloseReleasesTheFileForSwapping()
        {
            // Dispose() alone does not release the pooled handle; Close() must also clear
            // the pools or the password-change file swap hits a sharing violation.
            using var store = new TempStore();
            NoteStore.Create("mobile");
            NoteStore.Close();

            string moved = store.DbPath + ".moved";
            File.Move(store.DbPath, moved);   // throws IOException if a handle is still held
            File.Move(moved, store.DbPath);
        }
    }
}

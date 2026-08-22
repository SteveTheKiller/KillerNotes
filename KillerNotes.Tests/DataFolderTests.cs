using System;
using System.IO;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Where the databases live (#6, shipped 1.0.2).
    ///
    /// Only the resolution RULE is tested, through NoteStore.ResolveDbDir. The live DbDir reads
    /// the "DataFolder" setting out of HKCU, and a test that wrote there would be editing the
    /// real setting on the machine running it: a crash between set and restore would leave the
    /// app looking for notes in a folder that does not exist and quietly creating a fresh
    /// database there. Not worth it to cover a two-branch expression.
    ///
    /// The Move / "Leave them" file shuffling in DatabasesDialog is UI and is not covered here.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class DataFolderTests
    {
        [Fact]
        public void AChosenFolderWins()
        {
            string chosen = Path.Combine(Path.GetTempPath(), "KillerNotesDataFolderTest");
            Assert.Equal(chosen, NoteStore.ResolveDbDir(chosen));
        }

        [Fact]
        public void NoSettingFallsBackToTheStockFolder()
        {
            Assert.Equal(NoteStore.DefaultDbDir, NoteStore.ResolveDbDir(null));
            Assert.Equal(NoteStore.DefaultDbDir, NoteStore.ResolveDbDir(""));
        }

        /// <summary>Whitespace is the interesting one: a setting that was cleared badly, or a
        /// path the user blanked in a text field, must not resolve to a folder named " ".</summary>
        [Fact]
        public void AWhitespaceSettingFallsBackRatherThanBecomingAFolderName()
        {
            Assert.Equal(NoteStore.DefaultDbDir, NoteStore.ResolveDbDir("   "));
            Assert.Equal(NoteStore.DefaultDbDir, NoteStore.ResolveDbDir("\t"));
        }

        [Fact]
        public void TheStockFolderIsUnderApplicationData()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.Equal(Path.Combine(appData, "KillerNotes"), NoteStore.DefaultDbDir);
        }

        /// <summary>DbPath is the resolved folder plus the active file, which is what every open,
        /// lock and password swap actually operates on. Driven through the test seam so the real
        /// setting is never involved.</summary>
        [Fact]
        public void DbPathCombinesTheResolvedFolderWithTheActiveFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "KillerNotesDbPathTest");
            string? saved = NoteStore.DbDirOverride;
            try
            {
                NoteStore.DbDirOverride = dir;
                Assert.Equal(dir, NoteStore.DbDir);
                Assert.Equal(Path.Combine(dir, NoteStore.ActiveDbFile), NoteStore.DbPath);
            }
            finally { NoteStore.DbDirOverride = saved; }
        }
    }
}

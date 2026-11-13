using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class CopyIntegrityTests
    {
        [Fact]
        public void PasswordChangesKeepIdsReservedForUndo()
        {
            using var store = new TempStore();
            NoteStore.Create("source");
            long retired = NoteStore.Create("copy");
            NoteStore.DeleteForever(retired);

            NoteStore.SetPassword("test-password");

            long replacement = NoteStore.Create("replacement");
            Assert.True(replacement > retired);
            NoteStore.DeleteForever(retired);
            Assert.NotNull(NoteStore.GetNote(replacement));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ImportedNotesReserveFreshIdsForTheirWholeSession(bool sharedFile)
        {
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".knote");
            try
            {
                using (new TempStore())
                {
                    long source = NoteStore.Create("imported");
                    NoteStore.ExportNote(source, file, null);
                }
                using (new TempStore())
                {
                    long retired = NoteStore.Create("retired");
                    NoteStore.DeleteForever(retired);
                    if (sharedFile) Assert.Equal(1, NoteStore.ImportNotes(file, null));
                    else NoteStore.ImportBatch(new[] { new PreparedImport { Title = "imported", Created = DateTime.Now, Modified = DateTime.Now } });
                    long imported = Assert.Single(NoteStore.List()).Id;
                    Assert.True(imported > retired);
                    NoteStore.DeleteForever(imported);

                    long replacement = NoteStore.Create("replacement");

                    Assert.True(replacement > imported);
                    NoteStore.DeleteForever(imported);
                    Assert.Equal("replacement", Assert.Single(NoteStore.List()).Title);
                }
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void RestoredExplicitIdsRemainReservedAfterPermanentDeletion()
        {
            using var store = new TempStore();
            long source = NoteStore.Create("source");
            var row = NoteStore.CaptureRow(source)!;
            NoteStore.DeleteForever(source);
            row.Id = 42;
            NoteStore.RestoreRow(row);
            NoteStore.DeleteForever(42);

            Assert.True(NoteStore.Create("replacement") > 42);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void ACompleteCopyKeepsPlaceholdersMetadataLinksAndPayloads(int format) => Sta.Run(() =>
        {
            using var store = new TempStore();
            long target = NoteStore.Create("target");
            long source = NoteStore.Create("{date} source", format);
            const string text = "{time} [[target]]";
            byte[] content;
            if (format == 1) content = MarkdownBlob.Encode(text);
            else
            {
                var doc = new FlowDocument(new Paragraph(new Bold(new Run(text))));
                using var output = new MemoryStream();
                new TextRange(doc.ContentStart, doc.ContentEnd).Save(output, DataFormats.XamlPackage);
                content = output.ToArray();
            }
            NoteStore.Save(source, "{date} source", content, text);
            NoteStore.SetNoteTags(source, "one, two");
            NoteStore.SetTitleColor(source, "#123456");
            NoteStore.SetSpellCheck(source, true);
            NoteStore.SetSyntaxHighlight(source, true);
            NoteStore.SaveSketches(source, new Dictionary<int, byte[]> { [3] = new byte[] { 1, 2, 3 } });
            NoteStore.AddRecording(source, new byte[] { 4, 5, 6 }, 1250);
            string group = NoteStore.GroupPath("Templates", "Work");

            long id = NoteStore.CreateFromTemplate(source, group: group, expand: false);

            var copy = NoteStore.GetNote(id)!;
            Assert.Equal("{date} source", copy.Title);
            Assert.Equal(format, copy.Format);
            Assert.Equal("one, two", copy.Tags);
            Assert.Equal("#123456", copy.TitleColor);
            Assert.True(copy.SpellCheck);
            Assert.True(copy.SyntaxHighlight);
            Assert.Equal(group, copy.Notebook);
            Assert.Contains(text, NoteStore.LoadPlain(id));
            Assert.Equal(target, Assert.Single(NoteStore.OutgoingLinks(id)).Id);
            Assert.Equal(new byte[] { 1, 2, 3 }, NoteStore.LoadSketches(id)[3]);
            Assert.Equal(new byte[] { 4, 5, 6 }, NoteStore.LoadRecordingRaw(id, 0));
            Assert.Equal(1250, NoteStore.LoadRecordingIndex(id)[0]);
            Assert.Contains(NoteStore.ListGroupTree(), g => g.Path == "Templates" && g.Parent == "");
            Assert.Contains(NoteStore.ListGroupTree(), g => g.Path == group && g.Parent == "Templates");
        });

        [Fact]
        public void IdsAlreadyPresentAtOpenStayReservedAfterPermanentDeletion()
        {
            using var store = new TempStore();
            long original = NoteStore.Create("original");
            NoteStore.Close();
            NoteStore.Open();
            NoteStore.DeleteForever(original);

            long replacement = NoteStore.Create("replacement");

            Assert.True(replacement > original);
        }

        [Fact]
        public void ACopyRejectedWhileWritingPayloadsLeavesNoNoteOrGroup() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long source = NoteStore.Create("source", 1);
            NoteStore.Save(source, "source", MarkdownBlob.Encode("[[target]]"), "[[target]]");
            NoteStore.SaveSketches(source, new Dictionary<int, byte[]> { [0] = new byte[] { 1, 2, 3 } });
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DbPath }.ConnectionString))
            {
                db.Open();
                using var reject = db.CreateCommand();
                reject.CommandText = "CREATE TRIGGER reject_copy BEFORE INSERT ON sketches BEGIN SELECT RAISE(ABORT, 'copy storage failure'); END";
                reject.ExecuteNonQuery();
            }

            var error = Assert.Throws<SqliteException>(() => NoteStore.CreateFromTemplate(source, group: "Templates", expand: false));
            Assert.Contains("copy storage failure", error.Message);

            Assert.Equal(source, Assert.Single(NoteStore.List()).Id);
            Assert.Empty(NoteStore.ListGroupTree());
            Assert.Equal("[[target]]", NoteStore.LoadPlain(source));
            Assert.Single(NoteStore.LoadSketches(source));
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnUnreadableRichTemplateFailsWithoutCreatingAnEmptyCopy(bool expand) => Sta.Run(() =>
        {
            using var store = new TempStore();
            long source = NoteStore.Create("unreadable", Encoding.UTF8.GetBytes("invalid package"), "original text");

            Assert.ThrowsAny<Exception>(() => NoteStore.CreateFromTemplate(source, expand: expand));

            Assert.Equal(source, Assert.Single(NoteStore.List()).Id);
            Assert.Equal("original text", NoteStore.LoadPlain(source));
        });

        [Fact]
        public void ImportImmediatelyMakesEveryGroupLevelAvailableWithoutReopening()
        {
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".knote");
            string path = NoteStore.GroupPath("Work", "Projects");
            try
            {
                using (new TempStore())
                {
                    long id = NoteStore.Create("imported");
                    NoteStore.SetNoteGroup(id, path);
                    NoteStore.ExportNote(id, file, null);
                }
                using (new TempStore())
                {
                    NoteStore.AddGroup("Work");
                    NoteStore.SetGroupColor("Work", "#123456");
                    Assert.Equal(1, NoteStore.ImportNotes(file, null));
                    var groups = NoteStore.ListGroupTree();
                    Assert.Equal("#123456", Assert.Single(groups, g => g.Path == "Work").Color);
                    Assert.Equal("Work", Assert.Single(groups, g => g.Path == path).Parent);
                }
            }
            finally { File.Delete(file); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ANewNoteNeverReusesAnIdHeldByUndoInTheOpenSession(bool withContent)
        {
            using var store = new TempStore();
            long original = NoteStore.Create("original");
            long copy = NoteStore.Create("copy");
            NoteStore.DeleteForever(copy);

            long replacement = withContent
                ? NoteStore.Create("replacement", Encoding.UTF8.GetBytes("body"), "body")
                : NoteStore.Create("replacement");

            Assert.True(replacement > copy);
            NoteStore.DeleteForever(copy);
            Assert.Equal("replacement", NoteStore.GetNote(replacement)!.Title);
            Assert.NotNull(NoteStore.GetNote(original));
        }
    }
}

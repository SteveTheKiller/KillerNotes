using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using KillerNotes.Models;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class McpCommandTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static MainWindow DetachedShell()
        {
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            typeof(MainWindow).GetField("_saveTimer", PrivateInstance)!.SetValue(shell, new DispatcherTimer());
            typeof(MainWindow).GetField("_currentId", PrivateInstance)!.SetValue(shell, -1L);
            return shell;
        }

        private static string Execute(MainWindow shell, params string[] args) => (string)typeof(MainWindow).GetMethod("ExecuteMcpCommand", PrivateInstance)!.Invoke(shell, new object[] { args })!;

        [Theory]
        [InlineData("delete-group")]
        [InlineData("delete-tag")]
        [InlineData("permanent-delete")]
        [InlineData("empty-trash")]
        public void DestructiveCommandsRequireExplicitConfirmation(string command) => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Keep"); NoteStore.Trash(id);
            NoteStore.AddGroup("Keep group"); NoteStore.AddTag("Keep tag", "");
            var error = Assert.Throws<TargetInvocationException>(() => Execute(DetachedShell(), command));
            Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Contains("--confirm", error.InnerException!.Message);
            Assert.Single(NoteStore.ListTrash()); Assert.Single(NoteStore.ListGroupTree()); Assert.Contains(NoteStore.ListTags(), t => t.Name == "Keep tag");
        });

        [Fact]
        public void PermanentDeleteRejectsLiveNoteEvenWithConfirmation() => Sta.Run(() =>
        {
            using var store = new TempStore(); long id = NoteStore.Create("Keep");
            var error = Assert.Throws<TargetInvocationException>(() => Execute(DetachedShell(), "permanent-delete", "--id", id.ToString(), "--confirm"));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Equal(id, Assert.Single(NoteStore.List()).Id);
        });

        [Fact]
        public void BackupCommandKeepsEncryptionAndRejectsExistingOutput() => Sta.Run(() =>
        {
            using var store = new TempStore("test-key");
            long id = NoteStore.Create("Keep", Note.FormatMarkdown);
            NoteStore.Save(id, "Keep", MarkdownBlob.Encode("body"), "body");
            string output = Path.Combine(Path.GetDirectoryName(store.DbPath)!, "backup.kndb");
            var shell = DetachedShell(); Execute(shell, "backup-now", "--output", output);
            Assert.True(NoteStore.IsEncryptedFile(output));
            byte[] before = File.ReadAllBytes(output);
            Assert.Throws<TargetInvocationException>(() => Execute(shell, "backup-now", "--output", output));
            Assert.Equal(before, File.ReadAllBytes(output)); Assert.Single(NoteStore.List());
        });

        [Fact]
        public void GroupSelectionIsDatabaseScopedAndCanBeCleared() => Sta.Run(() =>
        {
            using var store = new TempStore(); NoteStore.AddGroup("Templates");
            var shell = DetachedShell();
            Assert.Contains("Templates", Execute(shell, "template-group", "--name", "Templates"));
            Assert.Contains("Templates", Execute(shell, "template-group"));
            Execute(shell, "template-group", "--name", "");
            Assert.DoesNotContain("Templates", Execute(shell, "template-group"));
            Assert.Throws<TargetInvocationException>(() => Execute(shell, "daily-group", "--name", "Missing"));
        });

        [Fact]
        public void MarkdownUpdateOfRichNoteKeepsOriginalVersionAndSupportsEmptyContent() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Rich");
            var document = new FlowDocument(new Paragraph(new Run("Original rich text")));
            using var stream = new MemoryStream();
            new TextRange(document.ContentStart, document.ContentEnd).Save(stream, DataFormats.XamlPackage);
            byte[] original = stream.ToArray();
            NoteStore.Save(id, "Rich", original, "Original rich text");
            typeof(MainWindow).GetMethod("SaveMarkdown", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { id, "Rich", "" });
            Assert.Equal(Note.FormatMarkdown, NoteStore.GetFormat(id));
            Assert.Equal("", MarkdownBlob.Decode(NoteStore.LoadContent(id)));
            Assert.Contains(NoteStore.ListHistory(id), version => NoteStore.LoadVersion(version.Id)?.Content?.SequenceEqual(original) == true);
        });

        [Fact]
        public void ShareWritesPasswordProtectedCopyAndDoesNotOverwrite() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Private", Note.FormatMarkdown);
            NoteStore.Save(id, "Private", MarkdownBlob.Encode("secret"), "secret");
            string output = Path.Combine(Path.GetDirectoryName(store.DbPath)!, "shared.knote");
            var shell = DetachedShell();
            Assert.Contains(output.Replace("\\", "\\\\"), Execute(shell, "share", "--id", id.ToString(), "--output", output, "--password", "test-password"));
            Assert.True(NoteStore.IsEncryptedFile(output));
            byte[] before = File.ReadAllBytes(output);
            Assert.Throws<TargetInvocationException>(() => Execute(shell, "share", "--id", id.ToString(), "--output", output));
            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Equal("secret", MarkdownBlob.Decode(NoteStore.LoadContent(id)));
        });

        [Fact]
        public void VersionGetRejectsVersionFromAnotherNote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long first = NoteStore.Create("First", Note.FormatMarkdown);
            NoteStore.Save(first, "First", MarkdownBlob.Encode("first body"), "first body");
            NoteStore.Snapshot(first, force: true);
            long version = NoteStore.ListHistory(first)[0].Id;
            long second = NoteStore.Create("Second");
            var shell = DetachedShell();
            Assert.Contains("first body", Execute(shell, "version-get", "--id", first.ToString(), "--version-id", version.ToString()));
            var error = Assert.Throws<TargetInvocationException>(() => Execute(shell, "version-get", "--id", second.ToString(), "--version-id", version.ToString()));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        });

        [Fact]
        public void NoteCreatedFromTemplateKeepsEmbeddedRecording() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long template = NoteStore.Create("Template");
            var document = new FlowDocument(new Paragraph(new Run("Audio note")));
            using var stream = new MemoryStream();
            new TextRange(document.ContentStart, document.ContentEnd).Save(stream, DataFormats.XamlPackage);
            NoteStore.Save(template, "Template", stream.ToArray(), "Audio note");
            byte[] recording = [4, 3, 2, 1];
            Assert.Equal(0, NoteStore.AddRecording(template, recording, 900));

            var shell = DetachedShell();
            long copy = (long)typeof(MainWindow).GetMethod("CreateFromTemplate", PrivateInstance)!
                .Invoke(shell, new object?[] { template, "Copy", null, false })!;
            Assert.Equal(recording, NoteStore.LoadRecordingRaw(copy, 0));
            Assert.Equal(900, NoteStore.LoadRecordingIndex(copy)[0]);
        });

        [Fact]
        public void ExportAllAcceptsAnEmptyFolderAndRefusesOneWithFiles() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.Create("One");
            string root = Path.GetDirectoryName(store.DbPath)!;
            string empty = Path.Combine(root, "vault-empty");
            Directory.CreateDirectory(empty);
            var shell = DetachedShell();

            Execute(shell, "export-all", "--output", empty);
            Assert.NotEmpty(Directory.GetFileSystemEntries(empty));

            // The folder now holds the export, so a second run would overwrite it.
            var error = Assert.Throws<TargetInvocationException>(() => Execute(shell, "export-all", "--output", empty));
            Assert.IsType<ArgumentException>(error.InnerException);
        });

        [Fact]
        public void RestoringAVersionDoesNotLinkTextDrawnInASketch() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long real = NoteStore.Create("Real");
            NoteStore.Create("Ghost");
            long id = NoteStore.Create("Drawn");
            var sketch = new Attachment(AttachmentKind.Sketch, SketchModel.Serialize(new[]
            {
                new SketchObject { Kind = SketchKind.Text, Text = "[[Ghost]]" },
            }));
            var image = new System.Windows.Controls.Image
            {
                Source = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8),
                ToolTip = Attachments.Marker(AttachmentKind.Sketch, sketch.Hash),
            };
            var paragraph = new Paragraph(new Run("see [[Real]] "));
            paragraph.Inlines.Add(new InlineUIContainer(image));
            var drawn = new FlowDocument(paragraph);
            using var stream = new MemoryStream();
            new TextRange(drawn.ContentStart, drawn.ContentEnd).Save(stream, DataFormats.XamlPackage);
            // The stored text is the body plus the sketch's labels, as a save writes it.
            NoteStore.SaveEditorState(id, "Drawn", stream.ToArray(), "see [[Real]] \n[[Ghost]]", new[] { "Real" }, new[] { sketch });
            Assert.True(NoteStore.Snapshot(id, force: true));
            long version = NoteStore.ListHistory(id)[0].Id;
            NoteStore.Save(id, "Drawn", Array.Empty<byte>(), "later text");
            NoteStore.SetLinks(id, Array.Empty<string>());

            // The detached window has no sidebar to refresh once the version is back.
            Record.Exception(() => Execute(DetachedShell(), "version-restore", "--id", id.ToString(), "--version-id", version.ToString()));

            Assert.Equal(new[] { real }, NoteStore.OutgoingLinks(id).Select(l => l.Id));
            Assert.DoesNotContain(NoteStore.Backlinks("Ghost"), b => b.Id == id);
        });

        [Fact]
        public void ConvertingMarkdownToRichKeepsPipeTablesAsTables() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Hosts", Note.FormatMarkdown);
            const string md = "Before\n\n| Host | Port |\n| --- | --- |\n| web01 | 443 |\n\nAfter";
            NoteStore.Save(id, "Hosts", MarkdownBlob.Encode(md), md);

            // The detached window has no sidebar to refresh once the note is stored.
            Record.Exception(() => Execute(DetachedShell(), "convert", "--id", id.ToString(), "--format", "rich"));

            Assert.Equal(Note.FormatRich, NoteStore.GetFormat(id));
            var document = NoteSearch.LoadDocument(NoteStore.LoadContent(id), Note.FormatRich);
            Assert.Single(document.Blocks.OfType<Table>());
        });

        // Editing a recording after switching notes or databases: RecordingPersistenceTests.

        [Theory]
        [InlineData("--content", "--title", "--title", "Actual title")]
        [InlineData("--title", "Actual title", "--content", "--title")]
        [InlineData("--content", "--group", "--group", "--tags", "--title", "Actual title")]
        public void OptionLikeValuesStayValues(params string[] options) => Sta.Run(() =>
        {
            using var store = new TempStore();
            var shell = DetachedShell();
            // The detached window has no sidebar to refresh once the note is stored; the store
            // is what this checks.
            var error = Record.Exception(() => Execute(shell, new[] { "create" }.Concat(options).ToArray()));
            Assert.True(error == null || error.InnerException is ArgumentNullException, error?.ToString());
            var note = Assert.Single(NoteStore.List());
            Assert.Equal("Actual title", note.Title);
            int at = Array.IndexOf(options, "--content");
            Assert.Equal(options[at + 1], MarkdownBlob.Decode(NoteStore.LoadContent(note.Id)));
        });

        [Fact]
        public void ACreateThatCannotSaveItsContentLeavesNoEmptyNoteBehind() => Sta.Run(() =>
        {
            using var store = new TempStore();
            using (var other = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DbPath}"))
            {
                other.Open();
                using var cmd = other.CreateCommand();
                cmd.CommandText = "CREATE TRIGGER refuse_edit BEFORE UPDATE ON notes BEGIN SELECT RAISE(ABORT, 'disk is full'); END";
                cmd.ExecuteNonQuery();
            }

            var error = Assert.Throws<TargetInvocationException>(() =>
                Execute(DetachedShell(), "create", "--title", "Idea", "--content", "body"));
            Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error.InnerException);

            Assert.Empty(NoteStore.List());
        });

        [Theory]
        [InlineData("create", "--title", "a", "--title", "b", "--content", "x")]
        [InlineData("create", "--title", "a", "--content", "x", "--unknown", "y")]
        [InlineData("create", "--title", "a", "--content")]
        public void DuplicateUnknownAndMissingOptionsChangeNothing(params string[] args) => Sta.Run(() =>
        {
            using var store = new TempStore();
            var error = Assert.Throws<TargetInvocationException>(() => Execute(DetachedShell(), args));
            Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Empty(NoteStore.List());
        });
    }
}

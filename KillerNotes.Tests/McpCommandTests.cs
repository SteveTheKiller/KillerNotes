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
    }
}

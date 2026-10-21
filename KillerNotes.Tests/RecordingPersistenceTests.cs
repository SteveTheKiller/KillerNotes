using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// An embedded recording used to vanish at the first save: its playback chip is not an
    /// element the note format keeps, so the stored note held a blank where the chip had been and
    /// the audio sat orphaned in the database. These drive the editor's own embed, serialize and
    /// reopen code (the methods SaveCurrentNoteCore and OpenNote call) against a real store.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class RecordingPersistenceTests : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly TimeSpan _interval = NoteStore.HistoryInterval;

        public void Dispose() => NoteStore.HistoryInterval = _interval;

        [Fact]
        public void ARecordingKeepsItsChipAndAudioThroughSaveAndReopen() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("n");
            var shell = Shell(id, "before ");
            Embed(shell, Audio(1), 1500);
            Embed(shell, Audio(2), 2500);
            Save(shell, id);

            var reopened = Shell(id, null);
            Open(reopened, id);
            Assert.Equal(new[] { Audio(1), Audio(2) }, Chips(reopened).Select(Payload));
            Assert.Equal(new[] { 1500, 2500 }, Chips(reopened).Select(c => RecordingRef.From(c.Tag)!.DurationMs));
            Assert.Contains("before", new TextRange(reopened.Editor.Document.ContentStart, reopened.Editor.Document.ContentEnd).Text);

            // A second save of the reopened note keeps them too.
            Save(reopened, id);
            var again = Shell(id, null);
            Open(again, id);
            Assert.Equal(2, Chips(again).Count);
        });

        [Fact]
        public void RestoringAVersionPlaysTheAudioThatVersionHad() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.HistoryInterval = TimeSpan.Zero;
            long id = NoteStore.Create("n");
            var shell = Shell(id, "");
            Embed(shell, Audio(3), 1000);
            Save(shell, id);

            string first = RecordingRef.From(Chips(shell).Single().Tag)!.Hash;
            Embed(shell, Audio(4), 2000, replaceHash: first, editNoteId: id);   // re-record over it
            Save(shell, id);
            Assert.Equal(Audio(4), Payload(Chips(shell).Single()));

            Assert.True(NoteStore.RestoreVersion(id, NoteStore.ListHistory(id).First().Id));
            var reopened = Shell(id, null);
            Open(reopened, id);
            Assert.Equal(Audio(3), Payload(Chips(reopened).Single()));
        });

        [Fact]
        public void RecordingsOrphanedByEarlierVersionsComeBackAtTheEndOfTheirNote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("old");
            var doc = new FlowDocument(new Paragraph(new Run("old note")));
            using (var ms = new MemoryStream())
            {
                new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
                NoteStore.Save(id, "old", ms.ToArray(), "old note");
            }
            NoteStore.AddRecording(id, Audio(5), 700);
            byte[] stored = NoteStore.LoadRecordingRaw(id, 0)!;

            var shell = Shell(id, null);
            Open(shell, id);
            var chip = Chips(shell).Single();
            // Playable at once, from the editor; the database takes it at the next save.
            var held = (Attachment)Invoke(shell, "RecordingPayload", RecordingRef.From(chip.Tag)!.Hash)!;
            Assert.Equal(stored, held.Payload);
            Assert.Equal(700, held.DurationMs);
            Assert.True((bool)Get(shell, "_dirty")!);   // so the next save anchors it for good

            Save(shell, id);
            Assert.Empty(NoteStore.LoadRecordingIndex(id));   // moved, not copied
            var reopened = Shell(id, null);
            Open(reopened, id);
            Assert.Equal(stored, Payload(Chips(reopened).Single()));
        });

        [Fact]
        public void EditingARecordingAfterSwitchingNotesUpdatesItsOriginalNote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long original = NoteStore.Create("Original"), current = NoteStore.Create("Current");
            var a = Shell(original, "");
            Embed(a, Audio(6), 100);
            Save(a, original);
            string hash = RecordingRef.From(Chips(a).Single().Tag)!.Hash;
            var b = Shell(current, "");
            Embed(b, Audio(7), 100);
            Save(b, current);

            Embed(b, Audio(8), 200, replaceHash: hash, editNoteId: original);   // b is the open note

            var reopenOriginal = Shell(original, null);
            Open(reopenOriginal, original);
            Assert.Equal(Audio(8), Payload(Chips(reopenOriginal).Single()));
            var reopenCurrent = Shell(current, null);
            Open(reopenCurrent, current);
            Assert.Equal(Audio(7), Payload(Chips(reopenCurrent).Single()));
        });

        [Fact]
        public void AnEditCannotReachIntoAnotherDatabase() => Sta.Run(() =>
        {
            string otherDatabase;
            string otherHash;
            using (new TempStore())
            {
                long other = NoteStore.Create("Other");
                var o = Shell(other, "");
                Embed(o, Audio(9), 100);
                Save(o, other);
                otherHash = RecordingRef.From(Chips(o).Single().Tag)!.Hash;
                otherDatabase = NoteStore.DbPath;
            }
            using var store = new TempStore();
            long current = NoteStore.Create("Current");
            var shell = Shell(current, "");
            var error = Assert.Throws<TargetInvocationException>(() =>
                Invoke(shell, "EmbedRecordingInNote", Audio(10), 200, otherHash, current, otherDatabase));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        });

        /// <summary>Saving, copying and opening must leave the editor's undo history alone: Ctrl+Z
        /// undoes the user's last edit, never turns a chip into a picture. Needs a shown window,
        /// because a RichTextBox outside one records no undo at all.</summary>
        [Fact]
        public void SaveCopyAndOpenNeverReachTheUndoHistory() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("n");
            var shell = Shell(id, "start ");
            var editor = (RichTextBox)Get(shell, "Editor")!;
            var window = new Window { Content = editor, Width = 400, Height = 300, Left = -20000, Top = -20000,
                                      ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show();
            try
            {
                editor.CaretPosition = editor.Document.ContentEnd;
                Embed(shell, Audio(11), 900);
                editor.CaretPosition = editor.Document.ContentEnd;
                editor.CaretPosition.InsertTextInRun("typed");
                Assert.True(editor.CanUndo);

                Save(shell, id);
                editor.SelectAll();
                Invoke(shell, "BuildSelectionData");
                editor.Undo();   // must take back "typed", nothing else

                Assert.DoesNotContain("typed", new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
                Assert.Single(Chips(shell));

                // Opening a note starts a fresh history: nothing to undo into the previous one.
                using (var ms = new MemoryStream(NoteStore.LoadContent(id)!))
                    new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Load(ms, DataFormats.XamlPackage);
                Invoke(shell, "ReviveRecordings", id);
                Assert.False(editor.CanUndo);
                Assert.Single(Chips(shell));

                // Deleting a chip makes the editor serialize it for Undo; its Tag must allow that.
                var chip = Chips(shell).Single();
                var container = (InlineUIContainer)chip.Parent;
                new TextRange(container.ElementStart, container.ElementEnd).Text = "";
                Assert.Empty(Chips(shell));
                editor.Undo();
                Assert.Equal(RecordingRef.From(chip.Tag)!.Hash, RecordingRef.From(Chips(shell).Single().Tag)!.Hash);
            }
            finally { window.Close(); }
        });

        // ---- the editor, detached from a window ----

        private static MainWindow Shell(long id, string? text)
        {
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            // Stand in for the field initializers an uninitialized object skips: every empty
            // collection the window would have been constructed with.
            foreach (var field in typeof(MainWindow).GetFields(Members))
                if (field.GetValue(shell) == null && field.FieldType.IsGenericType && !field.FieldType.IsInterface &&
                    field.FieldType.Namespace == "System.Collections.Generic" && field.FieldType.GetConstructor(Type.EmptyTypes) != null &&
                    !field.Name.StartsWith("_recording", StringComparison.Ordinal))
                    field.SetValue(shell, Activator.CreateInstance(field.FieldType));
            var editor = new RichTextBox();
            if (text != null) editor.Document = new FlowDocument(new Paragraph(new Run(text)));
            Set(shell, "Editor", editor);
            Set(shell, "_saveTimer", new DispatcherTimer());
            Set(shell, "_syntaxTimer", new DispatcherTimer());
            Set(shell, "_currentId", id);
            Set(shell, "StatusText", new TextBlock());
            return shell;
        }

        private static void Embed(MainWindow shell, byte[] audio, int ms, string? replaceHash = null, long editNoteId = -1) =>
            Invoke(shell, "EmbedRecordingInNote", audio, ms, replaceHash!, editNoteId, NoteStore.DbPath);

        private static void Save(MainWindow shell, long id)
        {
            var attachments = new List<Attachment>();
            var blob = (byte[])Invoke(shell, "SerializeRichDocument", attachments)!;
            bool drop = (bool)Get(shell, "_dropLegacyRecordings")!;
            NoteStore.SaveEditorState(id, "n", blob, "", Array.Empty<string>(), attachments, drop);
            Invoke(shell, "OnRichDocumentSaved");
        }

        private static void Open(MainWindow shell, long id)
        {
            var editor = (RichTextBox)Get(shell, "Editor")!;
            using var ms = new MemoryStream(NoteStore.LoadContent(id)!);
            new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Load(ms, DataFormats.XamlPackage);
            Invoke(shell, "ReviveRecordings", id);
        }

        private static List<Border> Chips(MainWindow shell) =>
            Descendants(((RichTextBox)Get(shell, "Editor")!).Document).OfType<Border>().Where(b => RecordingRef.From(b.Tag) != null).ToList();

        private static byte[] Payload(Border chip) =>
            NoteStore.GetAttachment(RecordingRef.From(chip.Tag)!.Hash)?.Payload ?? Array.Empty<byte>();

        private static IEnumerable<UIElement> Descendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is InlineUIContainer { Child: { } c }) yield return c;
                else if (child is BlockUIContainer { Child: { } bc }) yield return bc;
                else if (child is DependencyObject d)
                    foreach (var nested in Descendants(d)) yield return nested;
            }
        }

        private static byte[] Audio(byte seed) => Enumerable.Range(0, 64).Select(i => (byte)(i ^ seed)).ToArray();

        private static object? Get(MainWindow shell, string name) =>
            (typeof(MainWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).GetValue(shell);

        private static void Set(MainWindow shell, string name, object value) =>
            (typeof(MainWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(shell, value);

        private static object? Invoke(MainWindow shell, string name, params object[] args) =>
            (typeof(MainWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(shell, args);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Copying or cutting a sketch used to paste back as a flat picture, and a recording as a
    /// blank. The copy now carries the payloads its markers name, so a paste into the same note,
    /// another note or another database keeps an editable sketch and a playable recording through
    /// save and reopen. Drives the editor's own copy and paste hooks without the system clipboard.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ClipboardAttachmentTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Fact]
        public void APastedSketchAndRecordingStayEditableAndPlayableInAnotherDatabase() => Sta.Run(() =>
        {
            IDataObject copied;
            using (new TempStore())
            {
                long source = NoteStore.Create("source");
                var a = Shell(source);
                var img = new Image { Source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8) };
                Sketch.SetData(img, Bytes(1));
                Editor(a).Document = new FlowDocument(new Paragraph(new InlineUIContainer(img)));
                Editor(a).CaretPosition = Editor(a).Document.ContentEnd;
                Invoke(a, "EmbedRecordingInNote", Bytes(2), 1200, null!, -1L, NoteStore.DbPath);
                Editor(a).SelectAll();
                copied = (IDataObject)Invoke(a, "BuildSelectionData")!;
            }   // the source database is closed: everything has to come from the copy

            using var store = new TempStore();
            long target = NoteStore.Create("target");
            var b = Shell(target);
            Paste(b, copied);
            Save(b, target);

            var reopened = Shell(target);
            using (var ms = new MemoryStream(NoteStore.LoadContent(target)!))
                new TextRange(Editor(reopened).Document.ContentStart, Editor(reopened).Document.ContentEnd).Load(ms, DataFormats.XamlPackage);
            Sketch.Link(Editor(reopened).Document.Blocks, NoteStore.LoadSketches(target), NoteStore.GetAttachment);
            Invoke(reopened, "ReviveRecordings", target);

            var sketch = Sketch.Images(Editor(reopened).Document.Blocks).Single();
            Assert.True(Sketch.TryGetData(sketch, out var payload));
            Assert.Equal(Bytes(1), payload);
            var chip = Descendants(Editor(reopened).Document).OfType<Border>().Single(c => RecordingRef.From(c.Tag) != null);
            Assert.Equal(1200, RecordingRef.From(chip.Tag)!.DurationMs);
            Assert.NotNull(NoteStore.GetAttachment(RecordingRef.From(chip.Tag)!.Hash));
        });

        private static void Paste(MainWindow shell, IDataObject data)
        {
            Invoke(shell, "AcceptPastedAttachments", data);
            var stream = (Stream)data.GetData(DataFormats.XamlPackage);
            stream.Position = 0;
            new TextRange(Editor(shell).Document.ContentStart, Editor(shell).Document.ContentEnd).Load(stream, DataFormats.XamlPackage);
            Invoke(shell, "NormalizePastedContent");
        }

        private static void Save(MainWindow shell, long id)
        {
            var attachments = new List<Attachment>();
            var blob = (byte[])Invoke(shell, "SerializeRichDocument", attachments)!;
            NoteStore.SaveEditorState(id, "n", blob, "", Array.Empty<string>(), attachments);
            Invoke(shell, "OnRichDocumentSaved");
        }

        private static MainWindow Shell(long id)
        {
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            foreach (var field in typeof(MainWindow).GetFields(Members))
                if (field.GetValue(shell) == null && field.FieldType.IsGenericType && !field.FieldType.IsInterface &&
                    field.FieldType.Namespace == "System.Collections.Generic" && field.FieldType.GetConstructor(Type.EmptyTypes) != null &&
                    !field.Name.StartsWith("_recording", StringComparison.Ordinal) && !field.Name.StartsWith("_pasted", StringComparison.Ordinal))
                    field.SetValue(shell, Activator.CreateInstance(field.FieldType));
            Set(shell, "Editor", new RichTextBox());
            Set(shell, "_saveTimer", new DispatcherTimer());
            Set(shell, "_syntaxTimer", new DispatcherTimer());
            Set(shell, "_currentId", id);
            Set(shell, "StatusText", new TextBlock());
            return shell;
        }

        private static RichTextBox Editor(MainWindow shell) =>
            (RichTextBox)typeof(MainWindow).GetField("Editor", Members)!.GetValue(shell)!;

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

        private static byte[] Bytes(byte seed) => Enumerable.Range(0, 48).Select(i => (byte)(i * 3 + seed)).ToArray();

        private static void Set(MainWindow shell, string name, object value) =>
            (typeof(MainWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(shell, value);

        private static object? Invoke(MainWindow shell, string name, params object[] args) =>
            (typeof(MainWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(shell, args);
    }
}

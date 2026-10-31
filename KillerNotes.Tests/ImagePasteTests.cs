using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A screenshot is a bitmap-only clipboard. The paste reads the image from the paste's own
    /// data, not the live clipboard again, and a clipboard that another listener is holding open
    /// (Win+V history, OneDrive) turns into a skipped paste instead of an unhandled exception.
    /// </summary>
    public sealed class ImagePasteTests
    {
        [Fact]
        public void ABitmapOnlyPasteInsertsTheImageFromThePastedData() => Sta.Run(() =>
        {
            var (shell, editor) = Shell();
            editor.Document = new FlowDocument(new Paragraph(new Run("")));
            editor.CaretPosition = editor.Document.ContentStart;
            var data = new DataObject(DataFormats.Bitmap, BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8));
            var args = new DataObjectPastingEventArgs(data, false, DataFormats.Bitmap);

            Invoke(shell, "Editor_OnPaste", editor, args);

            Assert.True(args.CommandCancelled);
            Assert.Single(Images(editor.Document));
        });

        [Fact]
        public void AClipboardHeldOpenSkipsTheImagePasteInsteadOfThrowing() => Sta.Run(() =>
        {
            var (shell, editor) = Shell();
            editor.Document = new FlowDocument(new Paragraph(new Run("")));
            var args = new DataObjectPastingEventArgs(new HeldOpen(), false, DataFormats.Bitmap);

            Invoke(shell, "Editor_OnPaste", editor, args);

            Assert.Empty(Images(editor.Document));
        });

        private static List<Image> Images(FlowDocument doc)
        {
            var found = new List<Image>();
            for (var p = doc.ContentStart; p != null && p.CompareTo(doc.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
                if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer { Child: Image img } && !found.Contains(img))
                    found.Add(img);
            return found;
        }

        /// <summary>A clipboard that announces a bitmap and then refuses to hand it over.</summary>
        private sealed class HeldOpen : IDataObject
        {
            public object GetData(string format) => GetData(format, true);
            public object GetData(string format, bool autoConvert) => throw new ExternalException("CLIPBRD_E_CANT_OPEN");
            public object GetData(Type format) => throw new ExternalException("CLIPBRD_E_CANT_OPEN");
            public bool GetDataPresent(string format) => format == DataFormats.Bitmap;
            public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);
            public bool GetDataPresent(Type format) => false;
            public string[] GetFormats() => new[] { DataFormats.Bitmap };
            public string[] GetFormats(bool autoConvert) => GetFormats();
            public void SetData(object data) { }
            public void SetData(string format, object data) { }
            public void SetData(string format, object data, bool autoConvert) { }
            public void SetData(Type format, object data) { }
        }

        private static (MainWindow, RichTextBox) Shell() => EditorShell.Make();

        private static void Invoke(MainWindow shell, string name, params object[] args) => EditorShell.Invoke(shell, name, args);
    }
}

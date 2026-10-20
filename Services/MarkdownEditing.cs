using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace KillerNotes.Services
{
    internal static class MarkdownEditing
    {
        public static void Attach(RichTextBox editor, Func<bool> isMarkdown)
        {
            DataObject.AddPastingHandler(editor, (_, e) =>
            {
                if (!isMarkdown()) return;
                string? text = e.DataObject.GetData(DataFormats.UnicodeText) as string
                    ?? e.DataObject.GetData(DataFormats.Text) as string;
                if (text == null)
                {
                    // Rich-only clipboard data still has recoverable characters. Load it
                    // into a detached document before projecting the supported text shapes.
                    foreach (string format in new[] { DataFormats.XamlPackage, DataFormats.Rtf, DataFormats.Xaml })
                    {
                        if (!e.DataObject.GetDataPresent(format)) continue;
                        try
                        {
                            object value = e.DataObject.GetData(format);
                            using var copy = new MemoryStream();
                            if (value is Stream stream)
                            {
                                long position = stream.CanSeek ? stream.Position : 0;
                                try { stream.CopyTo(copy); }
                                finally { if (stream.CanSeek) stream.Position = position; }
                            }
                            else if (value is string rich)
                            {
                                byte[] bytes = Encoding.UTF8.GetBytes(rich);
                                copy.Write(bytes, 0, bytes.Length);
                            }
                            else continue;
                            copy.Position = 0;
                            var doc = new FlowDocument();
                            new TextRange(doc.ContentStart, doc.ContentEnd).Load(copy, format);
                            text = MarkdownBlob.TextOf(doc);
                            break;
                        }
                        catch (ArgumentException) { }
                        catch (InvalidOperationException) { }
                        catch (System.Xml.XmlException) { }
                    }
                }
                if (text == null) { e.CancelCommand(); return; }
                e.DataObject = new DataObject(DataFormats.UnicodeText, text);
                e.FormatToApply = DataFormats.UnicodeText;
            });
            CommandManager.AddPreviewCanExecuteHandler(editor, (_, e) =>
            {
                if (!isMarkdown() || !IsStructural(e.Command)) return;
                e.CanExecute = false;
                e.Handled = true;
            });
            CommandManager.AddPreviewExecutedHandler(editor, (_, e) =>
            {
                if (isMarkdown() && IsStructural(e.Command)) e.Handled = true;
            });
        }

        private static bool IsStructural(ICommand command) =>
            command == EditingCommands.ToggleBullets || command == EditingCommands.ToggleNumbering;
    }
}

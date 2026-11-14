using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
                if (!isMarkdown() || (!IsStructural(e.Command) && MarkerFor(e.Command) == null && e.Command != EditingCommands.ToggleUnderline)) return;
                e.CanExecute = e.Command != EditingCommands.ToggleUnderline && !editor.IsReadOnly;
                e.Handled = true;
            });
            CommandManager.AddPreviewExecutedHandler(editor, (_, e) =>
            {
                if (!isMarkdown()) return;
                if (e.Command == EditingCommands.ToggleUnderline) { e.Handled=true; return; }
                if (MarkerFor(e.Command) is string marker)
                {
                    ToggleInline(editor, marker);
                    e.Handled = true;
                }
                else if (IsStructural(e.Command))
                {
                    ToggleList(editor, e.Command == EditingCommands.ToggleNumbering);
                    e.Handled = true;
                }
            });
        }

        private static string? MarkerFor(ICommand command) => command == EditingCommands.ToggleBold ? "**"
            : command == EditingCommands.ToggleItalic ? "*" : null;

        public static void ToggleInline(RichTextBox editor, string marker)
        {
            if (editor.IsReadOnly) return;
            var selection = editor.Selection;
            bool empty = selection.IsEmpty;
            var ranges = editor.Document.Blocks.OfType<Paragraph>()
                .Where(p => empty ? ReferenceEquals(p, editor.CaretPosition.Paragraph)
                    : p.ContentEnd.CompareTo(selection.Start) >= 0 && p.ContentStart.CompareTo(selection.End) < 0)
                .Select(p => new TextRange(selection.Start.CompareTo(p.ContentStart) > 0 ? selection.Start : p.ContentStart,
                    selection.End.CompareTo(p.ContentEnd) < 0 ? selection.End : p.ContentEnd)).ToArray();
            editor.BeginChange();
            try
            {
                foreach (var range in ranges)
                {
                    string value = range.Text;
                    if (!empty && value.Length == 0) continue;
                    bool remove = value.Length >= marker.Length*2 && value.StartsWith(marker, StringComparison.Ordinal)
                        && value.EndsWith(marker, StringComparison.Ordinal);
                    if (marker == "`")
                    {
                        var code = Regex.Match(value,@"^(`+)([\s\S]*?)\1$");
                        if (code.Success)
                        {
                            string body = code.Groups[2].Value;
                            range.Text = body.StartsWith(" ",StringComparison.Ordinal) && body.EndsWith(" ",StringComparison.Ordinal)
                                && body.Trim().Length > 0 ? body.Substring(1,body.Length-2) : body;
                        }
                        else
                        {
                            string fence = new string('`',Regex.Matches(value,"`+").Cast<Match>().Select(m => m.Length).DefaultIfEmpty(0).Max()+1);
                            bool pad = value.StartsWith("`",StringComparison.Ordinal) || value.EndsWith("`",StringComparison.Ordinal)
                                || (value.StartsWith(" ",StringComparison.Ordinal) && value.EndsWith(" ",StringComparison.Ordinal) && value.Trim().Length > 0);
                            range.Text = fence + (pad ? " ":"") + value + (pad ? " ":"") + fence;
                        }
                    }
                    else range.Text = remove ? value.Substring(marker.Length,value.Length-marker.Length*2) : marker+value+marker;
                    if (empty)
                        editor.CaretPosition = range.Start.GetInsertionPosition(LogicalDirection.Forward)
                            .GetPositionAtOffset(marker.Length,LogicalDirection.Forward);
                }
                if (!empty && ranges.Length > 0) selection.Select(ranges[0].Start,ranges[ranges.Length-1].End);
            }
            finally { editor.EndChange(); }
            editor.Focus();
        }

        public static void ToggleList(RichTextBox editor, bool numbered)
        {
            if (editor.IsReadOnly) return;
            var selection = editor.Selection;
            var paragraphs = editor.Document.Blocks.OfType<Paragraph>()
                .Where(p => selection.IsEmpty ? ReferenceEquals(p, editor.CaretPosition.Paragraph)
                    : p.ContentEnd.CompareTo(selection.Start) >= 0 && p.ContentStart.CompareTo(selection.End) < 0).ToArray();
            string target = numbered ? @"^[ \t]*\d+[.)]\s+" : @"^[ \t]*[-+*]\s+";
            bool remove = paragraphs.Length > 0 && paragraphs.All(p => Regex.IsMatch(new TextRange(p.ContentStart,p.ContentEnd).Text,target));
            editor.BeginChange();
            try
            {
                int number = 1;
                foreach (var paragraph in paragraphs)
                {
                    var range = new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
                    string value = range.Text;
                    var marker = Regex.Match(value,@"^([ \t]*)(?:[-+*]|\d+[.)])\s+");
                    string indent = marker.Success ? marker.Groups[1].Value : Regex.Match(value,@"^[ \t]*").Value;
                    string body = value.Substring(marker.Success ? marker.Length : indent.Length);
                    range.Text = indent + (remove ? "" : numbered ? (number++).ToString(System.Globalization.CultureInfo.InvariantCulture)+". " : "- ") + body;
                }
            }
            finally { editor.EndChange(); }
            editor.Focus();
        }

        public static void InsertBlock(RichTextBox editor, string source)
        {
            if (editor.IsReadOnly) return;
            var paragraph = editor.CaretPosition.Paragraph ?? editor.Selection.End.GetInsertionPosition(LogicalDirection.Backward)?.Paragraph;
            if (paragraph != null) editor.Selection.Select(paragraph.ContentEnd,paragraph.ContentEnd);
            string before = new TextRange(editor.Document.ContentStart,editor.Selection.Start).Text.Replace("\r\n","\n");
            string prefix = before.Length == 0 || before.EndsWith("\n\n",StringComparison.Ordinal) ? ""
                : before.EndsWith("\n",StringComparison.Ordinal) ? "\n" : "\n\n";
            editor.BeginChange();
            try
            {
                editor.Selection.Text = prefix + source + "\n\n";
                editor.CaretPosition = editor.Selection.End;
            }
            finally { editor.EndChange(); }
            editor.Focus();
        }

        private static bool IsStructural(ICommand command) =>
            command == EditingCommands.ToggleBullets || command == EditingCommands.ToggleNumbering;
    }
}

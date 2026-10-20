using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    internal static class DocumentReplacement
    {
        public static void Apply(RichTextBox editor, IReadOnlyList<(int Start, int Length, string Text)> edits)
        {
            var runs = new List<(int Offset, TextPointer Start, int Length)>();
            int offset = 0;
            for (TextPointer? p = editor.Document.ContentStart; p != null; p = p.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    int length = p.GetTextInRun(LogicalDirection.Forward).Length;
                    if (length > 0) runs.Add((offset, p, length));
                    offset += length;
                }
                else if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementEnd && p.Parent is Paragraph)
                    offset++;
            }
            editor.BeginChange();
            try
            {
                for (int i = edits.Count - 1; i >= 0; i--)
                {
                    var edit = edits[i];
                    var start = At(runs, edit.Start);
                    var end = At(runs, edit.Start + edit.Length, end: true);
                    if (start != null && end != null) new TextRange(start, end).Text = edit.Text;
                }
            }
            finally { editor.EndChange(); }
        }

        private static TextPointer? At(List<(int Offset, TextPointer Start, int Length)> runs, int offset, bool end = false)
        {
            int lookup = end ? offset - 1 : offset;
            foreach (var run in runs)
                if (lookup >= run.Offset && lookup < run.Offset + run.Length)
                    return run.Start.GetPositionAtOffset(offset - run.Offset,
                        end ? LogicalDirection.Backward : LogicalDirection.Forward);
            return null;
        }
    }
}

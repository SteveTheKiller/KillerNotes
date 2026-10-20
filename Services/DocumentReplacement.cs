using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    internal static class DocumentReplacement
    {
        internal const char ObjectBoundary = '\uFFFC';

        internal static bool ContainsObject(TextPointer start, TextPointer end)
        {
            for (TextPointer? p = start; p != null && p.CompareTo(end) < 0;
                p = p.GetNextContextPosition(LogicalDirection.Forward))
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement)
                    return true;
            return false;
        }

        public static string Substitute(System.Text.RegularExpressions.Regex regex, string input,
            int start, int length, string replacement)
        {
            var match = regex.Match(input, start);
            try { return match.Success && match.Index == start && match.Length == length
                ? match.Result(replacement) : replacement; }
            catch (System.ArgumentException) { return replacement; }
        }

        public static int Apply(RichTextBox editor, IReadOnlyList<(int Start, int Length, string Text)> edits)
        {
            var runs = new List<(int Offset, TextPointer Start, int Length)>();
            var separators = new Dictionary<int, (TextPointer Start, TextPointer End)>();
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
                {
                    separators[offset] = (p, p.GetNextInsertionPosition(LogicalDirection.Forward) ?? p);
                    offset++;
                }
                else if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement)
                    offset++;
            }
            int applied = 0;
            editor.BeginChange();
            try
            {
                for (int i = edits.Count - 1; i >= 0; i--)
                {
                    var edit = edits[i];
                    var start = At(runs, separators, edit.Start);
                    var end = At(runs, separators, edit.Start + edit.Length, end: true);
                    if (start != null && end != null && !ContainsObject(start, end))
                    {
                        new TextRange(start, end).Text = edit.Text;
                        applied++;
                    }
                }
            }
            finally { editor.EndChange(); }
            return applied;
        }

        private static TextPointer? At(List<(int Offset, TextPointer Start, int Length)> runs,
            Dictionary<int, (TextPointer Start, TextPointer End)> separators, int offset, bool end = false)
        {
            int lookup = end ? offset - 1 : offset;
            if (separators.TryGetValue(lookup, out var separator)) return end ? separator.End : separator.Start;
            int lo = 0, hi = runs.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                var run = runs[mid];
                if (lookup < run.Offset) hi = mid - 1;
                else if (lookup >= run.Offset + run.Length) lo = mid + 1;
                else
                    return run.Start.GetPositionAtOffset(offset - run.Offset,
                        end ? LogicalDirection.Backward : LogicalDirection.Forward);
            }
            return null;
        }
    }
}

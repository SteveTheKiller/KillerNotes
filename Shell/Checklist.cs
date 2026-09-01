// ═══════════════════════════════════════════════════════════
//  CHECKLISTS  -  checkbox lines in the editor
// ═══════════════════════════════════════════════════════════
//
// Alt+C (or the format bar button) puts a box at the head of the current line, or of every
// line in the selection, and takes it off again. Clicking the box flips it. Enter on a
// checkbox line starts the next one with an empty box, and Enter on a line holding nothing
// but its box ends the list, the way a word processor ends a bulleted list.
//
// The box is a character (Services/Checklist.cs), so everything here is text surgery on the
// paragraph's first characters: a TextRange over the box and a Text assignment, which the
// RichTextBox records as one undo step and repaints itself. Nothing is stored beyond the
// glyph, so the note's markdown export reads "- [ ]" and search finds the words after it.
//
// Markdown notes get the same gestures over their own marker ("- [ ] "), so a checklist typed
// in either kind of note behaves the same way.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        // True while the hand cursor over a box is ours to clear; the wikilink hand (Ctrl held)
        // is somebody else's and is left alone.
        private bool _checkHand;

        private void InitChecklist()
        {
            Editor.PreviewMouseLeftButtonDown += Checklist_MouseDown;
            Editor.PreviewMouseMove += Checklist_MouseMove;
            Editor.PreviewKeyDown += Checklist_KeyDown;
        }

        // ---- Toggle (Alt+C, format bar) ----

        private void Checkbox_Click(object sender, RoutedEventArgs e) => ToggleChecklist();

        /// <summary>Adds a box to every selected line that lacks one; when they all have one,
        /// takes them all off. A collapsed selection means the caret's line.</summary>
        private void ToggleChecklist()
        {
            if (_currentId < 0 || _currentInTrash) return;
            var paras = ParagraphsInSelection();
            if (paras.Count == 0) return;
            bool md = CurrentIsMarkdown;
            bool add = paras.Any(p => Checklist.Of(TextOf(p), md) == Checklist.State.None);
            Editor.BeginChange();   // one undo step for the whole selection
            try
            {
                foreach (var p in paras)
                {
                    if (add)
                    {
                        if (Checklist.Of(TextOf(p), md) == Checklist.State.None)
                            InsertPrefix(p, Checklist.Prefix(md, false));
                    }
                    else RemovePrefix(p, md);
                }
            }
            finally { Editor.EndChange(); }
            MarkDirty();
            Editor.Focus();
        }

        private List<Paragraph> ParagraphsInSelection()
        {
            var sel = Editor.Selection;
            if (sel.IsEmpty) return Editor.CaretPosition.Paragraph is Paragraph p ? [p] : [];
            return [.. AllParagraphs(Editor.Document.Blocks).Where(p =>
                p.ContentEnd.CompareTo(sel.Start) >= 0 && p.ContentStart.CompareTo(sel.End) <= 0)];
        }

        // Every paragraph in document order, however deep it sits in a list, table or section.
        private static IEnumerable<Paragraph> AllParagraphs(BlockCollection blocks)
        {
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case Paragraph p:
                        yield return p;
                        break;
                    case List l:
                        foreach (var li in l.ListItems)
                            foreach (var p in AllParagraphs(li.Blocks)) yield return p;
                        break;
                    case Section s:
                        foreach (var p in AllParagraphs(s.Blocks)) yield return p;
                        break;
                    case Table t:
                        foreach (var g in t.RowGroups)
                            foreach (var r in g.Rows)
                                foreach (var c in r.Cells)
                                    foreach (var p in AllParagraphs(c.Blocks)) yield return p;
                        break;
                }
            }
        }

        // ---- Text surgery ----

        private static string TextOf(Paragraph p) => new TextRange(p.ContentStart, p.ContentEnd).Text;

        private static TextPointer Head(Paragraph p) => p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);

        /// <summary>The position `count` characters after `from`. Steps by insertion position,
        /// so a formatting boundary between two runs is never miscounted as a character.</summary>
        private static TextPointer Advance(TextPointer from, int count)
        {
            var tp = from;
            for (int i = 0; i < count; i++) tp = tp.GetNextInsertionPosition(LogicalDirection.Forward) ?? tp;
            return tp;
        }

        private static void InsertPrefix(Paragraph p, string prefix) => Head(p).InsertTextInRun(prefix);

        private static void RemovePrefix(Paragraph p, bool md)
        {
            int len = Checklist.PrefixLength(TextOf(p), md);
            if (len == 0) return;
            var head = Head(p);
            new TextRange(head, Advance(head, len)).Text = "";
        }

        /// <summary>Flips the box on a line: the glyph in rich text, the marker in markdown.</summary>
        private void FlipBox(Paragraph p)
        {
            bool md = CurrentIsMarkdown;
            var state = Checklist.Of(TextOf(p), md);
            if (state == Checklist.State.None) return;
            var head = Head(p);
            var (from, to) = Checklist.BoxSpan(md);
            var range = new TextRange(Advance(head, from), Advance(head, to));
            bool wasChecked = state == Checklist.State.Checked;
            range.Text = md
                ? (wasChecked ? "[ ]" : "[x]")
                : (wasChecked ? Checklist.Empty : Checklist.Checked).ToString();
            MarkDirty();
        }

        // ---- Click on the box ----

        /// <summary>The paragraph whose box sits under a point in the editor, or null.</summary>
        private Paragraph? BoxUnder(Point pt)
        {
            try
            {
                var pos = Editor.GetPositionFromPoint(pt, false);
                if (pos?.Paragraph is not Paragraph p) return null;
                bool md = CurrentIsMarkdown;
                if (Checklist.Of(TextOf(p), md) == Checklist.State.None) return null;
                var head = Head(p);
                var (from, to) = Checklist.BoxSpan(md);
                var a = Advance(head, from).GetCharacterRect(LogicalDirection.Forward);
                var b = Advance(head, to).GetCharacterRect(LogicalDirection.Backward);
                var box = new Rect(
                    new Point(Math.Min(a.Left, b.Left) - 2, Math.Min(a.Top, b.Top)),
                    new Point(Math.Max(a.Right, b.Right) + 2, Math.Max(a.Bottom, b.Bottom)));
                return box.Contains(pt) ? p : null;
            }
            catch (InvalidOperationException) { return null; }   // document changed under the hit test
        }

        private void Checklist_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Plain single click only: Ctrl+Click is the wikilink gesture, and a double-click
            // over the box is a word selection the editor should keep.
            if (Keyboard.Modifiers != ModifierKeys.None || e.ClickCount != 1) return;
            if (_currentId < 0 || _currentInTrash) return;
            var p = BoxUnder(e.GetPosition(Editor));
            if (p == null) return;
            e.Handled = true;   // the caret stays where it was
            FlipBox(p);
        }

        private void Checklist_MouseMove(object sender, MouseEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None) return;   // Ctrl: the wikilink hand
            bool over = _currentId >= 0 && !_currentInTrash && BoxUnder(e.GetPosition(Editor)) != null;
            if (over) { Editor.Cursor = Cursors.Hand; _checkHand = true; }
            else if (_checkHand) { Editor.Cursor = null; _checkHand = false; }
        }

        // ---- Enter continues the list ----

        private void Checklist_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Return || Keyboard.Modifiers != ModifierKeys.None) return;
            if (_currentId < 0 || _currentInTrash || WikiPopupOpen) return;
            if (Editor.CaretPosition.Paragraph is not Paragraph p) return;
            bool md = CurrentIsMarkdown;
            string text = TextOf(p);
            if (Checklist.Of(text, md) == Checklist.State.None) return;

            // Enter on an item holding nothing but its box ends the list.
            if (text.Trim().Length <= Checklist.PrefixLength(text, md))
            {
                RemovePrefix(p, md);
                MarkDirty();
                e.Handled = true;
                return;
            }

            // Otherwise the editor splits the paragraph as usual, and the new one is seeded
            // with an empty box once that has happened.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Editor.CaretPosition.Paragraph is not Paragraph np || ReferenceEquals(np, p)) return;
                if (Checklist.Of(TextOf(np), md) != Checklist.State.None) return;
                string prefix = Checklist.Prefix(md, false);
                InsertPrefix(np, prefix);
                Editor.CaretPosition = Advance(Head(np), prefix.Length);
                MarkDirty();
            }), DispatcherPriority.Input);
        }
    }
}

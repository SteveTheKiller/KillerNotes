// ═══════════════════════════════════════════════════════════
//  [[ AUTOCOMPLETE  -  pick a note instead of remembering its title
// ═══════════════════════════════════════════════════════════
//
// Without this, wikilinks only work if you can spell a note's title from memory, and a link that
// misses by one character silently becomes a link to a note that does not exist. This is what
// makes the feature usable rather than merely present.
//
// The popup is a READER of the text, like everything else in the wikilink work: it watches what
// was typed, and when it inserts, it inserts plain characters. No element is created, nothing is
// marked up, and the note on disk is the text you see.
//
// The one genuinely fiddly part is finding where "[[" is, so the query can be replaced. Counting
// backwards with GetPositionAtOffset would be wrong: offsets there are SYMBOLS, and the syntax
// highlighter splits paragraphs into many Runs, so element edges between the caret and the "["
// shift the count. PointerBack below walks actual text runs instead, which is the same lesson
// ParagraphCodeText records in SyntaxHighlighting.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private Popup? _wlPopup;
        private ListBox? _wlList;
        private int _wlQueryLen;      // characters of query after the "[[", so the insert knows what to replace

        private void InitWikiLinkComplete()
        {
            _wlList = new ListBox { MaxHeight = 220, MinWidth = 220, FontSize = 12 };
            _wlList.MouseLeftButtonUp += (_, _) => AcceptWikiCompletion();
            _wlPopup = new Popup
            {
                Child = new Border
                {
                    Child = _wlList,
                    Background = (System.Windows.Media.Brush?)Application.Current?.TryFindResource("MenuBackgroundBrush"),
                    BorderBrush = (System.Windows.Media.Brush?)Application.Current?.TryFindResource("MenuBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2),
                },
                Placement = PlacementMode.RelativePoint,
                PlacementTarget = Editor,
                StaysOpen = false,
                AllowsTransparency = true,
            };

            Editor.TextChanged += (_, _) => UpdateWikiCompletion();
            // PREVIEW, so the arrows and Enter drive the list instead of the caret while it is up.
            Editor.PreviewKeyDown += WikiCompletion_PreviewKeyDown;
            Editor.LostKeyboardFocus += (_, _) => CloseWikiCompletion();
        }

        private bool WikiPopupOpen => _wlPopup?.IsOpen == true;

        private void CloseWikiCompletion()
        {
            _wlPopup?.IsOpen = false;
        }

        private void WikiCompletion_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!WikiPopupOpen || _wlList == null) return;
            switch (e.Key)
            {
                case Key.Escape:
                    CloseWikiCompletion(); e.Handled = true; break;
                case Key.Down:
                    _wlList.SelectedIndex = Math.Min(_wlList.Items.Count - 1, _wlList.SelectedIndex + 1);
                    _wlList.ScrollIntoView(_wlList.SelectedItem); e.Handled = true; break;
                case Key.Up:
                    _wlList.SelectedIndex = Math.Max(0, _wlList.SelectedIndex - 1);
                    _wlList.ScrollIntoView(_wlList.SelectedItem); e.Handled = true; break;
                case Key.Enter:
                case Key.Tab:
                    AcceptWikiCompletion(); e.Handled = true; break;
            }
        }

        /// <summary>Re-reads the text behind the caret after every edit and opens, refilters, or
        /// closes the list. Cheap: it only ever looks at the current paragraph.</summary>
        private void UpdateWikiCompletion()
        {
            if (_wlPopup == null || _wlList == null || _loadingNote) return;
            if (NoteStore.IsReadOnly) { CloseWikiCompletion(); return; }

            string? query = WikiQueryBeforeCaret();
            if (query == null) { CloseWikiCompletion(); return; }

            _wlQueryLen = query.Length;
            var hits = NoteStore.TitlesStartingWith(query);
            // Never offer the note being edited: linking a note to itself is not an edge, and the
            // graph drops it anyway.
            hits = [.. hits.Where(h => h.Id != _currentId)];
            if (hits.Count == 0) { CloseWikiCompletion(); return; }

            _wlList.ItemsSource = hits.Select(h => h.Title).ToList();
            _wlList.SelectedIndex = 0;
            PlaceWikiPopupAtCaret();
            _wlPopup.IsOpen = true;
        }

        /// <summary>The text between an unclosed "[[" and the caret, or null when the caret is not
        /// inside one. Paragraph-local, and a "]]" between the two closes the link, so a completed
        /// link does not reopen the list when you type after it.</summary>
        private string? WikiQueryBeforeCaret()
        {
            var caret = Editor.CaretPosition;
            var para = caret?.Paragraph;
            if (caret == null || para == null) return null;
            string before = new TextRange(para.ContentStart, caret).Text;
            int open = before.LastIndexOf("[[", StringComparison.Ordinal);
            if (open < 0) return null;
            string query = before[(open + 2)..];
            if (query.Contains(']') || query.Contains('\n') || query.Contains('\r')) return null;
            return query.Length > 100 ? null : query;   // not a title any more, stop offering
        }

        private void PlaceWikiPopupAtCaret()
        {
            if (_wlPopup == null) return;
            try
            {
                var r = Editor.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                // Offsets are in the Editor's own coordinate space and so is the caret rect, so
                // the app-wide scale applies to both equally and needs no compensation here -
                // unlike a context menu, which is placed from the mouse in screen space.
                _wlPopup.HorizontalOffset = r.Left;
                _wlPopup.VerticalOffset = r.Bottom + 2;
            }
            catch (InvalidOperationException) { /* caret between documents mid-load */ }
        }

        /// <summary>Replaces "[[query" with "[[Title]]" and puts the caret after it.</summary>
        private void AcceptWikiCompletion()
        {
            if (_wlList?.SelectedItem is not string title) { CloseWikiCompletion(); return; }
            var caret = Editor.CaretPosition;
            // +2 for the brackets themselves, so the whole "[[query" is replaced rather than
            // leaving a second pair behind.
            var start = PointerBack(caret, _wlQueryLen + 2);
            CloseWikiCompletion();
            if (start == null) return;

            var range = new TextRange(start, caret);
            // ONE undo unit: accepting a completion is a single action to the person doing it, so
            // Ctrl+Z takes the whole link back rather than peeling characters off it.
            Editor.BeginChange();
            try
            {
                range.Text = WikiLinks.Wrap(title);
                Editor.CaretPosition = range.End;
            }
            finally { Editor.EndChange(); }
            MarkDirty();
        }

        /// <summary>A pointer N TEXT CHARACTERS back from here, walking runs rather than symbol
        /// offsets so element boundaries between them cannot shift the count.</summary>
        private static TextPointer? PointerBack(TextPointer from, int chars)
        {
            var p = from;
            int guard = 0;
            while (chars > 0 && p != null && guard++ < 10000)
            {
                if (p.GetPointerContext(LogicalDirection.Backward) == TextPointerContext.Text)
                {
                    int run = p.GetTextRunLength(LogicalDirection.Backward);
                    if (run <= 0) { p = p.GetNextContextPosition(LogicalDirection.Backward); continue; }
                    int take = Math.Min(run, chars);
                    p = p.GetPositionAtOffset(-take, LogicalDirection.Backward);
                    chars -= take;
                }
                else
                {
                    var next = p.GetNextContextPosition(LogicalDirection.Backward);
                    if (next == null) return null;
                    p = next;
                }
            }
            return chars == 0 ? p : null;
        }
    }
}

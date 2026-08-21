using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KillerNotes.Shell
{
    // ═══════════════════════════════════════════════════════════
    //  IN-NOTE FIND  -  Ctrl+F, the find bar over the editor
    // ═══════════════════════════════════════════════════════════
    // Searches the OPEN note. The sidebar box stays what it always was: cross-note full-text
    // search over the store. F3 keeps focusing the sidebar while this bar is closed, and steps
    // matches while it is open - the split every other editor uses.
    //
    // TWO WALKS, AND ONLY ONE OF THEM IS DANGEROUS. LineNumbers.cs documents the #16 hang: it
    // walked VISUAL lines (GetLineStartPosition) on every event, and a visual walk forces WPF to
    // lay out and format every line it crosses, which is minutes of Not Responding on a big note.
    // This file never does that. Finding matches is a CONTENT walk - TextPointer.GetTextInRun over
    // the symbol tree - which reads what is already in memory and forces no layout at all, so it
    // is safe to run across the whole document. The only viewport-limited thing here is the
    // PAINTING, which is limited because drawing is per-frame work, not because reading is unsafe.
    //
    // The document is never modified. Matches are drawn by an adorner, exactly as
    // EditorSelectionText.cs draws white selected text: no ApplyPropertyValue, no formatting runs,
    // so the undo stack, the dirty flag, autosave and syntax highlighting never see any of this.
    // The worst failure mode is a cosmetic misdraw for one frame.
    public partial class MainWindow
    {
        private bool _findOpen;
        private string _findTerm = "";
        private int _findIndex = -1;                       // -1 = nothing current
        private readonly List<(int Start, int Length)> _findHits = [];

        // Match options (#14): case, whole word, regex. They govern find AND replace, and are
        // session state, not settings - a find bar reopens with plain matching, the default
        // every editor's does.
        private bool _findCase, _findWord, _findRegex;

        /// <summary>True while the replace row is expanded (Ctrl+H).</summary>
        private bool _replaceOpen;

        // Flattened document text plus the map back into it. Rebuilt only when the document
        // actually changed - a term edit reuses it, which is what keeps typing in the find box
        // from re-walking the note on every keystroke.
        private string _findPlain = "";
        private bool _findPlainStale = true;
        private readonly List<(int Offset, TextPointer Start, int Length)> _findRuns = [];

        private const double FindBarMs = 160;

        // Floating-card placement. The bar is dragged by its grip anywhere inside the note
        // body and remembers where it was left, under Software\KillerNotes\Settings the same
        // way the format bar and the app scale do.
        private const string FindBarKey = "FindBar";
        private const double FindBoxMin = 60, FindBoxMax = 320;
        // Grip + glyph + count + three buttons + the card's paddings and margins: what is NOT
        // the text box, so the box can be sized from whatever width is left.
        // 246, not 240: the find box gained a wrapper border when it became a proper field
        // (1px each side plus a 4px right margin), so the fit has that much less room to give.
        // +78 in 1.2.2 for the three match-option toggles (26px each).
        private const double FindBarChrome = 324;

        private (double StartX, double StartY, Thickness Orig)? _findDrag;
        private bool _findPlaced;   // the saved spot is applied once, on the first open

        // ── Cache ────────────────────────────────────────────────

        /// <summary>
        /// Called from Editor_TextChanged. Marks the flattened text stale rather than rebuilding:
        /// an edit while the bar is open should not pay for a walk until something actually asks
        /// for a match.
        /// </summary>
        private void InvalidateFindCache()
        {
            _findPlainStale = true;
            if (_findOpen) RunFind(keepIndex: true);
        }

        /// <summary>
        /// Flatten the document to a string, recording where each text run landed in it.
        ///
        /// GetTextInRun is a CONTENT read - it never forces layout - so this is safe over the
        /// whole note however large. Embedded objects (images, recording chips) contribute no
        /// characters, which is correct: there is nothing in them to match.
        /// </summary>
        private void RebuildFindPlain()
        {
            _findRuns.Clear();
            var sb = new System.Text.StringBuilder();

            TextPointer? p = Editor.Document.ContentStart;
            while (p != null)
            {
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string run = p.GetTextInRun(LogicalDirection.Forward);
                    if (run.Length > 0)
                    {
                        _findRuns.Add((sb.Length, p, run.Length));
                        sb.Append(run);
                    }
                }
                else if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementEnd
                         && p.Parent is Paragraph)
                {
                    // One separator per paragraph so a term cannot match across a line break.
                    sb.Append('\n');
                }
                p = p.GetNextContextPosition(LogicalDirection.Forward);
            }

            _findPlain = sb.ToString();
            _findPlainStale = false;
        }

        /// <summary>
        /// Offset in the flattened text back to a live TextPointer. Binary search over the run
        /// table, then GetPositionAtOffset INSIDE that run - so this costs a log rather than a
        /// walk, and it cannot drift across a run boundary the way a document-wide offset would.
        /// </summary>
        private TextPointer? PointerForOffset(int offset)
        {
            int lo = 0, hi = _findRuns.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var r = _findRuns[mid];
                if (offset < r.Offset) hi = mid - 1;
                else if (offset >= r.Offset + r.Length) lo = mid + 1;
                else { found = mid; break; }
            }
            if (found < 0) return null;
            var run = _findRuns[found];
            return run.Start.GetPositionAtOffset(offset - run.Offset, LogicalDirection.Forward);
        }

        // ── Searching ────────────────────────────────────────────

        /// <summary>
        /// Re-run the current term over the whole note. <paramref name="keepIndex"/> holds the
        /// current match position across an edit so typing in the note does not throw you back to
        /// the first hit.
        /// </summary>
        private void RunFind(bool keepIndex = false)
        {
            int wasAt = keepIndex && _findIndex >= 0 && _findIndex < _findHits.Count
                        ? _findHits[_findIndex].Start : -1;

            _findHits.Clear();
            _findIndex = -1;

            if (_findTerm.Length > 0)
            {
                if (_findPlainStale) RebuildFindPlain();
                CollectFindHits(_findPlain, _findHits);

                if (_findHits.Count > 0)
                {
                    _findIndex = 0;
                    if (wasAt >= 0)
                        for (int i = 0; i < _findHits.Count; i++)
                            if (_findHits[i].Start >= wasAt) { _findIndex = i; break; }
                }
            }

            UpdateFindCount();
            RepaintFindMatches();
        }

        /// <summary>Fills <paramref name="hits"/> with the current term's matches over
        /// <paramref name="plain"/>, honoring the three option toggles. Shared by in-note find
        /// and the cross-note replace scan (GlobalReplace.cs), so the two can never disagree
        /// about what matches. Case folding stays Ordinal: a note is as likely to hold a path
        /// or a command as prose, and culture folding turns some of those into surprises.</summary>
        internal void CollectFindHits(string plain, List<(int Start, int Length)> hits)
        {
            if (_findTerm.Length == 0) return;

            if (_findRegex)
            {
                var rx = TryBuildFindRegex();
                if (rx == null) return;   // invalid pattern: zero hits, the count line reads no matches
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(plain))
                    if (m.Length > 0)     // an empty match cannot be stepped or replaced
                        hits.Add((m.Index, m.Length));
                return;
            }

            var cmp = _findCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int at = 0;
            while (at <= plain.Length - _findTerm.Length)
            {
                int hit = plain.IndexOf(_findTerm, at, cmp);
                if (hit < 0) break;
                if (!_findWord || IsWholeWordAt(plain, hit, _findTerm.Length))
                    hits.Add((hit, _findTerm.Length));
                at = hit + 1;   // overlapping matches count, the same as a text editor's do
            }
        }

        private static bool IsWholeWordAt(string plain, int start, int length)
        {
            static bool WordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
            if (start > 0 && WordChar(plain[start - 1])) return false;
            int end = start + length;
            return end >= plain.Length || !WordChar(plain[end]);
        }

        /// <summary>The term as a Regex under the current options, or null when the pattern is
        /// invalid. Whole word wraps the pattern in \b anchors, same as every editor's combo.</summary>
        private System.Text.RegularExpressions.Regex? TryBuildFindRegex()
        {
            var opts = System.Text.RegularExpressions.RegexOptions.None;
            if (!_findCase) opts |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            string pattern = _findWord ? @"\b(?:" + _findTerm + @")\b" : _findTerm;
            try { return new System.Text.RegularExpressions.Regex(pattern, opts); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>Step to the next or previous match, wrapping at either end.</summary>
        private void StepFind(int delta)
        {
            if (_findHits.Count == 0) return;
            _findIndex = ((_findIndex + delta) % _findHits.Count + _findHits.Count) % _findHits.Count;
            ScrollToCurrentFind();
            UpdateFindCount();
            RepaintFindMatches();
        }

        /// <summary>
        /// Bring the current match on screen WITHOUT moving focus or the caret. Selecting it would
        /// be the obvious route and is wrong twice over: it steals the caret from the find box, and
        /// a selection change is what the syntax and selection overlays hang off.
        /// </summary>
        private void ScrollToCurrentFind()
        {
            if (_findIndex < 0 || _findIndex >= _findHits.Count) return;
            var p = PointerForOffset(_findHits[_findIndex].Start);
            if (p == null) return;
            try
            {
                Rect r = p.GetCharacterRect(LogicalDirection.Forward);
                if (r.IsEmpty) return;
                // Only scroll when it is actually off screen, so stepping between two matches on
                // the same screen does not jog the view.
                if (r.Top < 0 || r.Bottom > Editor.ActualHeight)
                    Editor.ScrollToVerticalOffset(
                        Editor.VerticalOffset + r.Top - Editor.ActualHeight / 3);
            }
            catch { }
        }

        private void UpdateFindCount()
        {
            if (FindCountText == null) return;
            FindCountText.Text = _findTerm.Length == 0
                ? ""
                : _findHits.Count == 0
                    ? Loc("Str_St_FindNoMatches")
                    : string.Format(Loc("Str_St_FindMatches"), _findIndex + 1, _findHits.Count);
        }

        private void RepaintFindMatches()
        {
            var layer = AdornerLayer.GetAdornerLayer(Editor);
            if (layer?.GetAdorners(Editor) is { } list)
                foreach (var a in list)
                    if (a is FindMatchAdorner f) { f.InvalidateVisual(); return; }
        }

        // ── Open / close ─────────────────────────────────────────

        private void InitFindBar()
        {
            Editor.Loaded += (_, _) =>
            {
                var layer = AdornerLayer.GetAdornerLayer(Editor);
                if (layer == null) return;   // no AdornerDecorator above the editor: no highlights, nothing breaks
                var existing = layer.GetAdorners(Editor);
                if (existing != null)
                    foreach (var a in existing)
                        if (a is FindMatchAdorner) return;   // Loaded refires; never stack two
                layer.Add(new FindMatchAdorner(Editor, this));
            };

            EnableFindBarDrag(FindGrip);

            // The card floats inside the note body, so a resize of that area has to shrink the
            // text box and pull the card back inside. Covers window resizes, the sidebar
            // splitter, and the preview pane opening.
            EditorArea.SizeChanged += (_, _) =>
            {
                FitFindBox();
                if (FindBar.Visibility == Visibility.Visible) ClampFindBar();
            };
        }

        // ── Floating placement (KillerPDF's search bar) ───────────

        /// <summary>Clamps a Left/Top so the whole card stays inside the note body.</summary>
        private void ClampFindBar(ref double left, ref double top)
        {
            double w = FindBar.ActualWidth  > 0 ? FindBar.ActualWidth  : 0;
            double h = FindBar.ActualHeight > 0 ? FindBar.ActualHeight : 0;
            left = Math.Max(0, Math.Min(Math.Max(0, EditorArea.ActualWidth  - w), left));
            top  = Math.Max(0, Math.Min(Math.Max(0, EditorArea.ActualHeight - h), top));
        }

        /// <summary>Re-clamps where the card already is, after the area it floats in changed.</summary>
        private void ClampFindBar()
        {
            double l = FindBar.Margin.Left, t = FindBar.Margin.Top;
            ClampFindBar(ref l, ref t);
            FindBar.Margin = new Thickness(l, t, 0, 0);
        }

        /// <summary>
        /// Puts the card back where it was left, or at the top right of the note body the
        /// first time. Must run after layout, or the card has no width to clamp against.
        /// </summary>
        private void ApplySavedFindBarPosition()
        {
            var inv = CultureInfo.InvariantCulture;
            double left, top;
            if (int.TryParse(App.GetSetting(FindBarKey + "Left"), NumberStyles.Integer, inv, out int sl) &&
                int.TryParse(App.GetSetting(FindBarKey + "Top"),  NumberStyles.Integer, inv, out int st))
            {
                left = sl; top = st;
            }
            else
            {
                left = EditorArea.ActualWidth - FindBar.ActualWidth - 16;
                top  = 6;
            }
            ClampFindBar(ref left, ref top);
            FindBar.Margin = new Thickness(left, top, 0, 0);
        }

        /// <summary>Sizes the text box to whatever is left of the note body's width after the
        /// card's own furniture, so the bar can never be wider than the area it floats in.</summary>
        private void FitFindBox()
        {
            if (FindBox == null) return;
            FindBox.Width = Math.Max(FindBoxMin,
                Math.Min(FindBoxMax, EditorArea.ActualWidth - FindBarChrome));
        }

        /// <summary>Grip drag: moves the card inside the note body and persists where it lands.</summary>
        private void EnableFindBarDrag(FrameworkElement handle)
        {
            handle.MouseLeftButtonDown += (_, e) =>
            {
                var p = e.GetPosition(EditorArea);
                _findDrag = (p.X, p.Y, FindBar.Margin);
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += (_, e) =>
            {
                if (_findDrag == null || !handle.IsMouseCaptured) return;
                var d = _findDrag.Value;
                var p = e.GetPosition(EditorArea);
                double l = d.Orig.Left + (p.X - d.StartX);
                double t = d.Orig.Top  + (p.Y - d.StartY);
                ClampFindBar(ref l, ref t);
                FindBar.Margin = new Thickness(l, t, 0, 0);
            };
            handle.MouseLeftButtonUp += (_, e) =>
            {
                if (_findDrag == null) return;
                _findDrag = null;
                handle.ReleaseMouseCapture();
                var inv = CultureInfo.InvariantCulture;
                App.SetSetting(FindBarKey + "Left", ((int)FindBar.Margin.Left).ToString(inv));
                App.SetSetting(FindBarKey + "Top",  ((int)FindBar.Margin.Top).ToString(inv));
                e.Handled = true;
            };
        }

        /// <summary>The rail's find toggle. A rail toggle is a switch, so a second click closes
        /// the bar rather than refocusing it, which is what Ctrl+F does instead.</summary>
        private void FindRail_Click(object sender, RoutedEventArgs e) => ToggleFindBar();

        internal void ToggleFindBar()
        {
            if (_findOpen) CloseFindBar();
            else OpenFindBar();
        }

        /// <summary>Ctrl+F. Opens the bar, or refocuses it when it is already open.</summary>
        internal void OpenFindBar()
        {
            if (_findOpen) { FindBox.Focus(); FindBox.SelectAll(); return; }

            _findOpen = true;
            FindRailBtn.Tag = "on";   // light the rail toggle while the bar is open (family pattern)
            // A card that floats has no edge to slide out of, so it fades in. Clear any held
            // animation first or the Opacity write below is ignored.
            FindBar.BeginAnimation(UIElement.OpacityProperty, null);
            FindBar.Opacity = 0;
            FindBar.Visibility = Visibility.Visible;

            // Seed from the editor's selection, the way every find box does, so Ctrl+F on a
            // selected word searches for it immediately. Single-line selections only: a
            // multi-line selection as a search term is never what was meant.
            string sel = Editor.Selection?.Text ?? "";
            if (sel.Length is > 0 and < 120 && !sel.Contains('\n'))
                FindBox.Text = sel;

            // Place it once it has been laid out and has a real width to clamp, then fade in.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FitFindBox();
                FindBar.UpdateLayout();
                if (!_findPlaced) { ApplySavedFindBarPosition(); _findPlaced = true; }
                else ClampFindBar();

                var fade = new System.Windows.Media.Animation.DoubleAnimation(
                    0, 1, TimeSpan.FromMilliseconds(FindBarMs))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                FindBar.BeginAnimation(UIElement.OpacityProperty, fade);
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            FindBox.Focus();
            FindBox.SelectAll();
            _findTerm = FindBox.Text;
            RunFind();
        }

        /// <summary>Esc, or the bar's own close button. Hooked into HandleEscape BEFORE the
        /// sidebar SearchBox branch, or closing this would clear the cross-note search instead.</summary>
        internal void CloseFindBar()
        {
            if (!_findOpen) return;
            _findOpen = false;
            FindRailBtn.Tag = null;   // clear the rail toggle's lit state

            // The replace row does not survive a close: the next Ctrl+F opens plain find, and
            // only Ctrl+H brings the row back (#14).
            _replaceOpen = false;
            ReplaceRow.Visibility = Visibility.Collapsed;

            // Fade out rather than blink away; on completion collapse it and drop the animation
            // so the next open starts from a clean Opacity.
            var fade = new System.Windows.Media.Animation.DoubleAnimation(
                FindBar.Opacity, 0, TimeSpan.FromMilliseconds(FindBarMs - 20))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            fade.Completed += (_, _) =>
            {
                // Esc then Ctrl+F inside the fade: the bar is open again and its fade-in is
                // already running, so this must not collapse it back.
                if (_findOpen) return;
                FindBar.Visibility = Visibility.Collapsed;
                FindBar.BeginAnimation(UIElement.OpacityProperty, null);
                FindBar.Opacity = 0;
            };
            FindBar.BeginAnimation(UIElement.OpacityProperty, fade);

            _findHits.Clear();
            _findIndex = -1;
            RepaintFindMatches();
            Editor.Focus();
        }

        // ── Bar event handlers (wired in MainWindow.xaml) ─────────

        private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _findTerm = FindBox.Text ?? "";
            RunFind();
            ScrollToCurrentFind();
        }

        private void FindNext_Click(object sender, RoutedEventArgs e) => StepFind(1);
        private void FindPrev_Click(object sender, RoutedEventArgs e) => StepFind(-1);
        private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar();

        /// <summary>Enter steps forward, Shift+Enter back - the find box's own convention, kept
        /// separate from the window-level F3 so it works while the box has the keyboard.</summary>
        private void FindBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            StepFind((System.Windows.Input.Keyboard.Modifiers
                      & System.Windows.Input.ModifierKeys.Shift) != 0 ? -1 : 1);
            e.Handled = true;
        }

        // ── Replace (#14) ────────────────────────────────────────

        /// <summary>Ctrl+H. Opens the bar (or keeps it) with the replace row expanded, and puts
        /// the keyboard in the replace box.</summary>
        internal void OpenReplaceBar()
        {
            if (_currentId < 0 || Services.NoteStore.IsReadOnly) return;
            OpenFindBar();
            if (!_replaceOpen)
            {
                _replaceOpen = true;
                ReplaceRow.Visibility = Visibility.Visible;
                // The card just got taller; pull it back inside the note body if it was parked
                // against the bottom edge. After layout, so the new height is real.
                Dispatcher.BeginInvoke(new Action(ClampFindBar),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            ReplaceBox.Focus();
            ReplaceBox.SelectAll();
        }

        /// <summary>What the given hit becomes: the replace box verbatim, or for regex the
        /// pattern's substitution ($1 groups and friends) evaluated against that hit.</summary>
        private string ReplacementFor((int Start, int Length) hit)
        {
            string repl = ReplaceBox.Text ?? "";
            if (!_findRegex) return repl;
            var rx = TryBuildFindRegex();
            if (rx == null) return repl;
            var m = rx.Match(_findPlain, hit.Start, hit.Length);
            try { return m.Success ? m.Result(repl) : repl; }
            catch (ArgumentException) { return repl; }   // malformed substitution: keep it literal
        }

        /// <summary>Replaces the CURRENT match and steps to the next one.</summary>
        private void ReplaceCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (Services.NoteStore.IsReadOnly) return;
            if (_findIndex < 0 || _findIndex >= _findHits.Count) return;

            var hit = _findHits[_findIndex];
            string repl = ReplacementFor(hit);
            var a = PointerForOffset(hit.Start);
            var b = a?.GetPositionAtOffset(hit.Length, LogicalDirection.Forward);
            if (a == null || b == null) return;

            // The edit fires TextChanged, whose InvalidateFindCache re-runs the find with
            // keepIndex - which would land back INSIDE the replacement whenever it still
            // contains the term ("a" -> "aa"). Step explicitly to the first hit past the
            // replacement instead, so repeated clicks always move forward.
            int resumeAt = hit.Start + repl.Length;
            new TextRange(a, b).Text = repl;

            if (_findHits.Count > 0)
            {
                _findIndex = 0;
                for (int i = 0; i < _findHits.Count; i++)
                    if (_findHits[i].Start >= resumeAt) { _findIndex = i; break; }
                UpdateFindCount();
                RepaintFindMatches();
            }
            ScrollToCurrentFind();
        }

        /// <summary>Replaces every match in the note as ONE editor undo unit. Offsets were
        /// collected once by the find pass; they are applied BACK-TO-FRONT so each earlier
        /// offset is still valid when its turn comes (#14).</summary>
        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            if (Services.NoteStore.IsReadOnly) return;
            if (_findHits.Count == 0) return;

            // The find pass counts OVERLAPPING matches; a replace can only consume each
            // character once, so overlaps collapse greedily left-to-right first.
            var hits = new List<(int Start, int Length)>(_findHits.Count);
            int lastEnd = -1;
            foreach (var h in _findHits)
            {
                if (h.Start < lastEnd) continue;
                hits.Add(h);
                lastEnd = h.Start + h.Length;
            }
            var repls = new List<string>(hits.Count);
            foreach (var h in hits) repls.Add(ReplacementFor(h));

            // BeginChange batches everything inside into a single undo unit and holds the
            // TextChanged storm until EndChange, so the cache invalidation runs once.
            Editor.BeginChange();
            try
            {
                for (int i = hits.Count - 1; i >= 0; i--)
                {
                    var a = PointerForOffset(hits[i].Start);
                    var b = a?.GetPositionAtOffset(hits[i].Length, LogicalDirection.Forward);
                    if (a == null || b == null) continue;
                    new TextRange(a, b).Text = repls[i];
                }
            }
            finally { Editor.EndChange(); }

            FlashStatus(string.Format(Loc("Str_St_Replaced"), hits.Count));
        }

        /// <summary>Toggles one of the three match options; the family Tag="on" lights it.</summary>
        private void FindOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b) b.Tag = (b.Tag as string) == "on" ? null : "on";
            _findCase  = (FindCaseBtn.Tag as string) == "on";
            _findWord  = (FindWordBtn.Tag as string) == "on";
            _findRegex = (FindRegexBtn.Tag as string) == "on";
            RunFind();
            ScrollToCurrentFind();
        }

        /// <summary>Enter in the replace box replaces the current match; Ctrl+Enter replaces all.</summary>
        private void ReplaceBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            if ((System.Windows.Input.Keyboard.Modifiers
                 & System.Windows.Input.ModifierKeys.Control) != 0)
                ReplaceAll_Click(this, new RoutedEventArgs());
            else
                ReplaceCurrent_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }

        // Read by the adorner, which lives outside this class.
        internal bool FindIsOpen => _findOpen;
        internal IReadOnlyList<(int Start, int Length)> FindHits => _findHits;
        internal int FindCurrentIndex => _findIndex;
        internal TextPointer? FindPointerFor(int offset) => PointerForOffset(offset);
    }

    /// <summary>
    /// Paints the match highlights. Same contract as SelectionTextAdorner: hit-test invisible,
    /// never touches the document, and clamps to the viewport - GetCharacterRect on a position
    /// off screen forces WPF to format the lines in between, which is the #16 hang.
    /// </summary>
    internal sealed class FindMatchAdorner : Adorner
    {
        private readonly RichTextBox _rtb;
        private readonly MainWindow _win;

        public FindMatchAdorner(RichTextBox rtb, MainWindow win) : base(rtb)
        {
            _rtb = rtb;
            _win = win;
            IsHitTestVisible = false;
            rtb.TextChanged += (_, _) => InvalidateVisual();
            rtb.SizeChanged += (_, _) => InvalidateVisual();
            rtb.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
        }

        protected override void OnRender(DrawingContext dc)
        {
            try { RenderMatches(dc); }
            catch { }   // a render pass must never take the app down
        }

        private void RenderMatches(DrawingContext dc)
        {
            if (!_win.FindIsOpen || _win.FindHits.Count == 0 || !_rtb.IsLoaded) return;

            Brush fill = Application.Current?.TryFindResource("FindMatchBrush") as Brush
                         ?? new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD5, 0x4F));
            Brush current = Application.Current?.TryFindResource("FindCurrentMatchBrush") as Brush
                            ?? new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0x8A, 0x00));

            // The visible slice, as offsets. Everything outside it is skipped without ever asking
            // for its rectangle, which is what keeps this off the formatting path.
            var topPos = _rtb.GetPositionFromPoint(new Point(0, 0), true);
            var bottomPos = _rtb.GetPositionFromPoint(
                new Point(Math.Max(0, _rtb.ActualWidth - 1), Math.Max(0, _rtb.ActualHeight - 1)), true);
            if (topPos == null || bottomPos == null) return;

            dc.PushClip(new RectangleGeometry(new Rect(_rtb.RenderSize)));

            for (int i = 0; i < _win.FindHits.Count; i++)
            {
                var hit = _win.FindHits[i];
                TextPointer? a = _win.FindPointerFor(hit.Start);
                if (a == null || a.CompareTo(topPos) < 0 || a.CompareTo(bottomPos) > 0) continue;
                TextPointer? b = a.GetPositionAtOffset(hit.Length, LogicalDirection.Forward);
                if (b == null) continue;

                Rect ra = a.GetCharacterRect(LogicalDirection.Forward);
                Rect rb = b.GetCharacterRect(LogicalDirection.Backward);
                if (ra.IsEmpty) continue;

                // One rectangle per match, and only when it did not wrap: a wrapped match would
                // need a band per visual line, and asking for those means walking lines. A
                // wrapped hit is drawn as its first line only rather than paying that price.
                double right = (rb.IsEmpty || Math.Abs(rb.Top - ra.Top) > 0.5) ? _rtb.ActualWidth : rb.Right;
                dc.DrawRectangle(i == _win.FindCurrentIndex ? current : fill, null,
                                 new Rect(ra.Left, ra.Top, Math.Max(1, right - ra.Left), ra.Height));
            }

            dc.Pop();
        }
    }
}

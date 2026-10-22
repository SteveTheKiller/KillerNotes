using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // ═══════════════════════════════════════════════════════════
    //  CROSS-NOTE REPLACE (#14) - the floating row beneath Search
    // ═══════════════════════════════════════════════════════════
    // The sidebar search box already answers "which notes carry this text"; this row is the
    // write half: replace that term inside every note that carries it. The term is taken
    // literally and case-insensitively, mirroring the sidebar search it sits under - the find
    // bar's regex/word options belong to the IN-NOTE surface and deliberately do not reach
    // over here.
    //
    // Matching runs over each note's DOCUMENT text, never the stored plain column: the stored
    // plain also carries sketch text labels (Editor.Sketch.cs), which a text replace must not
    // rewrite - they live inside sketch payloads, not in the note body.
    //
    // The whole operation is one SQLite transaction (NoteStore.UpdateContents) and ONE
    // app-level undo entry (ActionUndo.cs): Ctrl+Z after a 40-note replace puts all 40 back.
    public partial class MainWindow
    {
        private bool _sidebarReplaceOpen;
        private int _sidebarReplaceAnimationVersion;

        /// <summary>Shows the cross-note replace row directly beneath Search.</summary>
        private void SidebarReplace_Click(object sender, RoutedEventArgs e)
        {
            bool open = !_sidebarReplaceOpen;
            SetSidebarReplaceOpen(open);
            if (!open) { SidebarReplaceBtn.Focus(); return; }
            if (SearchBox.Text.Length == 0) SearchBox.Focus();
            else SidebarReplaceBox.Focus();
        }

        private void SetSidebarReplaceOpen(bool open)
        {
            if (_sidebarReplaceOpen == open) return;
            _sidebarReplaceOpen = open;
            int version = ++_sidebarReplaceAnimationVersion;
            SidebarReplaceBtn.Tag = open ? "on" : null;
            TitleSearchFlyout.Width = TitleNoteSearch.ActualWidth + 8;
            TitleSearchFlyout.Height = TitleSearchSlot.ActualHeight + 34;
            SidebarReplaceRow.IsHitTestVisible = open;
            TitleSearchFlyout.IsHitTestVisible = open;
            if (open)
            {
                SidebarReplaceRow.Visibility = Visibility.Visible;
                TitleSearchFlyout.Visibility = Visibility.Visible;
            }
            bool animate = SystemParameters.ClientAreaAnimation;
            var duration = new Duration(TimeSpan.FromMilliseconds(animate ? 140 : 0));
            var easing = new System.Windows.Media.Animation.CubicEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            foreach (var element in new FrameworkElement[] { TitleSearchFlyout, SidebarReplaceRow })
            {
                double current = element.Opacity;
                double target = open ? 1 : 0;
                element.Opacity = target;
                var fade = new System.Windows.Media.Animation.DoubleAnimation(current, target, duration)
                    { EasingFunction = easing, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
                if (!open) fade.Completed += (_, _) =>
                {
                    if (version == _sidebarReplaceAnimationVersion && !_sidebarReplaceOpen)
                        element.Visibility = Visibility.Collapsed;
                };
                element.BeginAnimation(OpacityProperty, fade);
            }
            // The search box never moves: the panel fades in around it with the same 4px of panel
            // on every side, and the Replace row fades in with the panel as one piece. (Sliding the
            // panel and nudging the box up 2px left the box off-center against the panel's edge.)
            SidebarReplaceRow.RenderTransform = new System.Windows.Media.TranslateTransform(0, TitleSearchSlot.ActualHeight);
        }

        private void SidebarReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            if (NoteStore.IsReadOnly) return;
            // The notes come from the whole search, filters included; what is replaced is only its
            // words and phrases (Services/SearchQuery.cs), so "tag:work budget" replaces "budget".
            string search = SearchBox.Text ?? "";
            string term = SearchQuery.Parse(search).FreeText;
            string repl = SidebarReplaceBox.Text ?? "";
            if (term.Length == 0) { FlashStatus(Loc("Str_St_ReplaceNeedsTerm")); return; }

            // The open note takes part from the database like every other note, so its edits
            // must be on disk first; it is reloaded below if it was touched.
            if (!SaveCurrentNote(refreshList: false)) return;

            // ---- Scan: which notes, and how many matches in each ----
            var affected = new List<(long Id, string Title, string BodyBefore, FlowDocument Doc,
                                     List<(int Start, int Length)> Hits,
                                     List<(int Offset, TextPointer Start, int Length)> Runs)>();
            foreach (var note in NoteStore.List(search))
            {
                byte[]? blob = NoteStore.LoadContent(note.Id);
                if (blob == null) continue;
                FlowDocument doc;
                try { doc = LoadDocBlob(blob); }
                catch { continue; }   // an unreadable note is skipped, never corrupted
                var (plain, runs) = FlattenDoc(doc);
                var hits = LiteralHits(plain, term);
                if (hits.Count == 0) continue;
                affected.Add((note.Id, note.Title, plain, doc, hits, runs));
            }
            if (affected.Count == 0) { FlashStatus(Loc("Str_St_FindNoMatches")); return; }

            // ---- Confirmation: every affected note by name, with its match count ----
            int total = affected.Sum(a => a.Hits.Count);
            var lines = new System.Text.StringBuilder();
            const int listCap = 12;
            for (int i = 0; i < affected.Count && i < listCap; i++)
            {
                string title = string.IsNullOrWhiteSpace(affected[i].Title)
                    ? Loc("Str_Untitled") : affected[i].Title;
                lines.AppendLine($"{title}  ({affected[i].Hits.Count})");
            }
            if (affected.Count > listCap)
                lines.AppendLine(string.Format(Loc("Str_Dlg_ReplaceMore"), affected.Count - listCap));

            var dlg = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_ReplaceAllHead"), total, affected.Count),
                string.Format(Loc("Str_Dlg_ReplaceAllBody"), term, repl) + "\n\n" + lines.ToString().TrimEnd(),
                Loc("Str_Btn_ReplaceAll")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            // ---- Snapshot for the single undo entry, then rewrite and commit ----
            var before = new List<NoteStore.NoteRow>();
            foreach (var a in affected)
                if (NoteStore.CaptureRow(a.Id) is { } row) before.Add(row);

            var updates = new List<(long Id, byte[] Content, string Plain)>();
            foreach (var a in affected)
            {
                ApplyHits(a.Runs, a.Hits, repl);
                var whole = new TextRange(a.Doc.ContentStart, a.Doc.ContentEnd);
                using var ms = new MemoryStream();
                whole.Save(ms, DataFormats.XamlPackage);
                // Stored plain = document text plus the note's sketch labels, the same recipe
                // SaveCurrentNote uses, so search keeps finding labeled diagrams afterward.
                string stored = whole.Text;
                string labels = SketchLabelTextFor(a.Id);
                if (labels.Length > 0) stored = stored + "\n" + labels;
                updates.Add((a.Id, ms.ToArray(), stored));
            }
            NoteStore.UpdateContents(updates);
            foreach (var a in affected)
                NoteStore.SetLinks(a.Id, WikiLinks.Parse(new TextRange(a.Doc.ContentStart, a.Doc.ContentEnd).Text));

            long openId = _currentId;
            bool touchedOpen = openId >= 0 && affected.Any(a => a.Id == openId);
            RefreshList();
            if (touchedOpen) OpenNote(openId);
            RefreshBacklinks();

            PushUndo(() =>
            {
                NoteStore.RestoreContents(before);
                foreach (var a in affected) NoteStore.SetLinks(a.Id, WikiLinks.Parse(a.BodyBefore));
                long cur = _currentId;
                RefreshList();
                if (cur >= 0 && before.Any(r => r.Id == cur)) OpenNote(cur);
                RefreshBacklinks();
            });

            FlashStatus(string.Format(Loc("Str_St_ReplacedNotes"), total, affected.Count));
        }

        // ---- Helpers ----

        /// <summary>A XamlPackage blob as an off-screen FlowDocument.</summary>
        private static FlowDocument LoadDocBlob(byte[] blob)
        {
            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var ms = new MemoryStream(blob);
            range.Load(ms, DataFormats.XamlPackage);
            return doc;
        }

        /// <summary>Flattens a document to text plus the run map back into it - the same walk
        /// as the find bar's RebuildFindPlain (FindBar.cs), so offsets mean the same thing.</summary>
        private static (string Plain, List<(int Offset, TextPointer Start, int Length)> Runs)
            FlattenDoc(FlowDocument doc)
        {
            var runs = new List<(int Offset, TextPointer Start, int Length)>();
            var sb = new System.Text.StringBuilder();
            TextPointer? p = doc.ContentStart;
            while (p != null)
            {
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string run = p.GetTextInRun(LogicalDirection.Forward);
                    if (run.Length > 0)
                    {
                        runs.Add((sb.Length, p, run.Length));
                        sb.Append(run);
                    }
                }
                else if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement)
                    sb.Append(DocumentReplacement.ObjectBoundary);
                else if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementEnd
                         && p.Parent is Paragraph)
                {
                    sb.Append('\n');
                }
                p = p.GetNextContextPosition(LogicalDirection.Forward);
            }
            return (sb.ToString(), runs);
        }

        /// <summary>Non-overlapping, case-insensitive literal matches - each character can be
        /// replaced at most once, unlike the find bar's overlapping COUNT.</summary>
        private static List<(int Start, int Length)> LiteralHits(string plain, string term)
        {
            var hits = new List<(int Start, int Length)>();
            int at = 0;
            while (at <= plain.Length - term.Length)
            {
                int hit = plain.IndexOf(term, at, StringComparison.OrdinalIgnoreCase);
                if (hit < 0) break;
                if (plain.IndexOf(DocumentReplacement.ObjectBoundary, hit, term.Length) < 0)
                    hits.Add((hit, term.Length));
                at = hit + term.Length;
            }
            return hits;
        }

        /// <summary>Applies the replacement to every hit, BACK-TO-FRONT so each earlier offset
        /// is still valid when its turn comes - the run map was built once, before any edit.</summary>
        private static void ApplyHits(List<(int Offset, TextPointer Start, int Length)> runs,
                                      List<(int Start, int Length)> hits, string repl)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                var a = PointerAt(runs, hits[i].Start);
                var b = PointerAt(runs, hits[i].Start + hits[i].Length, end: true);
                if (a == null || b == null || DocumentReplacement.ContainsObject(a, b)) continue;
                new TextRange(a, b).Text = repl;
            }
        }

        /// <summary>Offset back to a live TextPointer through the run map - the same binary
        /// search as the find bar's PointerForOffset, against a caller-owned map.</summary>
        private static TextPointer? PointerAt(List<(int Offset, TextPointer Start, int Length)> runs, int offset, bool end = false)
        {
            int lookup = end ? offset - 1 : offset;
            int lo = 0, hi = runs.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var (rOffset, _, rLength) = runs[mid];
                if (lookup < rOffset) hi = mid - 1;
                else if (lookup >= rOffset + rLength) lo = mid + 1;
                else { found = mid; break; }
            }
            if (found < 0) return null;
            var (runOffset, runStart, _) = runs[found];
            return runStart.GetPositionAtOffset(offset - runOffset,
                end ? LogicalDirection.Backward : LogicalDirection.Forward);
        }

        /// <summary>A note's sketch text labels from its stored payloads, one per line - the
        /// store-side twin of CollectSketchLabelText (Editor.Sketch.cs), for notes that are not
        /// open in the editor.</summary>
        private static string SketchLabelTextFor(long noteId)
        {
            var parts = new List<string>();
            // The payloads the stored body names, or the ordinal rows for a note saved before
            // sketch markers existed (Services/Sketch.cs).
            var payloads = Attachments.MarkersIn(NoteStore.LoadContent(noteId))
                .Select(NoteStore.GetAttachment)
                .Where(a => a?.Kind == AttachmentKind.Sketch)
                .Select(a => a!.Payload)
                .ToList();
            if (payloads.Count == 0) payloads = NoteStore.LoadSketches(noteId).Values.ToList();
            foreach (var payload in payloads)
                foreach (var o in SketchModel.Deserialize(payload))
                    if (o.Kind == SketchKind.Text && !string.IsNullOrWhiteSpace(o.Text))
                        parts.Add(o.Text!.Trim());
            return string.Join("\n", parts);
        }
    }
}

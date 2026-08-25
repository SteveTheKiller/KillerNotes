// ═══════════════════════════════════════════════════════════
//  RENAME PROPAGATION  -  keeping [[links]] pointed at the note they name
// ═══════════════════════════════════════════════════════════
//
// A wikilink targets a TITLE, not an id - see the note_links schema for why, and it is the same
// reason you can link a note before you write it. The cost of that choice is this: renaming a note
// silently turns every [[old title]] in the notebook into a link to a ghost. The note is still
// there, and every pointer to it is now wrong.
//
// WHEN THE QUESTION IS ASKED. Not from the save path. A title is saved every two seconds while it
// is being typed, so prompting there would ask about "Proj", then "Proje", then "Projec" - a
// dialog interrupting the very rename it is asking about. The rename is RECORDED at save and the
// question is asked when the title box gives up the keyboard, which is when the user is done.
//
// COALESCED ON THE FIRST OLD TITLE. Editing a title in three passes before leaving the box is one
// rename as far as the links are concerned: they still say whatever they said at the start, so
// that is the title being searched for, not the intermediate spellings nobody ever linked.
//
// BOTH NOTE FORMATS. A rich-text note is a XamlPackage and is rewritten through the same document
// surgery the cross-note replace uses; a markdown note is UTF-8 text and is rewritten as text.
// Skipping markdown notes would leave a rename half-applied, which is worse than not offering.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        /// <summary>The note whose title changed, and the title it had BEFORE the change. -1 when
        /// there is nothing pending.</summary>
        private long _renameId = -1;
        private string _renameFrom = "";

        /// <summary>One note's rewrite, held between the scan and the commit so the confirmation
        /// can name every note before anything is written.</summary>
        private sealed class RenameEdit
        {
            public long Id;
            public string Title = "";
            public int Hits;
            public string BodyBefore = "";
            public byte[] Content = [];
            public string BodyAfter = "";
        }

        /// <summary>Records that a note's title changed. Called from SaveCurrentNote, the one place
        /// that can see the stored row and the new box text at the same time.</summary>
        private void RecordRename(long id, string from, string to)
        {
            if (id < 0) return;
            if (from.Trim().Length == 0 || to.Trim().Length == 0) return;
            if (string.Equals(from, to, StringComparison.Ordinal)) return;
            if (_renameId != id) { _renameId = id; _renameFrom = from; }
        }

        private void TitleBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            // Background priority, so whatever click moved the focus finishes before a modal opens
            // on top of it. Same reasoning as the startup prompts.
            Dispatcher.BeginInvoke(new Action(FlushPendingRename), DispatcherPriority.Background);

        /// <summary>Offers to rewrite every [[old title]] in the notebook. Returns silently when
        /// nothing linked to the old title, which is the ordinary case.</summary>
        private void FlushPendingRename()
        {
            long id = _renameId;
            string from = _renameFrom;
            _renameId = -1;
            _renameFrom = "";
            if (id < 0 || !NoteStore.IsOpen || NoteStore.IsReadOnly) return;

            string to = _notes.FirstOrDefault(n => n.Id == id)?.Title ?? "";
            if (to.Trim().Length == 0) return;
            // A pure change of case is not a rename here: targets resolve case-insensitively, so
            // every existing link already points at the note and rewriting them would be churn.
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return;

            // The link table still says the OLD title for every note except the one just saved, so
            // this is the exact candidate set - no text search over the notebook.
            var sources = NoteStore.Backlinks(from);
            if (sources.Count == 0) return;

            var edits = new List<RenameEdit>();
            foreach (var (srcId, srcTitle) in sources)
            {
                var edit = BuildRenameEdit(srcId, srcTitle, from, to);
                if (edit != null) edits.Add(edit);
            }
            if (edits.Count == 0) return;

            if (!ConfirmRename(from, to, edits)) return;
            CommitRename(edits);
        }

        /// <summary>Rewrites one note in memory, or returns null when it has nothing to rewrite or
        /// cannot be read. Never throws into the rename: an unreadable note is skipped, never
        /// half-written.</summary>
        private RenameEdit? BuildRenameEdit(long srcId, string srcTitle, string from, string to)
        {
            byte[]? blob = NoteStore.LoadContent(srcId);
            if (blob == null) return null;
            string wrapped = WikiLinks.Wrap(to);

            try
            {
                if (!MarkdownBlob.IsPackage(blob))
                {
                    // ---- Markdown note: the blob IS the text ----
                    string text = MarkdownBlob.Decode(blob);
                    var spans = MatchingSpans(text, from);
                    if (spans.Count == 0) return null;
                    var sb = new StringBuilder(text);
                    // Back to front, so every earlier offset is still valid when its turn comes.
                    for (int i = spans.Count - 1; i >= 0; i--)
                        sb.Remove(spans[i].Start, spans[i].Length).Insert(spans[i].Start, wrapped);
                    string after = sb.ToString();
                    return new RenameEdit
                    {
                        Id = srcId, Title = srcTitle, Hits = spans.Count,
                        BodyBefore = text, Content = MarkdownBlob.Encode(after), BodyAfter = after,
                    };
                }

                // ---- Rich text: the same document surgery as the cross-note replace ----
                var doc = LoadDocBlob(blob);
                var (plain, runs) = FlattenDoc(doc);
                var hits = MatchingSpans(plain, from);
                if (hits.Count == 0) return null;
                // The WHOLE bracketed span is replaced, not the title inside it: a link may be
                // written [[ Old Title ]] and the brackets have to be rebuilt around the new one.
                ApplyHits(runs, [.. hits.Select(h => (h.Start, h.Length))], wrapped);
                var whole = new TextRange(doc.ContentStart, doc.ContentEnd);
                using var ms = new MemoryStream();
                whole.Save(ms, DataFormats.XamlPackage);
                return new RenameEdit
                {
                    Id = srcId, Title = srcTitle, Hits = hits.Count,
                    BodyBefore = plain, Content = ms.ToArray(), BodyAfter = whole.Text,
                };
            }
            catch { return null; }
        }

        /// <summary>Every [[...]] span whose target is this title, case-insensitively, in order.</summary>
        private static List<(int Start, int Length, string Target)> MatchingSpans(string text, string title) =>
            [.. WikiLinks.Spans(text)
                .Where(s => string.Equals(s.Target, title, StringComparison.OrdinalIgnoreCase))];

        /// <summary>Names every note that would change and how many links it holds, so this is
        /// never a blind rewrite of notes the user is not looking at.</summary>
        private bool ConfirmRename(string from, string to, List<RenameEdit> edits)
        {
            int total = edits.Sum(e => e.Hits);
            var lines = new StringBuilder();
            const int listCap = 12;
            for (int i = 0; i < edits.Count && i < listCap; i++)
            {
                string title = string.IsNullOrWhiteSpace(edits[i].Title)
                    ? Loc("Str_Untitled") : edits[i].Title;
                lines.AppendLine($"{title}  ({edits[i].Hits})");
            }
            if (edits.Count > listCap)
                lines.AppendLine(string.Format(Loc("Str_Dlg_ReplaceMore"), edits.Count - listCap));

            var dlg = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_RenameLinksHead"), total, edits.Count),
                string.Format(Loc("Str_Dlg_RenameLinksBody"), from, to) + "\n\n" + lines.ToString().TrimEnd(),
                Loc("Str_Btn_UpdateLinks")) { Owner = this };
            dlg.ShowDialog();
            return dlg.Confirmed;
        }

        /// <summary>Writes every rewrite in one transaction and pushes ONE undo entry, so a rename
        /// that touched nine notes is undone by a single Ctrl+Z rather than nine.</summary>
        private void CommitRename(List<RenameEdit> edits)
        {
            var before = new List<NoteStore.NoteRow>();
            foreach (var e in edits)
                if (NoteStore.CaptureRow(e.Id) is { } row) before.Add(row);

            var updates = new List<(long Id, byte[] Content, string Plain)>();
            foreach (var e in edits)
            {
                // Stored plain is the body plus the note's sketch labels, the same recipe
                // SaveCurrentNote uses, so a labeled diagram stays searchable afterwards.
                string labels = SketchLabelTextFor(e.Id);
                string stored = labels.Length == 0 ? e.BodyAfter : e.BodyAfter + "\n" + labels;
                updates.Add((e.Id, e.Content, stored));
            }
            NoteStore.UpdateContents(updates);

            // The link table is a cache of what the notes SAY, so it has to be re-derived from the
            // rewritten text. Without this the notebook still believes these notes point at the old
            // title, and the graph and the backlinks strip would both keep saying so.
            foreach (var e in edits) NoteStore.SetLinks(e.Id, WikiLinks.Parse(e.BodyAfter));

            long openId = _currentId;
            bool touchedOpen = openId >= 0 && edits.Any(e => e.Id == openId);
            RefreshList();
            if (touchedOpen) OpenNote(openId);
            RefreshBacklinks();

            // Captured for the closure: the fields are about to be reused by the next rename.
            var undoLinks = edits.Select(e => (e.Id, e.BodyBefore)).ToList();
            int total = edits.Sum(e => e.Hits);
            int noteCount = edits.Count;

            PushUndo(() =>
            {
                NoteStore.RestoreContents(before);
                // Same re-derivation on the way back, or undo would restore the text and leave the
                // link table describing the text it no longer holds.
                foreach (var (undoId, body) in undoLinks) NoteStore.SetLinks(undoId, WikiLinks.Parse(body));
                long cur = _currentId;
                RefreshList();
                if (cur >= 0 && before.Any(r => r.Id == cur)) OpenNote(cur);
                RefreshBacklinks();
            });

            FlashStatus(string.Format(Loc("Str_St_RenameLinks"), total, noteCount));
        }
    }
}

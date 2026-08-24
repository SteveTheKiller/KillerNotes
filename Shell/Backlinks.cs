// ═══════════════════════════════════════════════════════════
//  BACKLINKS  -  "linked from", under the editor
// ═══════════════════════════════════════════════════════════
//
// The half of a wikilink the author never typed. Forward links you already know about, because
// you wrote them; the value of a second brain is arriving at a note and being told what else in
// the notebook points at it.
//
// A strip under the editor rather than a panel you open: a backlink you have to go looking for
// does not get looked at. It hides itself entirely when a note has none, so a notebook with no
// links yet looks exactly as it did before this existed.
//
// Rebuilt on note open and after each save, both of which are the only moments the answer can
// change. It is one indexed query over note_links, so this is cheap enough to do bluntly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        /// <summary>Alt+M / the strip's own right-click menu. Remembered across restarts: someone
        /// who does not want the strip does not want to dismiss it once per launch.</summary>
        private bool _backlinkBarHidden;

        private void InitBacklinkBar() => _backlinkBarHidden = App.GetSetting("HideBacklinkBar") == "1";

        private void HideBacklinkBar_Click(object sender, RoutedEventArgs e) => ToggleBacklinkBar();

        private void ToggleBacklinkBar()
        {
            _backlinkBarHidden = !_backlinkBarHidden;
            App.SetSetting("HideBacklinkBar", _backlinkBarHidden ? "1" : "0");
            RefreshBacklinks();
            // Hiding something from its own menu leaves nothing on screen to undo it with, so the
            // status line names the key that brings it back.
            FlashStatus(Loc(_backlinkBarHidden ? "Str_St_MentionsHidden" : "Str_St_MentionsShown"));
        }

        /// <summary>One entry in the strip. Cached so a window resize can re-lay-out the row
        /// without going back to the database - the contents have not changed, only the space.
        /// </summary>
        private sealed record StripItem(long Id, string Title, bool Mention);

        private readonly List<StripItem> _strip = [];

        private void RefreshBacklinks()
        {
            if (BacklinkBar == null || BacklinkRow == null) return;
            _strip.Clear();

            string title = TitleBox.Text ?? "";
            if (_backlinkBarHidden || _currentId < 0 || string.IsNullOrWhiteSpace(title) || !NoteStore.IsOpen)
            {
                BacklinkRow.Children.Clear();
                BacklinkBar.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var (id, t) in NoteStore.Backlinks(title))
                _strip.Add(new StripItem(id, string.IsNullOrWhiteSpace(t) ? Loc("Str_Untitled") : t, false));
            foreach (var (id, t) in NoteStore.UnlinkedMentions(title))
                _strip.Add(new StripItem(id, string.IsNullOrWhiteSpace(t) ? Loc("Str_Untitled") : t, true));

            LayoutStrip();
        }

        // ── Laying the row out ───────────────────────────────────────────────────────────
        //
        // Fills the row from the cache, fitting what it can and rolling the rest into one
        // "And N more" button. Called on every refresh AND on every resize of the content pane,
        // so widening the window promotes names out of the menu and narrowing it puts them back.

        /// <summary>The widest the strip may be: the 60% star column it lives in, less its own
        /// 10px padding either side. Returns 0 before the host has been laid out, which tells the
        /// caller to try again once it has.</summary>
        private double StripBudget()
        {
            double host = BacklinkHost?.ActualWidth ?? 0;
            return host <= 0 ? 0 : host * 0.6 - 24;
        }

        /// <summary>Re-lays the row out when the pane resizes, so widening the window promotes
        /// names out of the overflow menu and narrowing it puts them back. Works from the cache,
        /// so this never touches the database.</summary>
        private void BacklinkHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged) LayoutStrip();
        }

        private void LayoutStrip()
        {
            if (BacklinkBar == null || BacklinkRow == null) return;
            BacklinkRow.Children.Clear();

            if (_backlinkBarHidden || _strip.Count == 0)
            {
                BacklinkBar.Visibility = Visibility.Collapsed;
                return;
            }
            BacklinkBar.Visibility = Visibility.Visible;

            double budget = StripBudget();
            if (budget <= 0)
            {
                // The pane has no width yet (first paint). Show everything for this pass and come
                // back at Loaded priority, by which point ActualWidth is real.
                foreach (var it in _strip) BacklinkRow.Children.Add(BuildChip(it));
                Dispatcher.BeginInvoke(new Action(LayoutStrip), DispatcherPriority.Loaded);
                return;
            }

            // Labels first, and they are never dropped - a row of bare names with no idea which
            // are links and which are mentions is worse than showing fewer names.
            double used = 0;
            bool anyLink = _strip.Any(i => !i.Mention);
            if (anyLink) used += AddFixed(MakeLabel("Str_Lbl_Backlinks", "Str_TT_Backlinks"));

            int shown = 0;
            bool dividerDone = false;
            // Reserve room for the overflow button up front when there is any real chance of
            // needing it. Measuring the row twice to find out exactly would cost a second pass
            // for a button whose width barely varies.
            double reserve = _strip.Count > 1 ? 96 : 0;

            foreach (var it in _strip)
            {
                if (it.Mention && !dividerDone)
                {
                    dividerDone = true;
                    // The divider and the second label only appear once something is actually
                    // going to sit after them.
                    if (anyLink && shown > 0) used += AddFixed(MakeDivider());
                    used += AddFixed(MakeLabel("Str_Lbl_Mentions", "Str_TT_Mentions"));
                }

                var chip = BuildChip(it);
                double w = Measured(chip);
                if (shown > 0 && used + w > budget - reserve) break;
                BacklinkRow.Children.Add(chip);
                used += w;
                shown++;
            }

            int hidden = _strip.Count - shown;
            if (hidden > 0) BacklinkRow.Children.Add(BuildOverflow(_strip.Skip(shown).ToList(), hidden));
        }

        private double AddFixed(FrameworkElement e)
        {
            BacklinkRow.Children.Add(e);
            return Measured(e);
        }

        private static double Measured(FrameworkElement e)
        {
            e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return e.DesiredSize.Width;
        }

        private TextBlock MakeLabel(string textKey, string tipKey) => new()
        {
            Text = Loc(textKey),
            Foreground = (Brush?)TryFindResource("DimTextBrush") ?? Brushes.Gray,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = Loc(tipKey),
        };

        private Border MakeDivider() => new()
        {
            Width = 1,
            Margin = new Thickness(6, 3, 10, 3),
            Background = (Brush?)TryFindResource("BarEdgeBrush") ?? Brushes.Gray,
        };

        /// <summary>One name in the row. A backlink opens the note; a mention LINKS it, because
        /// being told a link is missing makes "add it" the obvious action - Ctrl+Click still just
        /// goes there.</summary>
        private Button BuildChip(StripItem it)
        {
            var chip = new Button
            {
                Content = it.Title,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Cursor = Cursors.Hand,
                FontSize = 11,
                Tag = it.Id,
                ToolTip = it.Mention ? Loc("Str_TT_MentionChip") : null,
            };
            if (Application.Current?.TryFindResource("SurfaceButton") is Style s) chip.Style = s;
            chip.Click += (_, _) => ChipActivated(it);
            return chip;
        }

        private void ChipActivated(StripItem it)
        {
            if (!it.Mention || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                SaveCurrentNote(refreshList: false);
                OpenNote(it.Id);
                SelectNoteInList(it.Id);   // WikiLinkNav.cs
                return;
            }
            LinkMention(it.Id, TitleBox.Text ?? "");
        }

        /// <summary>The overflow button. Opens a plain ContextMenu, which picks up the app's menu
        /// theme implicitly, so the list looks like every other menu in the window rather than
        /// like a second kind of popup.</summary>
        private Button BuildOverflow(List<StripItem> rest, int count)
        {
            var more = new Button
            {
                Content = string.Format(Loc("Str_Lbl_AndMore"), count),
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Cursor = Cursors.Hand,
                FontSize = 11,
                ToolTip = Loc("Str_TT_AndMore"),
            };
            if (Application.Current?.TryFindResource("SurfaceButton") is Style s) more.Style = s;
            more.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Top };
                bool sep = false;
                foreach (var it in rest)
                {
                    // One separator where the links end and the mentions begin, so the menu keeps
                    // the distinction the row's two labels were making.
                    if (it.Mention && !sep && rest.Any(r => !r.Mention)) { sep = true; menu.Items.Add(new Separator()); }
                    var mi = new MenuItem
                    {
                        Header = it.Title,
                        ToolTip = it.Mention ? Loc("Str_TT_MentionChip") : null,
                    };
                    var captured = it;
                    mi.Click += (_, _) => ChipActivated(captured);
                    menu.Items.Add(mi);
                }
                menu.IsOpen = true;
            };
            return more;
        }

        // ── Unlinked mentions ────────────────────────────────────────────────────────────
        //
        // Notes that already say this note's title without linking it. The connection is in the
        // text; all that is missing is the two pairs of brackets, so the useful thing is not to
        // report it but to offer to make it.

        /// <summary>Wraps the first occurrence of <paramref name="title"/> in another note with
        /// [[brackets]], turning a mention into a real link.
        ///
        /// Uses the SAME machinery as cross-note replace (GlobalReplace.cs): load the blob into an
        /// off-screen FlowDocument, flatten it to text plus a run map, apply the edit through that
        /// map, and write it back with the stored plain rebuilt the same way SaveCurrentNote does.
        /// Nothing new is invented for it, so it inherits the sketch-label handling and the
        /// single-transaction, single-undo behaviour.</summary>
        private void LinkMention(long noteId, string title)
        {
            if (NoteStore.IsReadOnly)
            {
                FlashStatus(string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner));
                return;
            }
            byte[]? blob = NoteStore.LoadContent(noteId);
            if (blob == null) return;

            FlowDocument doc;
            try { doc = LoadDocBlob(blob); }
            catch { return; }   // an unreadable note is left alone, never half-rewritten

            var (plain, runs) = FlattenDoc(doc);
            var hits = LiteralHits(plain, title);
            if (hits.Count == 0) { FlashStatus(Loc("Str_St_MentionGone")); RefreshBacklinks(); return; }

            // FIRST occurrence only. Linking every mention in a note is noise - one link is what
            // makes the connection, and the rest are just the word appearing again.
            var before = NoteStore.CaptureRow(noteId);
            ApplyHits(runs, [hits[0]], WikiLinks.Wrap(title));

            var whole = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var ms = new MemoryStream();
            whole.Save(ms, DataFormats.XamlPackage);
            string stored = whole.Text;
            string labels = SketchLabelTextFor(noteId);
            if (labels.Length > 0) stored = stored + "\n" + labels;

            NoteStore.UpdateContents([(noteId, ms.ToArray(), stored)]);
            NoteStore.SetLinks(noteId, WikiLinks.Parse(whole.Text));

            if (before != null)
                PushUndo(() =>
                {
                    NoteStore.RestoreContents([before]);
                    NoteStore.SetLinks(before.Id, WikiLinks.Parse(before.Plain));
                    RefreshList();
                    RefreshBacklinks();
                });

            RefreshList();
            RefreshBacklinks();
            FlashStatus(Loc("Str_St_Linked"));
        }

        /// <summary>Opens the link graph. Read-only and self-contained, so it needs nothing from
        /// the editor except a note to centre on and a way to open what you double-click.</summary>
        private void Graph_Click(object sender, RoutedEventArgs e) => ShowGraph(null);

        /// <summary>Graphs one group, from its right-click menu.</summary>
        private void GroupGraph_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not Models.GroupHeader g) return;
            ShowGraph(g.Path, g.Name);
        }

        /// <summary>Ctrl+Shift+B. Graphs the group whose header is selected in the sidebar, or the
        /// whole notebook when the selection is a note or nothing. One binding rather than two,
        /// because "graph what I am looking at" is the same intent either way - and the overlay
        /// says so rather than leaving it to be discovered.</summary>
        private void GraphShortcut()
        {
            if (NotesList?.SelectedItem is Models.GroupHeader g) ShowGraph(g.Path, g.Name);
            else ShowGraph(null);
        }

        private void ShowGraph(string? groupPath, string? groupName = null)
        {
            if (!NoteStore.IsOpen) return;
            SaveCurrentNote(refreshList: false);   // so a link typed seconds ago is already an edge

            var win = new Controls.GraphWindow(
                id =>
                {
                    OpenNote(id);
                    SelectNoteInList(id);
                    Activate();
                },
                // A ghost node names a note nobody has written. Opening one creates it, the same
                // as following a dead wikilink in the editor - the dead end IS the moment the
                // note should get made.
                title => { FollowWikiLink(title); Activate(); },
                _currentId,
                // The caption names the group, so two graph windows open side by side say which
                // is which - which is the whole reason to allow more than one.
                groupName is string gn ? Loc("Str_Lbl_Graph") + ": " + gn : Loc("Str_Lbl_Graph"),
                groupPath);

            if (win.IsEmpty)
            {
                FlashStatus(Loc(groupPath == null ? "Str_St_GraphEmpty" : "Str_St_GraphGroupEmpty"));
                return;
            }
            FlashStatus(win.Summary);
            win.Show();   // modeless: the point is to keep it open beside the notes
        }
    }
}

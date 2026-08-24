// ═══════════════════════════════════════════════════════════
//  NAVIGATION HISTORY  -  back and forward through notes
// ═══════════════════════════════════════════════════════════
//
// Wikilinks, backlink chips and the graph all made it easy to LEAVE a note and left no way to
// get back to it. Following three links and then trying to remember which of sixty notes you
// started from is the sort of thing a browser solved decades ago, so this is a browser history:
// a back stack, a forward stack, and the rule that navigating somewhere new discards the future.
//
// ONE FUNNEL. Every path that changes notes - the sidebar list, a chip, Ctrl+Click on a
// wikilink, a graph double-click, search results - ends up in OpenNote, so OpenNote is the only
// place that records. Recording at each call site instead would mean every future navigation
// feature has to remember to opt in, and the one that forgets is a silent hole in the history.
//
// The stacks hold ids rather than notes, and ids are re-checked against the live list on the way
// out: a note deleted while it sat in the history is skipped rather than resurrected or crashed
// on. That is why popping is a loop and not an index.

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private readonly List<long> _navBack = [];
        private readonly List<long> _navFwd = [];

        /// <summary>Set while the history is driving OpenNote, so replaying a step does not get
        /// recorded as a brand new navigation - which would make Back push what it just popped
        /// and leave you stuck between two notes forever.</summary>
        private bool _navReplaying;

        // Deep enough that a real trail of link-following survives, shallow enough that it is
        // never the reason memory grows. Two ids per step, so the whole thing is under a KB.
        private const int NavDepth = 60;

        /// <summary>Called by OpenNote with the note being left behind. The ONLY recording point.</summary>
        private void RecordNav(long leaving)
        {
            if (_navReplaying || leaving < 0) return;
            if (_navBack.Count > 0 && _navBack[^1] == leaving) return;   // no consecutive duplicates

            _navBack.Add(leaving);
            if (_navBack.Count > NavDepth) _navBack.RemoveAt(0);

            // Going somewhere new abandons the forward branch, exactly like a browser. Keeping it
            // would offer a "forward" into a trail you have already stepped off.
            _navFwd.Clear();
            UpdateNavState();
        }

        private void NavBack() => NavStep(_navBack, _navFwd, "Str_St_NavNoBack");
        private void NavForward() => NavStep(_navFwd, _navBack, "Str_St_NavNoForward");

        /// <summary>Both directions are the same move: pop one side, push the current note onto
        /// the other, open it without recording.</summary>
        private void NavStep(List<long> from, List<long> onto, string emptyMsg)
        {
            long target = -1;
            while (from.Count > 0)
            {
                target = from[^1];
                from.RemoveAt(from.Count - 1);
                if (_notes.Any(n => n.Id == target)) break;   // skip notes deleted since
                target = -1;
            }
            if (target < 0) { FlashStatus(Loc(emptyMsg)); UpdateNavState(); return; }

            if (_currentId >= 0 && _currentId != target)
            {
                onto.Add(_currentId);
                if (onto.Count > NavDepth) onto.RemoveAt(0);
            }

            SaveCurrentNote(refreshList: false);
            _navReplaying = true;
            try
            {
                OpenNote(target);
                SelectNoteInList(target);   // WikiLinkNav.cs
            }
            finally { _navReplaying = false; }
            UpdateNavState();
        }

        /// <summary>Greys the menu rows out when there is nowhere to go, so the menu reports the
        /// state instead of silently doing nothing.</summary>
        private void UpdateNavState()
        {
            if (NavBackItem != null) NavBackItem.IsEnabled = _navBack.Count > 0;
            if (NavFwdItem != null) NavFwdItem.IsEnabled = _navFwd.Count > 0;
        }

        /// <summary>Dropping a database drops its history with it - the ids in the stacks belong
        /// to the old file and mean something entirely different in the new one.</summary>
        private void ClearNavHistory()
        {
            _navBack.Clear();
            _navFwd.Clear();
            UpdateNavState();
        }

        private void NavBack_Click(object sender, RoutedEventArgs e) => NavBack();
        private void NavForward_Click(object sender, RoutedEventArgs e) => NavForward();

        // ── Mouse thumb buttons ──────────────────────────────────────────────────────────
        //
        // XButton1/XButton2 are back/forward everywhere else on the machine, so they need no
        // teaching. Handled at PREVIEW on the window: the RichTextBox would otherwise see them
        // first, and while it does nothing useful with them it does mark them handled.

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Never while a modal overlay owns the screen - the About card, the shortcuts overlay
            // and the Fonts card are not places a thumb button should quietly swap the note
            // behind them.
            if (AboutOverlay?.Visibility == Visibility.Visible) return;
            if (ShortcutOverlay?.Visibility == Visibility.Visible) return;
            if (FontsOverlay?.Visibility == Visibility.Visible) return;

            if (e.ChangedButton == MouseButton.XButton1) { NavBack(); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.XButton2) { NavForward(); e.Handled = true; }
        }
    }
}

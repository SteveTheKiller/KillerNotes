using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private readonly List<(int Start, int Length)> _globalFindHits = [];
        private readonly Dictionary<long, int> _globalMatchCounts = [];
        private DispatcherTimer? _globalSearchTimer;
        private DispatcherOperation? _globalSearchOperation;
        private int _globalSearchVersion, _globalUnreadable;
        private bool _globalSearchCounting;
        private string? _globalSearchStatusText;
        private string GlobalSearchTerm => SearchBox?.Text ?? "";
        private bool HasGlobalSearch => !string.IsNullOrWhiteSpace(GlobalSearchTerm);

        private void CancelGlobalSearchPreview()
        {
            _globalSearchVersion++;
            _globalSearchTimer?.Stop();
            _globalSearchOperation?.Abort();
            _globalSearchOperation = null;
        }

        private void QueueGlobalSearchPreview()
        {
            CancelGlobalSearchPreview();
            _globalMatchCounts.Clear();
            _globalUnreadable = 0;
            _globalSearchCounting = HasGlobalSearch;
            RefreshGlobalHighlights();
            UpdateGlobalSearchCount();
            if (!HasGlobalSearch) return;

            if (_globalSearchTimer == null)
            {
                _globalSearchTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                    { Interval = TimeSpan.FromMilliseconds(250) };
                _globalSearchTimer.Tick += (_, _) =>
                {
                    _globalSearchTimer.Stop();
                    ScanGlobalSearchMatches();
                };
            }
            _globalSearchTimer.Start();
        }

        private void ScanGlobalSearchMatches()
        {
            int version = _globalSearchVersion;
            string query = GlobalSearchTerm;
            string database = NoteStore.DbPath;
            var candidates = _notes.Select(n => (n.Id, n.Format)).ToArray();
            int next = 0;
            void ScanNext()
            {
                _globalSearchOperation = null;
                if (version != _globalSearchVersion || !NoteStore.IsOpen ||
                    database != NoteStore.DbPath || query != GlobalSearchTerm) return;
                if (next == candidates.Length)
                {
                    _globalSearchCounting = false;
                    UpdateGlobalSearchCount();
                    return;
                }
                var note = candidates[next++];
                try
                {
                    string plain;
                    if (note.Id == _currentId && !_loadingNote && !_loadFailed)
                    {
                        if (_findPlainStale) RebuildFindPlain();
                        plain = _findPlain;
                    }
                    else
                        plain = FlattenDoc(NoteSearch.LoadDocument(NoteStore.LoadContent(note.Id), note.Format)).Plain;
                    _globalMatchCounts[note.Id] = NoteSearch.CountMatches(plain, query);
                }
                catch { _globalUnreadable++; }
                // Yield between documents so a new query or window close cancels the scan.
                _globalSearchOperation = Dispatcher.BeginInvoke(new Action(ScanNext), DispatcherPriority.Background);
            }
            ScanNext();
        }

        private void RefreshGlobalHighlights()
        {
            if (SearchBox == null) return;
            _globalFindHits.Clear();
            if (HasGlobalSearch && !_loadingNote && !_loadFailed && _currentId >= 0 &&
                _notes.Any(n => n.Id == _currentId))
            {
                if (_findPlainStale) RebuildFindPlain();
                _globalFindHits.AddRange(NoteSearch.FindMatches(_findPlain, GlobalSearchTerm));
                _globalMatchCounts[_currentId] = NoteSearch.CountMatches(_findPlain, GlobalSearchTerm);
            }
            RepaintFindMatches();
            UpdateGlobalSearchCount();
        }

        private string GlobalSearchStatus()
        {
            if (_globalSearchCounting)
                return string.Format(Loc("Str_St_GlobalCounting"), _notes.Count);
            string key = _globalUnreadable > 0 ? "Str_St_GlobalMatchesIncomplete" : "Str_St_GlobalMatches";
            return string.Format(Loc(key), _globalMatchCounts.Values.Sum(), _notes.Count, _globalUnreadable);
        }

        private void UpdateGlobalSearchCount()
        {
            if (GlobalSearchCount == null) return;
            GlobalSearchCount.Visibility = HasGlobalSearch ? Visibility.Visible : Visibility.Collapsed;
            GlobalSearchCount.Text = _globalSearchCounting ? "..." : _globalMatchCounts.Values.Sum().ToString("N0");
            string status = GlobalSearchStatus();
            GlobalSearchCount.ToolTip = status;
            // A background count owns only its previous status, never a save/load error.
            if (HasGlobalSearch && !_statusTimer.IsEnabled && StatusText.Text == _globalSearchStatusText)
                StatusText.Text = _globalSearchStatusText = status;
        }
    }
}

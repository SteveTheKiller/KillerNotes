using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using KillerNotes.Controls;
using KillerNotes.Features.Security;
using KillerNotes.Services;

// Lock now (1.6.0, KN-P01). The title-bar lock sets the password; this ends an unlocked session
// without quitting: the note is saved, everything showing notes goes away, the store closes and
// the session password is forgotten, so the CLI and MCP get "Open and unlock" until the unlock.
//
// Three ways in: the lock button's menu or Ctrl+Shift+U, Windows locking or going to sleep (on by
// default), and the computer sitting idle (off by default). The rules for what may lock and what
// a failed save does live in SecurityController; this file is the window's half.
namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private readonly HeldEdits _heldEdits = new();
        private readonly HeldTools _heldTools = new();
        private DispatcherTimer? _idleLockTimer;
        private int _lockRetries;

        private void InitLockNow()
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            NoteStore.Rekeyed += OnDatabaseRekeyed;
            // SystemEvents are static: a handler left on them keeps this window alive and fires
            // into a closed one.
            Closed += (_, _) =>
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                NoteStore.Rekeyed -= OnDatabaseRekeyed;
                _idleLockTimer?.Stop();
            };
            _idleLockTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(30) };
            _idleLockTimer.Tick += (_, _) =>
            {
                if (AutoLock.IdleDue(AutoLock.ParseIdleMinutes(App.GetSetting(AutoLock.IdleSetting)), AutoLock.SystemIdle()))
                    LockNow(automatic: true);
            };
            _idleLockTimer.Start();
            InitLockOptions();
        }

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) QueueWindowsLock();
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) QueueWindowsLock();
        }

        // SystemEvents raise on their own thread; Send rather than BeginInvoke for sleep, so the
        // lock lands before the machine stops running code.
        private void QueueWindowsLock()
        {
            if (!AutoLock.WindowsLockEnabled(App.GetSetting(AutoLock.WindowsLockSetting))) return;
            Dispatcher.Invoke(new Action(() => LockNow(automatic: true)), DispatcherPriority.Send);
        }

        /// <summary>Locks the open notepad. Manual locks explain why they could not; automatic
        /// ones stay quiet, since there is nobody at the keyboard.</summary>
        private void LockNow(bool automatic)
        {
            if (!_security.CanLockNow)
            {
                if (!automatic && NoteStore.IsOpen && !NoteStore.HasPassword) StatusText.Text = Loc("Str_St_LockNeedsPassword");
                return;
            }
            if (automatic) AbandonEditorGestures();   // Editor.Float.cs - a held drag cannot save

            // A modal dialog runs its own loop under this call, and the code waiting on it would
            // carry on against a closed store. Close it, let its caller unwind, then lock.
            if (ComponentDispatcher.IsThreadModal)
            {
                CloseWindowsForLock(includeTools: false);
                // A modal this cannot close (a native message box) gives up after a few tries,
                // and the next trigger starts counting again.
                if (++_lockRetries <= 20)
                    Dispatcher.BeginInvoke(new Action(() => LockNow(automatic)), DispatcherPriority.Background);
                else
                    _lockRetries = 0;
                return;
            }
            _lockRetries = 0;
            if (_security.LockNow(automatic) && !automatic)
            {
                StatusText.Text = Loc("Str_St_Locked");
                ShowTipOnce("lock");   // SaveState.cs
            }
        }

        void ISecurityHost.EnterLocked(bool keepUnsavedEdits)
        {
            StopEmbeddedPlayback();          // Editor.Dictation.cs
            CancelBackgroundJob();             // VaultExport.cs - an export or import reads with the notepad's key
            _dictation?.StopForLock();       // the microphone is never live behind a locked screen
            CloseWindowsForLock(includeTools: true);
            _quickDraft = "";                // QuickNote.cs - closing the window just stored its text, and the locked screen reaches ShowQuickNote
            CancelGlobalSearchPreview();     // GlobalSearch.cs
            _globalMatchCounts.Clear();
            _globalFindHits.Clear();
            _saveTimer.Stop();

            if (keepUnsavedEdits && _currentId >= 0 && !_loadFailed)
            {
                HoldOpenEdits();
                StatusText.Text = Loc("Str_St_LockEditsHeld");
            }
            ((ISecurityHost)this).ClearEditor();
            _dirty = false;
            Editor.Document.Blocks.Clear();
            // Undo would otherwise step back into the note that was just put away.
            Editor.IsUndoEnabled = false;
            Editor.IsUndoEnabled = true;
            TitleBox.Text = "";
            _findPlain = "";
            _findPlainStale = true;
            ForgetSearches();
            NoteDragData.RemoveSharedRecordings(NoteDragData.DefaultTempRoot);   // decrypted copies under TEMP
            DictationRecorder.SweepTempRecordings(NoteDragData.DefaultTempRoot, System.IO.Path.GetTempPath());   // a take mid-transcription
            NoteDragData.RemoveDragFolders(NoteDragData.DefaultTempRoot);        // notes dragged out, written unencrypted
            ClearNavHistory();               // NoteHistory.cs - titles and ids of the locked notepad
            _notes = [];
            _trashNotes = [];
            _syncingSelection = true;        // emptying the list must not read as picking a note
            _sidebarItems.Clear();
            _syncingSelection = false;

            ScaleHost.Visibility = Visibility.Hidden;
            TitleSearchSlot.Visibility = Visibility.Collapsed;
            // DatabasesBtn stays live: Manage databases is the way to another notepad, or a new
            // one, when this password is lost (SecurityController.ApplyLockedSelection).
            BackupWarningBtn.Visibility = Visibility.Collapsed;   // names the notepad's backup state
            PadlockGlyph.Visibility = Visibility.Collapsed;       // SaveState.cs - no notepad on screen
            LockedPanel.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() => { if (LockedPanel.IsVisible) LockedUnlockBtn.Focus(); }), DispatcherPriority.Input);
        }

        /// <summary>What was searched for is the locked notepad's too, and the next one to open
        /// would show it. The boxes empty without their handlers: each would search a store that
        /// is about to close, and may be the reason this lock could not save.</summary>
        private void ForgetSearches()
        {
            CloseFindBar();                  // FindBar.cs
            SearchBox.TextChanged -= SearchBox_TextChanged;
            FindBox.TextChanged -= FindBox_TextChanged;
            SearchBox.Text = FindBox.Text = ReplaceBox.Text = SidebarReplaceBox.Text = "";
            _findTerm = "";
            SearchBox.TextChanged += SearchBox_TextChanged;
            FindBox.TextChanged += FindBox_TextChanged;
            // Ctrl+Z in an empty box would otherwise bring the words back for the next notepad.
            foreach (var box in new[] { SearchBox, FindBox, ReplaceBox, SidebarReplaceBox, TitleBox })
            {
                box.IsUndoEnabled = false;
                box.IsUndoEnabled = true;
            }
        }

        void ISecurityHost.LeaveLocked()
        {
            LockedPanel.Visibility = Visibility.Collapsed;
            ScaleHost.Visibility = Visibility.Visible;
            TitleSearchSlot.Visibility = Visibility.Visible;
            ShowPrivacyState();   // SaveState.cs
            HandlePendingOpenFile();         // Sharing.cs - a .knote or .kndb opened while locked
        }

        /// <summary>The open note's edits go into the held store, with the audio of any recording
        /// not saved yet: that lives only in the editor's memory, which reopening a note forgets.</summary>
        private void HoldOpenEdits() =>
            _heldEdits.Hold(LockedEdits.Capture(Editor.Document, _currentId, TitleBox.Text, NoteStore.DatabaseId,
                _currentFormat, _notes.FirstOrDefault(n => n.Id == _currentId)?.Created, _recordingCache?.Values));

        /// <summary>Puts edits held through an automatic lock back into their notes, once their own
        /// database is open again (SecurityHost.LoadNotes, after every open). The editor holds one
        /// note at a time, so each waits until the one before it has saved. Edits whose note is gone
        /// or has changed stay held, without holding up the others, and the status line says so. A
        /// read-only database cannot take them yet.</summary>
        private void RestoreLockedEdits()
        {
            if (NoteStore.IsReadOnly) return;
            foreach (var held in _heldEdits.For(NoteStore.DatabaseId))
            {
                if (!SaveCurrentNote(refreshList: false)) return;
                if (NoteStore.GetNote(held.NoteId) is not { } note || !held.Fits(note)) continue;
                if (_currentId != held.NoteId)
                {
                    // OpenNote finds notes through the sidebar, and a search left over from another
                    // notepad may be hiding this one.
                    if (!_notes.Any(n => n.Id == held.NoteId)) SearchBox.Text = "";
                    OpenNote(held.NoteId);
                }
                if (PutBack(held)) StatusText.Text = Loc("Str_St_LockEditsBack");
            }
            if (_heldEdits.For(NoteStore.DatabaseId).Count > 0) StatusText.Text = Loc("Str_St_LockEditsStuck");
        }

        private bool PutBack(LockedEdits held)
        {
            if (!held.TryRestore(Editor.Document, _currentId, NoteStore.DatabaseId)) return false;
            foreach (var recording in held.Recordings) Remember(recording);
            _heldEdits.Done(held);
            TitleBox.Text = held.Title;
            _findPlainStale = true;
            MarkDirty();                     // the next autosave tries again
            return true;
        }

        /// <summary>Closes every other window, except that the sketch pad and dictation are only
        /// hidden: an unfinished drawing or recording is this database's content, and comes back
        /// when this database is open again. Until then the tool is out of its slot, so another
        /// notepad's F7 or F8 opens an empty one rather than reaching it.</summary>
        private void CloseWindowsForLock(bool includeTools)
        {
            foreach (var window in Application.Current.Windows.OfType<Window>().ToList())
            {
                if (window == this || !window.IsVisible) continue;
                if (window == _sketchPad || window == _dictation)
                {
                    if (!includeTools) continue;
                    window.Hide();
                    if (window == _sketchPad) HoldSketchPad();
                    else HoldDictation();
                }
                else
                {
                    window.Close();
                }
            }
        }

        private void HoldSketchPad()
        {
            var pad = _sketchPad!;
            var session = _sketchSession;
            var target = _sketchEditTarget;
            _sketchPad = null;
            _sketchSession = null;
            _sketchEditTarget = null;
            SketchRailBtn.Tag = null;
            // A pad can outlive a switch of notepads, so its owner is the one it was opened in,
            // not whichever one is locking now.
            _heldTools.Hold(session?.Database ?? ToolOwner, pad, () =>
            {
                if (_sketchPad != null) return false;
                _sketchPad = pad;
                _sketchSession = session;
                if (NoteStore.DatabaseId is { } database) session?.Rebind(database);   // a password change while held
                // The placed sketch it was editing survives only if its edits were held: after
                // an ordinary lock the note reloaded with a new picture, so Print makes a new one.
                _sketchEditTarget = target != null && ImageInDocument(target) ? target : null;
                SketchRailBtn.Tag = "on";
                pad.Show();
                return true;
            });
        }

        private void HoldDictation()
        {
            var pad = _dictation!;
            string? owner = _dictationOwner ?? ToolOwner;
            _dictation = null;
            _dictationOwner = null;
            DictationRailBtn.Tag = null;
            _heldTools.Hold(owner, pad, () =>
            {
                if (_dictation != null) return false;
                _dictation = pad;
                _dictationOwner = NoteStore.DatabaseId;
                if (_dictationOwner != null) pad.RebindDatabase(_dictationOwner);   // a password change while held
                DictationRailBtn.Tag = "on";
                pad.Show();
                return true;
            });
        }

        /// <summary>Who a pad belongs to when its database has no id: a read-only file from before
        /// ids, which this copy cannot write one into. Its path and password are all there is.</summary>
        private static string ToolOwner => NoteStore.DatabaseId ?? "path:" + System.IO.Path.GetFullPath(NoteStore.DbPath);

        /// <summary>A password change gave the open database a new id. It is the same database,
        /// and whoever changed the password has just proved its key, so its work goes with it.</summary>
        private void OnDatabaseRekeyed(string oldId, string newId)
        {
            _heldEdits.Rebind(oldId, newId);
            _heldTools.Rebind(oldId, newId);
            if (_sketchSession?.Database == oldId) _sketchSession.Rebind(newId);
            if (_dictationOwner == oldId)
            {
                _dictationOwner = newId;
                _dictation?.RebindDatabase(newId);
            }
        }

        // Set once the quit prompt is answered, because the close fade calls Close again. The held
        // edits stay until the quit is certain: a save that fails after the answer calls it off.
        private bool _quitAccepted;

        /// <summary>Asked when the app quits with edits held: quitting loses them, so the safe
        /// answer (Escape) keeps the app running. Their titles stay off the prompt unless their
        /// own database is open: anyone at a locked screen can reach it with Alt+F4.</summary>
        private bool ConfirmQuitWhileLocked()
        {
            if (!_heldEdits.Any || _quitAccepted) return true;
            var dlg = new ConfirmDialog(
                Loc("Str_Lock_QuitHead"),
                _heldEdits.AllBelongTo(NoteStore.DatabaseId)
                    ? string.Format(Loc("Str_Lock_QuitBody"), string.Join(", ", _heldEdits.Titles))
                    : Loc("Str_Lock_QuitBodyOther"),
                Loc("Str_Lock_QuitAnyway"),
                Loc("Str_Lock_KeepRunning")) { Owner = this };
            dlg.ShowDialog();
            _quitAccepted = dlg.Confirmed;
            return dlg.Confirmed;
        }

        /// <summary>The shutdown that was accepted did not happen, so held edits are held again.</summary>
        internal void QuitCalledOff() => _quitAccepted = false;

        /// <summary>An update or an install is about to shut the app down, and a shutdown ignores
        /// the close prompt's answer, so both ask here, first. True when it can go ahead.</summary>
        internal bool ReadyToShutDown()
        {
            if (!ConfirmQuitWhileLocked()) return false;
            if (SaveCurrentNote(refreshList: false)) return true;
            _quitAccepted = false;
            return false;
        }

        /// <summary>Windows ending the session with edits held. They are only in memory, and with
        /// their database's key gone they cannot be saved anywhere, so the answer is no: the window
        /// comes back to say why. A prompt cannot do this job, since a session end does not wait
        /// for one, and the window's own close confirmation arrives too late to stop it.</summary>
        private void KeepSessionForHeldEdits(object sender, SessionEndingCancelEventArgs e)
        {
            if (e.Cancel || !_heldEdits.Any) return;
            e.Cancel = true;
            _tray.ShowWindow();
            StatusText.Text = Loc("Str_St_SessionHeld");
        }

        // ---- The lock button and the locked screen ----

        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            if (_security.IsLocked) { _security.Unlock(); return; }
            if (!NoteStore.IsOpen) return;
            if (!NoteStore.HasPassword) { _security.ToggleLock(); return; }

            var menu = new ContextMenu();
            FlyoutPlacement.UsePane(ContentPane);
            FlyoutPlacement.AttachTopRight(menu);   // the pane's top-right corner, 8px in
            var lockItem = new MenuItem { Header = Loc("Str_Lock_Now"), Icon = "\uE72E", InputGestureText = "Ctrl+Shift+U" };
            lockItem.Click += (_, _) => LockNow(automatic: false);
            var passwordItem = new MenuItem { Header = Loc("Str_Pw_ChangeHead"), Icon = "\uE8D7" };
            passwordItem.Click += (_, _) => _security.ToggleLock();
            menu.Items.Add(lockItem);
            menu.Items.Add(passwordItem);
            menu.Opened += (_, _) => menu.Dispatcher.BeginInvoke(new Action(() => { if (menu.IsOpen) lockItem.Focus(); }),
                DispatcherPriority.Input);
            menu.IsOpen = true;
        }

        private void LockedUnlock_Click(object sender, RoutedEventArgs e) => _security.Unlock();

        /// <summary>While locked only the locked screen's own keys work: Tab and Enter/Space reach
        /// the Unlock button, Ctrl+Shift+U unlocks, Alt+F4 still quits. Everything else would act
        /// on notes that are not there. Returns true for every key so the window's shortcuts never
        /// see one; the allowed keys are left unhandled for WPF's own focus and button handling.</summary>
        private bool HandleLockedKey(KeyEventArgs e)
        {
            if (!_security.IsLocked) return false;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Tab or Key.Enter or Key.Space) return true;
            if (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) return true;
            if (key == Key.U && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && !e.IsRepeat)
                _security.Unlock();
            e.Handled = true;
            return true;
        }

        // ---- About card options ----

        private bool _initializingLockOptions;

        private void InitLockOptions()
        {
            _initializingLockOptions = true;
            AboutLockWithWindows.IsChecked = AutoLock.WindowsLockEnabled(App.GetSetting(AutoLock.WindowsLockSetting));
            int idle = AutoLock.ParseIdleMinutes(App.GetSetting(AutoLock.IdleSetting));
            AboutIdleLockCombo.Items.Clear();
            foreach (int minutes in AutoLock.IdleChoices)
            {
                var item = new ComboBoxItem
                {
                    Tag = minutes,
                    Content = minutes == 0 ? Loc("Str_About_IdleLockOff") : string.Format(Loc("Str_About_IdleLockMinutes"), minutes),
                };
                AboutIdleLockCombo.Items.Add(item);
                if (minutes == idle) AboutIdleLockCombo.SelectedItem = item;
            }
            _initializingLockOptions = false;
        }

        private void AboutLockWithWindows_Click(object sender, RoutedEventArgs e)
            => App.SetSetting(AutoLock.WindowsLockSetting, AboutLockWithWindows.IsChecked == true ? "1" : "0");

        private void AboutIdleLock_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializingLockOptions || AboutIdleLockCombo.SelectedItem is not ComboBoxItem { Tag: int minutes }) return;
            App.SetSetting(AutoLock.IdleSetting, minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

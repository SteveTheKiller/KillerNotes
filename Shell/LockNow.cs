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
        private LockedEdits? _lockedEdits;
        private readonly List<Window> _hiddenForLock = [];
        private DispatcherTimer? _idleLockTimer;
        private int _lockRetries;

        private void InitLockNow()
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            // SystemEvents are static: a handler left on them keeps this window alive and fires
            // into a closed one.
            Closed += (_, _) =>
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
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
                StatusText.Text = Loc("Str_St_Locked");
        }

        void ISecurityHost.EnterLocked(bool keepUnsavedEdits)
        {
            StopEmbeddedPlayback();          // Editor.Dictation.cs
            CancelBackgroundJob();             // VaultExport.cs - an export or import reads with the notepad's key
            _dictation?.StopForLock();       // the microphone is never live behind a locked screen
            CloseWindowsForLock(includeTools: true);
            CancelGlobalSearchPreview();     // GlobalSearch.cs
            _globalMatchCounts.Clear();
            _globalFindHits.Clear();
            _saveTimer.Stop();

            if (keepUnsavedEdits && _currentId >= 0 && !_loadFailed)
            {
                _lockedEdits = LockedEdits.Capture(Editor.Document, _currentId, TitleBox.Text, NoteStore.ActiveDbFile, _currentFormat);
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
            ClearNavHistory();               // NoteHistory.cs - titles and ids of the locked notepad
            _notes = [];
            _trashNotes = [];
            _syncingSelection = true;        // emptying the list must not read as picking a note
            _sidebarItems.Clear();
            _syncingSelection = false;

            ScaleHost.Visibility = Visibility.Hidden;
            TitleSearchSlot.Visibility = Visibility.Collapsed;
            DatabasesBtn.IsEnabled = false;
            BackupWarningBtn.Visibility = Visibility.Collapsed;   // names the notepad's backup state
            LockedPanel.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() => { if (LockedPanel.IsVisible) LockedUnlockBtn.Focus(); }), DispatcherPriority.Input);
        }

        void ISecurityHost.LeaveLocked()
        {
            LockedPanel.Visibility = Visibility.Collapsed;
            ScaleHost.Visibility = Visibility.Visible;
            TitleSearchSlot.Visibility = Visibility.Visible;
            DatabasesBtn.IsEnabled = true;
            foreach (var window in _hiddenForLock) window.Show();
            _hiddenForLock.Clear();
            HandlePendingOpenFile();         // Sharing.cs - a .knote or .kndb opened while locked
        }

        /// <summary>Puts edits held through an automatic lock back into their note, once their own
        /// database is open again (SecurityHost.LoadNotes, after every open).</summary>
        private void RestoreLockedEdits()
        {
            if (_lockedEdits is not { } held) return;
            if (!string.Equals(held.Database, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase)) return;
            if (_currentId != held.NoteId && _notes.Any(n => n.Id == held.NoteId)) OpenNote(held.NoteId);
            if (!held.TryRestore(Editor.Document, _currentId, NoteStore.ActiveDbFile)) return;
            _lockedEdits = null;
            TitleBox.Text = held.Title;
            _findPlainStale = true;
            MarkDirty();                     // the next autosave tries again
            StatusText.Text = Loc("Str_St_LockEditsBack");
        }

        /// <summary>Closes every other window, except that the sketch pad and dictation are only
        /// hidden: an unfinished drawing or recording comes back after the unlock.</summary>
        private void CloseWindowsForLock(bool includeTools)
        {
            foreach (var window in Application.Current.Windows.OfType<Window>().ToList())
            {
                if (window == this || !window.IsVisible) continue;
                if (window == _sketchPad || window == _dictation)
                {
                    if (!includeTools) continue;
                    window.Hide();
                    _hiddenForLock.Add(window);
                }
                else
                {
                    window.Close();
                }
            }
        }

        /// <summary>Asked when the app quits while locked with edits held: quitting loses them,
        /// so the safe answer (Escape) keeps the app running.</summary>
        private bool ConfirmQuitWhileLocked()
        {
            if (_lockedEdits == null) return true;
            var dlg = new ConfirmDialog(
                Loc("Str_Lock_QuitHead"),
                string.Format(Loc("Str_Lock_QuitBody"), _lockedEdits.Title),
                Loc("Str_Lock_QuitAnyway"),
                Loc("Str_Lock_KeepRunning")) { Owner = this };
            dlg.ShowDialog();
            if (dlg.Confirmed) _lockedEdits = null;   // answered once; the close fade calls Close again
            return dlg.Confirmed;
        }

        // ---- The lock button and the locked screen ----

        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            if (_security.IsLocked) { _security.Unlock(); return; }
            if (!NoteStore.IsOpen) return;
            if (!NoteStore.HasPassword) { _security.ToggleLock(); return; }

            var menu = new ContextMenu { PlacementTarget = LockButton, Placement = PlacementMode.Bottom };
            var lockItem = new MenuItem { Header = Loc("Str_Lock_Now"), InputGestureText = "Ctrl+Shift+U" };
            lockItem.Click += (_, _) => LockNow(automatic: false);
            var passwordItem = new MenuItem { Header = Loc("Str_Pw_ChangeHead") };
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

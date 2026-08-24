using System;
using System.IO;
using Microsoft.Data.Sqlite;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Features.Security
{
    /// <summary>
    /// Password protection + database lifecycle: unlock-on-launch, the title-bar lock button
    /// (set / change / remove), the unlock screen's "New database" escape hatch (a forgotten
    /// password can never be recovered - AES-256 by design - but the app must stay usable), and the
    /// Manage databases dialog for switching between files.
    /// </summary>
    internal sealed class SecurityController
    {
        private readonly ISecurityHost _host;

        private string? _dbPassword;     // password of the open db, reused for silent reopens
        private string? _pendingStatus;  // status line to show after the list refresh

        internal SecurityController(ISecurityHost host) => _host = host;

        /// <summary>Opens the active database at launch; canceling the unlock exits the app.</summary>
        internal void OpenAtLaunch() => Open(exitOnCancel: true);

        /// <summary>Forgets the session password. Called when switching to a different file, whose
        /// password is not ours to try.</summary>
        internal void ForgetSessionPassword() => _dbPassword = null;

        /// <summary>Opens the active database, prompting to unlock when encrypted. Returns
        /// false when the user cancels (the app exits instead when exitOnCancel is set).</summary>
        internal bool Open(bool exitOnCancel)
        {
            try
            {
                if (NoteStore.IsEncrypted())
                {
                    // Silent retry with the session's known password first, so the Manage
                    // databases round trip and db switches never re-prompt needlessly.
                    if (_dbPassword != null)
                    {
                        try { NoteStore.Open(_dbPassword); }
                        catch (SqliteException) { }
                    }
                    if (!NoteStore.IsOpen && !PromptUnlock(exitOnCancel)) return false;
                }
                else
                {
                    NoteStore.Open();
                    _dbPassword = null;
                }
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_St_OpenDbFailed"), ex.Message));
                return false;
            }

            WarnNetworkFolder();
            WarnReadOnly();
            _host.ApplyReadOnlyState();
            _host.LoadNotes();
            _host.ShowLockState(NoteStore.HasPassword);
            if (_pendingStatus != null) { _host.SetStatus(_pendingStatus); _pendingStatus = null; }
            return true;
        }

        /// <summary>One-time warning when the data folder is on a network location: SQLite over
        /// SMB with two writers corrupts, and the lock file only mitigates. Remembered per folder,
        /// so changing the data folder to a different share warns once more.</summary>
        private void WarnNetworkFolder()
        {
            if (!NoteStore.IsNetworkPath(NoteStore.DbDir)) return;
            if (string.Equals(App.GetSetting("NetworkWarnedFolder"), NoteStore.DbDir, StringComparison.OrdinalIgnoreCase)) return;
            App.SetSetting("NetworkWarnedFolder", NoteStore.DbDir);

            var dlg = new ConfirmDialog(
                _host.Loc("Str_Net_WarnHead"),
                string.Format(_host.Loc("Str_Net_WarnBody"), NoteStore.DbDir),
                _host.Loc("Str_Btn_OK")) { Owner = _host.Window };
            dlg.CancelButton.Visibility = System.Windows.Visibility.Collapsed;
            dlg.ShowDialog();
        }

        /// <summary>Tells the user the database opened read-only and which host owns it.</summary>
        private void WarnReadOnly()
        {
            if (!NoteStore.IsReadOnly) return;
            var dlg = new ConfirmDialog(
                _host.Loc("Str_Net_ReadOnlyHead"),
                string.Format(_host.Loc("Str_Net_ReadOnlyBody"), NoteStore.ReadOnlyOwner),
                _host.Loc("Str_Btn_OK")) { Owner = _host.Window };
            dlg.CancelButton.Visibility = System.Windows.Visibility.Collapsed;
            dlg.ShowDialog();
        }

        private bool PromptUnlock(bool exitOnCancel)
        {
            string heading = _host.Loc("Str_Pw_UnlockHead");
            while (true)
            {
                var dlg = new PasswordDialog(heading,
                    string.Format(_host.Loc("Str_Pw_Protected"), NoteStore.ActiveDbFile),
                    _host.Loc("Str_Btn_Unlock"),
                    extraText: _host.Loc("Str_Pw_NewDbBtn")) { Owner = _host.Window };
                dlg.ShowDialog();

                if (dlg.ExtraClicked)
                {
                    if (StartFreshDatabase()) return true;
                    continue;   // declined the confirm - back to the unlock prompt
                }
                if (!dlg.Confirmed)
                {
                    if (exitOnCancel) _host.Window.Close();
                    return false;
                }
                try
                {
                    NoteStore.Open(dlg.Password);
                    _dbPassword = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password;
                    return true;
                }
                catch (SqliteException) { heading = _host.Loc("Str_Pw_WrongPw"); }
            }
        }

        // The escape hatch for a forgotten password: the data in the locked file is not
        // recoverable (that is the point of the encryption), but the app must not be a
        // brick. The locked file is kept on disk and stays visible in Manage databases.
        private bool StartFreshDatabase()
        {
            var confirm = new ConfirmDialog(
                _host.Loc("Str_Dlg_FreshHead"),
                _host.Loc("Str_Dlg_FreshBody"),
                _host.Loc("Str_Btn_StartNew")) { Owner = _host.Window };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return false;

            string archived = NoteStore.ArchiveDatabase();
            NoteStore.Open();
            _dbPassword = null;
            _pendingStatus = string.Format(_host.Loc("Str_St_FreshDb"), Path.GetFileName(archived));
            return true;
        }

        // ---- Manage databases (title-bar button) ----
        // The store is closed for the duration of the dialog, so every file - including
        // the active one - can be renamed or deleted safely. Reopening afterwards reuses
        // the session password silently where it still fits.

        internal void ShowDatabases()
        {
            _host.SaveOpenNote();
            string prevFile = NoteStore.ActiveDbFile;
            NoteStore.Close();

            var dlg = new DatabasesDialog { Owner = _host.Window };
            dlg.ShowDialog();

            if (dlg.SelectedDatabase != null &&
                !string.Equals(dlg.SelectedDatabase, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase))
            {
                App.SetSetting("ActiveDatabase", dlg.SelectedDatabase);
                _dbPassword = null;   // different file - its password is not ours to try
            }

            _host.ClearEditor();
            if (!Open(exitOnCancel: false))
            {
                // Unlock of the chosen db was canceled - fall back to the previous one.
                App.SetSetting("ActiveDatabase", prevFile);
                Open(exitOnCancel: false);
            }
        }

        // ---- Lock button: set / change / remove the password ----

        internal void ToggleLock()
        {
            if (!NoteStore.IsOpen) return;
            _host.SaveOpenNote();

            try
            {
                if (!NoteStore.HasPassword)
                {
                    var dlg = new PasswordDialog(
                        _host.Loc("Str_Pw_SetHead"),
                        _host.Loc("Str_Pw_SetBody"),
                        _host.Loc("Str_Btn_Encrypt"), showConfirm: true) { Owner = _host.Window };
                    dlg.ShowDialog();
                    if (!dlg.Confirmed || string.IsNullOrEmpty(dlg.Password)) return;
                    if (dlg.Password != dlg.PasswordConfirm)
                    {
                        _host.SetStatus(_host.Loc("Str_St_PwMismatch"));
                        return;
                    }
                    NoteStore.SetPassword(dlg.Password);
                    _dbPassword = dlg.Password;
                    _host.SetStatus(_host.Loc("Str_St_Encrypted"));
                }
                else
                {
                    var dlg = new PasswordDialog(
                        _host.Loc("Str_Pw_ChangeHead"),
                        _host.Loc("Str_Pw_ChangeBody"),
                        _host.Loc("Str_Btn_Apply"), showConfirm: true) { Owner = _host.Window };
                    dlg.ShowDialog();
                    if (!dlg.Confirmed) return;
                    if (dlg.Password != dlg.PasswordConfirm)
                    {
                        _host.SetStatus(_host.Loc("Str_St_PwMismatch"));
                        return;
                    }
                    NoteStore.SetPassword(string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password);
                    _dbPassword = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password;
                    _host.SetStatus(_host.Loc(NoteStore.HasPassword ? "Str_St_PwChanged" : "Str_St_PwRemoved"));
                }
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_St_PwFailed"), ex.Message));
            }
            _host.ShowLockState(NoteStore.HasPassword);
        }
    }
}

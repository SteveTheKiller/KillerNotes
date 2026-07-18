using System;
using System.IO;
using System.Windows;
using Microsoft.Data.Sqlite;
using KillerNotes.Services;

namespace KillerNotes
{
    // Password protection + database lifecycle: unlock-on-launch, the title-bar lock
    // button (set / change / remove), the unlock screen's "New database" escape hatch
    // (a forgotten password can never be recovered - AES-256 by design - but the app
    // must stay usable), and the Manage databases dialog for switching between files.
    public partial class MainWindow
    {
        private string? _dbPassword;     // password of the open db, reused for silent reopens
        private string? _pendingStatus;  // status line to show after the list refresh

        private void OpenDatabase() => OpenDatabase(exitOnCancel: true);

        /// <summary>Opens the active database, prompting to unlock when encrypted. Returns
        /// false when the user cancels (the app exits instead when exitOnCancel is set).</summary>
        private bool OpenDatabase(bool exitOnCancel)
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
                StatusText.Text = "Could not open the notes database: " + ex.Message;
                return false;
            }

            InitNotes();      // Notes.cs (idempotent)
            RefreshList();
            OpenStartupNote();   // Notes.cs - the app always opens into a note
            UpdateLockGlyph();
            if (_pendingStatus != null) { StatusText.Text = _pendingStatus; _pendingStatus = null; }
            return true;
        }

        private bool PromptUnlock(bool exitOnCancel)
        {
            string heading = "Unlock KillerNotes";
            while (true)
            {
                var dlg = new PasswordDialog(heading,
                    $"\"{NoteStore.ActiveDbFile}\" is password protected.", "Unlock",
                    extraText: "New database...") { Owner = this };
                dlg.ShowDialog();

                if (dlg.ExtraClicked)
                {
                    if (StartFreshDatabase()) return true;
                    continue;   // declined the confirm - back to the unlock prompt
                }
                if (!dlg.Confirmed)
                {
                    if (exitOnCancel) Close();
                    return false;
                }
                try
                {
                    NoteStore.Open(dlg.Password);
                    _dbPassword = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password;
                    return true;
                }
                catch (SqliteException) { heading = "Wrong password - try again"; }
            }
        }

        // The escape hatch for a forgotten password: the data in the locked file is not
        // recoverable (that is the point of the encryption), but the app must not be a
        // brick. The locked file is kept on disk and stays visible in Manage databases.
        private bool StartFreshDatabase()
        {
            var confirm = new ConfirmDialog(
                "Start a new database?",
                "The locked database stays on disk and can still be unlocked later if the\npassword turns up. There is no way to recover its notes without it.",
                "Start new") { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return false;

            string archived = NoteStore.ArchiveDatabase();
            NoteStore.Open();
            _dbPassword = null;
            _pendingStatus = "New database - the locked one is kept as " + Path.GetFileName(archived);
            return true;
        }

        // ---- Manage databases (title-bar button) ----
        // The store is closed for the duration of the dialog, so every file - including
        // the active one - can be renamed or deleted safely. Reopening afterwards reuses
        // the session password silently where it still fits.

        private void ManageDatabases_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentNote(refreshList: false);
            string prevFile = NoteStore.ActiveDbFile;
            NoteStore.Close();

            var dlg = new DatabasesDialog { Owner = this };
            dlg.ShowDialog();

            if (dlg.SelectedDatabase != null &&
                !string.Equals(dlg.SelectedDatabase, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase))
            {
                App.SetSetting("ActiveDatabase", dlg.SelectedDatabase);
                _dbPassword = null;   // different file - its password is not ours to try
            }

            _currentId = -1;
            ShowEditor(false);
            if (!OpenDatabase(exitOnCancel: false))
            {
                // Unlock of the chosen db was cancelled - fall back to the previous one.
                App.SetSetting("ActiveDatabase", prevFile);
                OpenDatabase(exitOnCancel: false);
            }
        }

        // ---- Lock button: set / change / remove the password ----

        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            if (!NoteStore.IsOpen) return;
            SaveCurrentNote(refreshList: false);

            try
            {
                if (!NoteStore.HasPassword)
                {
                    var dlg = new PasswordDialog(
                        "Set a password",
                        "Encrypts the whole notes database (AES-256 via SQLCipher).\nDo not lose it - there is no recovery.",
                        "Encrypt", showConfirm: true) { Owner = this };
                    dlg.ShowDialog();
                    if (!dlg.Confirmed || string.IsNullOrEmpty(dlg.Password)) return;
                    if (dlg.Password != dlg.PasswordConfirm)
                    {
                        StatusText.Text = "Passwords did not match - nothing changed";
                        return;
                    }
                    NoteStore.SetPassword(dlg.Password);
                    _dbPassword = dlg.Password;
                    StatusText.Text = "Database encrypted";
                }
                else
                {
                    var dlg = new PasswordDialog(
                        "Change or remove password",
                        "Enter a new password, or leave both boxes empty to remove protection.",
                        "Apply", showConfirm: true) { Owner = this };
                    dlg.ShowDialog();
                    if (!dlg.Confirmed) return;
                    if (dlg.Password != dlg.PasswordConfirm)
                    {
                        StatusText.Text = "Passwords did not match - nothing changed";
                        return;
                    }
                    NoteStore.SetPassword(string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password);
                    _dbPassword = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password;
                    StatusText.Text = NoteStore.HasPassword
                        ? "Password changed"
                        : "Password removed - database is no longer encrypted";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Password change failed: " + ex.Message;
            }
            UpdateLockGlyph();
        }

        // Lock (0xE72E) when encrypted, unlock (0xE785) when plaintext (Segoe MDL2).
        // Written as char casts so the private-use glyphs can never be mangled by tooling.
        private void UpdateLockGlyph()
            => LockButton.Content = ((char)(NoteStore.HasPassword ? 0xE72E : 0xE785)).ToString();
    }
}

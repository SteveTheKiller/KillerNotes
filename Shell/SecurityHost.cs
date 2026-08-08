using System.Windows;
using KillerNotes.Features;

// MainWindow's side of the security feature: it satisfies ISecurityHost and forwards the two
// button clicks. All the behaviour lives in Features/Security/SecurityController.cs.
namespace KillerNotes.Shell
{
    public partial class MainWindow : ISecurityHost
    {
        private readonly SecurityController _security = null!;

        // ---- ISecurityHost ----

        /// <summary>Lock (0xE72E) when encrypted, unlock (0xE785) when plaintext, Segoe MDL2.
        /// Written as char casts so the private-use glyphs can never be mangled by tooling.</summary>
        void ISecurityHost.ShowLockState(bool encrypted)
            => LockButton.Content = ((char)(encrypted ? 0xE72E : 0xE785)).ToString();

        // In-memory resume hint for the security round trips (Manage databases, lock/unlock).
        // The "LastNote" SETTING cannot serve here: demo sessions never write it on purpose, so
        // cancelling the Databases dialog in a demo fell through to the most-recently-modified
        // fallback and jumped the user to a different note (2026-08-08). Captured when a round
        // trip saves the open note; consumed by OpenStartupNote only when the SAME database
        // comes back.
        private long _resumeNoteId = -1;
        private string _resumeDb = "";

        void ISecurityHost.SaveOpenNote()
        {
            _resumeDb = NoteStore.ActiveDbFile;
            _resumeNoteId = _currentId;
            SaveCurrentNote(refreshList: false);   // Notes.cs
        }

        void ISecurityHost.LoadNotes()
        {
            InitNotes();         // Notes.cs (idempotent)
            RefreshList();
            OpenStartupNote();   // Notes.cs - the app always opens into a note
        }

        void ISecurityHost.ClearEditor()
        {
            _currentId = -1;
            ShowEditor(false);
            // No TextChanged fires on this path, so without an explicit pass the gutter keeps
            // the last note's numbers and width beside the lock screen; the rebuild sees
            // _currentId < 0 and collapses it.
            RebuildLineNumbers();
        }

        // ---- Entry points the rest of the shell calls ----

        /// <summary>Opens the active database at launch (MainWindow ctor); cancelling exits.</summary>
        private void OpenDatabase() => _security.OpenAtLaunch();

        /// <summary>Opens the active database after a switch (Sharing.cs, and the controller's own
        /// Manage databases round trip). Returns false when the unlock was cancelled.</summary>
        private bool OpenDatabase(bool exitOnCancel) => _security.Open(exitOnCancel);

        /// <summary>Drops the session password: the file being opened is a different one, so its
        /// password is not ours to try (Sharing.cs).</summary>
        private void ForgetDbPassword() => _security.ForgetSessionPassword();

        // ---- Handlers (wired in MainWindow.xaml) ----

        private void ManageDatabases_Click(object sender, RoutedEventArgs e) => _security.ShowDatabases();

        private void LockButton_Click(object sender, RoutedEventArgs e) => _security.ToggleLock();
    }
}

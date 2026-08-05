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

        void ISecurityHost.SaveOpenNote() => SaveCurrentNote(refreshList: false);   // Notes.cs

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

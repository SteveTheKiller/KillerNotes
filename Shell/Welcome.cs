// ═══════════════════════════════════════════════════════════
//  WELCOME  -  a notepad with no notes, and the sample notebook
// ═══════════════════════════════════════════════════════════
//
// 1.6.0, KN-P09. A notepad with no notes at all (a first run, or the last note moved to the
// trash and the trash emptied) used to get a blank note made for it. It now shows three ways to
// begin instead: start writing, open the sample notebook, or import a folder of notes.
//
// The sample notebook is its own notepad, "Sample notes", built once from the demo content and
// switched to like any other database: it is never mixed into the user's notes, and Manage
// databases deletes it. The content is English for now.

using System;
using System.IO;
using System.Windows;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        internal const string SampleNotebookFile = "Sample notes.db";

        /// <summary>Shows or hides the three ways to begin over the empty area.</summary>
        private void ShowWelcome(bool show)
        {
            if (WelcomePanel == null) return;
            WelcomePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            WelcomeSampleBtn.IsEnabled = NoteStore.DemoDbFile == null &&
                !string.Equals(NoteStore.ActiveDbFile, SampleNotebookFile, StringComparison.OrdinalIgnoreCase);
            WelcomeImportBtn.IsEnabled = WelcomeWriteBtn.IsEnabled = !NoteStore.IsReadOnly;
            if (show)
                Dispatcher.BeginInvoke(new Action(() => { if (WelcomePanel.IsVisible) WelcomeWriteBtn.Focus(); }),
                    System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>True when the open notepad holds no notes, in the trash or out of it.</summary>
        private static bool NotepadIsEmpty() =>
            NoteStore.IsOpen && NoteStore.List().Count == 0 && NoteStore.ListTrash().Count == 0;

        private void WelcomeWrite_Click(object sender, RoutedEventArgs e)
        {
            if (!NoteStore.IsOpen) return;
            CreateNewNote(focusTitle: true);
        }

        private void WelcomeSample_Click(object sender, RoutedEventArgs e) => OpenSampleNotebook();

        /// <summary>Switches to the "Sample notes" notepad, building it first the one time it does
        /// not exist yet. The previous notepad stays in Manage databases, untouched.</summary>
        private void OpenSampleNotebook()
        {
            if (!NoteStore.IsOpen || NoteStore.DemoDbFile != null) return;
            if (!SaveCurrentNote(refreshList: false)) return;
            string prevFile = NoteStore.ActiveDbFile;
            bool fresh = !File.Exists(Path.Combine(NoteStore.DbDir, SampleNotebookFile));
            try
            {
                NoteStore.Close();
                App.SetSetting("ActiveDatabase", SampleNotebookFile);
                ForgetDbPassword();   // SecurityHost.cs - a different file, its password is not ours
                _currentId = -1;
                if (fresh)
                {
                    // Built on its own first, so the usual open below finds it full and opens
                    // straight into a note rather than into this welcome screen.
                    NoteStore.Open();
                    GenerateDemoNotes();   // DemoMode.cs
                    NoteStore.Close();
                    _currentId = -1;
                }
                if (!OpenDatabase(exitOnCancel: false))
                {
                    App.SetSetting("ActiveDatabase", prevFile);
                    OpenDatabase(exitOnCancel: false);
                    return;
                }
                FlashStatus(Loc(fresh ? "Str_St_SampleMade" : "Str_St_SampleOpened"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                StatusText.Text = string.Format(Loc("Str_St_OpenDbFailed"), ex.Message);
                App.SetSetting("ActiveDatabase", prevFile);
                if (!NoteStore.IsOpen) OpenDatabase(exitOnCancel: false);
            }
        }
    }
}

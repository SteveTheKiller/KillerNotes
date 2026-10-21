namespace KillerNotes.Features.Security
{
    /// <summary>
    /// What SecurityController needs from the window hosting it, beyond the shared shell services.
    /// Expressed as intent ("show this lock state") rather than as controls, so the controller holds
    /// no reference to any Button or TextBlock and can be driven by a stub in a test.
    /// </summary>
    internal interface ISecurityHost : IShellServices
    {
        /// <summary>Reflects whether the open database is encrypted.</summary>
        void ShowLockState(bool encrypted);

        /// <summary>Flushes the open note before the store is closed or swapped underneath it.</summary>
        bool SaveOpenNote();

        /// <summary>Rebuilds the note list from the freshly opened store and opens a note; the app
        /// always opens into a note.</summary>
        void LoadNotes();

        void ClearNavigation();

        /// <summary>Drops the editor's current note - what it was showing belongs to a database that
        /// is about to be closed.</summary>
        void ClearEditor();

        /// <summary>Reflects NoteStore.IsReadOnly in the shell after an open: read-only locks the
        /// editor and title, disables New note, and pins a status line naming the owning host.</summary>
        void ApplyReadOnlyState();

        /// <summary>Takes every note off screen for Lock now, just before the store closes: the
        /// editor, the sidebar, open dialogs and caches. keepUnsavedEdits is set when an automatic
        /// lock could not save, so the open note's edits are held in memory for the unlock.</summary>
        void EnterLocked(bool keepUnsavedEdits);

        /// <summary>Puts the shell back after an unlock has reopened the store and loaded the notes.</summary>
        void LeaveLocked();
    }
}

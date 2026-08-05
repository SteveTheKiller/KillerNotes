namespace KillerNotes.Features
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
        void SaveOpenNote();

        /// <summary>Rebuilds the note list from the freshly opened store and opens a note; the app
        /// always opens into a note.</summary>
        void LoadNotes();

        /// <summary>Drops the editor's current note - what it was showing belongs to a database that
        /// is about to be closed.</summary>
        void ClearEditor();
    }
}

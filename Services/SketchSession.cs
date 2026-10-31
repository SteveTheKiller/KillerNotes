namespace KillerNotes.Services
{
    internal sealed class SketchSession
    {
        private readonly long _noteId;

        /// <summary>The database this sketch belongs to (NoteStore.DatabaseId). The pad can outlive
        /// a switch to another database, and a lock holds it for this one.</summary>
        public string? Database { get; private set; }

        public SketchSession(long noteId)
        {
            _noteId = noteId;
            Database = NoteStore.DatabaseId;
        }

        public bool CanPrint(long currentNoteId) => NoteStore.IsOpen && !NoteStore.IsReadOnly &&
            currentNoteId == _noteId && Database != null &&
            string.Equals(Database, NoteStore.DatabaseId, System.StringComparison.Ordinal);

        /// <summary>The password of its database changed, which gives it a new id.</summary>
        public void Rebind(string database) => Database = database;
    }
}

namespace KillerNotes.Services
{
    internal sealed class SketchSession
    {
        private readonly long _noteId;
        private readonly string _database;

        public SketchSession(long noteId)
        {
            _noteId = noteId;
            _database = System.IO.Path.GetFullPath(NoteStore.DbPath);
        }

        public bool CanPrint(long currentNoteId) => NoteStore.IsOpen && !NoteStore.IsReadOnly &&
            currentNoteId == _noteId && string.Equals(_database, System.IO.Path.GetFullPath(NoteStore.DbPath),
                System.StringComparison.OrdinalIgnoreCase);
    }
}

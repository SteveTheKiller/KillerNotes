using System;
using System.Linq;
using System.Windows.Documents;

namespace KillerNotes.Features.Security
{
    /// <summary>
    /// The open note's unsaved edits, held through an automatic lock whose save failed. Nobody was
    /// at the keyboard to read the save error, so the edits are kept rather than thrown away, and
    /// they go back into the same note of the same database after the unlock, where the next
    /// autosave tries again.
    ///
    /// The blocks themselves are moved, not serialized: a recording chip, a sketch's strokes and a
    /// floated picture all live on the elements, and a save is the very thing that just failed.
    /// They sit in a detached document while locked, so nothing on screen shows them.
    /// </summary>
    internal sealed class LockedEdits
    {
        private readonly FlowDocument _held = new();

        internal long NoteId { get; }
        internal string Title { get; }
        internal string Database { get; }
        internal int Format { get; }

        private LockedEdits(long noteId, string title, string database, int format)
        {
            NoteId = noteId;
            Title = title;
            Database = database;
            Format = format;
        }

        internal static LockedEdits Capture(FlowDocument editor, long noteId, string title, string database, int format)
        {
            var edits = new LockedEdits(noteId, title, database, format);
            Move(editor, edits._held);
            return edits;
        }

        /// <summary>Puts the edits back over what the unlock loaded, but only into their own note
        /// of their own database. Returns false, and keeps holding them, for anything else.</summary>
        internal bool TryRestore(FlowDocument editor, long openNoteId, string openDatabase)
        {
            if (openNoteId != NoteId || !string.Equals(openDatabase, Database, StringComparison.OrdinalIgnoreCase))
                return false;
            editor.Blocks.Clear();
            Move(_held, editor);
            return true;
        }

        private static void Move(FlowDocument from, FlowDocument to)
        {
            foreach (var block in from.Blocks.ToList())
            {
                from.Blocks.Remove(block);
                to.Blocks.Add(block);
            }
        }
    }
}

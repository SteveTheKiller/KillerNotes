using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Features.Security
{
    /// <summary>
    /// The open note's unsaved edits, held through an automatic lock whose save failed. Nobody was
    /// at the keyboard to read the save error, so the edits are kept rather than thrown away, and
    /// they go back into the same note of the same database after the unlock, where the next
    /// autosave tries again. The database is its id (NoteStore.DatabaseId), which only that file
    /// and exact copies of it carry, so a stranger under the same name or row id gets nothing.
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
        internal string? Database { get; private set; }
        internal int Format { get; }

        /// <summary>When the note was created, null when that was not known at the lock.</summary>
        internal DateTime? Created { get; }

        /// <summary>Audio the chips in these edits point at. A take not saved yet is nowhere else.</summary>
        internal IReadOnlyList<Attachment> Recordings { get; }

        private LockedEdits(long noteId, string title, string? database, int format, DateTime? created, IEnumerable<Attachment>? recordings)
        {
            NoteId = noteId;
            Title = title;
            Database = database;
            Format = format;
            Created = created;
            Recordings = recordings?.ToList() ?? [];
        }

        internal static LockedEdits Capture(FlowDocument editor, long noteId, string title, string? database, int format,
            DateTime? created = null, IEnumerable<Attachment>? recordings = null)
        {
            var edits = new LockedEdits(noteId, title, database, format, created, recordings);
            Move(editor, edits._held);
            return edits;
        }

        /// <summary>True when this stored note is still the one these edits came from. Row ids are
        /// reused after a note is gone, and blocks held from a rich note would lose their pictures
        /// in a note that has become Markdown.</summary>
        internal bool Fits(Note note) =>
            !note.IsDeleted && note.Format == Format && (Created == null || note.Created == Created);

        /// <summary>The password of their database changed, which gives it a new id.</summary>
        internal void Rebind(string newDatabase) => Database = newDatabase;

        /// <summary>Puts the edits back over what the unlock loaded, but only into their own note
        /// of their own database. Returns false, and keeps holding them, for anything else.</summary>
        internal bool TryRestore(FlowDocument editor, long openNoteId, string? openDatabase)
        {
            if (openNoteId != NoteId || Database == null || !string.Equals(openDatabase, Database, StringComparison.Ordinal))
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

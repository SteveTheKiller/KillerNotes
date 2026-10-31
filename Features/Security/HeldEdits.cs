using System;
using System.Collections.Generic;
using System.Linq;

namespace KillerNotes.Features.Security
{
    /// <summary>
    /// Every note's edits still waiting for their own database. An automatic lock whose save failed
    /// adds one; opening another notepad and failing there too adds another, so one lock never
    /// costs the edits held from the last. Each waits, in memory only, for the database it came
    /// from: its key is gone with the lock, so nothing can be written anywhere until it is back.
    /// </summary>
    internal sealed class HeldEdits
    {
        private readonly List<LockedEdits> _held = [];

        internal bool Any => _held.Count > 0;
        internal IReadOnlyList<LockedEdits> All => _held;
        internal IReadOnlyList<string> Titles => [.. _held.Select(edits => edits.Title)];

        internal void Hold(LockedEdits edits) => _held.Add(edits);

        /// <summary>Every edit held for this database, oldest first, as a list of its own so an
        /// entry can finish while the list is walked. A database with no id owns nothing.</summary>
        internal IReadOnlyList<LockedEdits> For(string? database) =>
            database == null ? [] : [.. _held.Where(edits => string.Equals(edits.Database, database, StringComparison.Ordinal))];

        /// <summary>True when everything held belongs to this database, so its titles are its
        /// own to show.</summary>
        internal bool AllBelongTo(string? database) => For(database).Count == _held.Count;

        internal void Done(LockedEdits edits) => _held.Remove(edits);

        /// <summary>The password of a database changed, which gives it a new id.</summary>
        internal void Rebind(string oldDatabase, string newDatabase)
        {
            foreach (var edits in For(oldDatabase)) edits.Rebind(newDatabase);
        }

        internal void Clear() => _held.Clear();
    }
}

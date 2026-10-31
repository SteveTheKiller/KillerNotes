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

        /// <summary>The oldest edits held for this database, or null when it has none. A database
        /// with no id owns nothing.</summary>
        internal LockedEdits? NextFor(string? database) =>
            database == null ? null : _held.FirstOrDefault(edits => string.Equals(edits.Database, database, StringComparison.Ordinal));

        internal void Done(LockedEdits edits) => _held.Remove(edits);

        internal void Clear() => _held.Clear();
    }
}

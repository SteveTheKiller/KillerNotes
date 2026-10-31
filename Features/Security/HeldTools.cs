using System;
using System.Collections.Generic;
using System.Linq;

namespace KillerNotes.Features.Security
{
    /// <summary>
    /// The sketch pad and dictation windows a lock hid, each kept for the database it was open in.
    /// Unfinished work in them is that database's content, so only that database, once really
    /// open, gets them back; any other one starts with empty tools of its own.
    /// </summary>
    internal sealed class HeldTools
    {
        private List<(string? Database, object Tool, Func<bool> Return)> _held = [];

        /// <summary><paramref name="bringBack"/> shows the tool and returns true, or returns false
        /// while its place is taken, and the tool is offered again at the next chance.</summary>
        internal void Hold(string? database, object tool, Func<bool> bringBack) => _held.Add((database, tool, bringBack));

        /// <summary>Brings back what this database owns. A database with no id owns nothing.</summary>
        internal void ReturnFor(string? database)
        {
            if (database == null) return;
            foreach (var held in _held.ToArray())
                if (string.Equals(held.Database, database, StringComparison.Ordinal) && held.Return())
                    _held.Remove(held);
        }

        internal void Forget(object tool) => _held.RemoveAll(held => ReferenceEquals(held.Tool, tool));

        /// <summary>The password of a database changed, which gives it a new id.</summary>
        internal void Rebind(string oldDatabase, string newDatabase) => _held =
            [.. _held.Select(held => string.Equals(held.Database, oldDatabase, StringComparison.Ordinal) ? held with { Database = newDatabase } : held)];
    }
}

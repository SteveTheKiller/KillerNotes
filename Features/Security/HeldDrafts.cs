using System;
using System.Collections.Generic;

namespace KillerNotes.Features.Security
{
    /// <summary>
    /// The quick-note draft of each database that locked with one open. It is the notepad's own
    /// words, so it stays off the locked screen and out of any other notepad, and goes back to
    /// the quick-note window only once its own database is open again. In memory only.
    /// </summary>
    internal sealed class HeldDrafts
    {
        private readonly Dictionary<string, string> _held = new(StringComparer.Ordinal);

        internal void Hold(string database, string text)
        {
            if (text.Length == 0) _held.Remove(database);
            else _held[database] = text;
        }

        /// <summary>What was held for any of these names of one database, which is then no longer
        /// held. A name of null owns nothing.</summary>
        internal string Take(params string?[] databases)
        {
            string text = "";
            foreach (string? database in databases)
            {
                if (database == null || !_held.TryGetValue(database, out string? held)) continue;
                _held.Remove(database);
                text = Join(text, held);
            }
            return text;
        }

        /// <summary>The held words first, then whatever was typed since, so neither is lost.</summary>
        internal static string Join(string held, string current) =>
            held.Length == 0 ? current : current.Length == 0 ? held : held + Environment.NewLine + Environment.NewLine + current;
    }
}

// One-time rewrap of markdown notes stored as raw source (MarkdownBlob.cs explains why they are
// packages now). Runs every time a database is opened, and is a no-op on a database that has
// already been through it.
//
// This exists because the wrapper alone is not enough. Writing packages only on save heals a note
// the next time somebody edits it, which for a note nobody touches again is never - and one such
// note is all it takes to keep a database lethal to any build older than 1.3.0, which reads every
// blob as a XamlPackage and dies on the throw. Migrating at open makes the guarantee a whole
// database rather than a per-note accident: once 1.3.0 has opened it, it is safe to share.
//
// Nothing here may prevent a database from opening. A note that will not re-encode is left raw,
// and a failure of the whole pass is swallowed: the app still opens, and the note content is
// untouched either way, because the rewrap only ever writes back text it just read successfully.

using System;
using System.Collections.Generic;

namespace KillerNotes.Services
{
    internal static class MarkdownMigration
    {
        /// <summary>Rewraps every raw markdown blob in the open database. Returns the number of
        /// notes rewritten, which is 0 on a database that needs nothing.</summary>
        public static int RewrapRawMarkdown()
        {
            try
            {
                if (!NoteStore.IsOpen || NoteStore.IsReadOnly) return 0;

                var raw = NoteStore.ListRawMarkdownBlobs();
                if (raw.Count == 0) return 0;

                var updates = new List<(long Id, byte[] Content)>(raw.Count);
                foreach (var (id, content) in raw)
                {
                    // Decode reads the raw bytes as source; Encode puts that same source in a
                    // package. A note that throws on either half stays as it was rather than being
                    // written back as something lossy.
                    try { updates.Add((id, MarkdownBlob.Encode(MarkdownBlob.Decode(content)))); }
                    catch { /* leave this one raw */ }
                }

                NoteStore.RewriteContents(updates);
                return updates.Count;
            }
            catch
            {
                return 0;
            }
        }
    }
}

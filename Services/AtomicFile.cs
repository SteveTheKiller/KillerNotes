using System;
using System.IO;

namespace KillerNotes.Services
{
    /// <summary>
    /// Writes a file so that a failure partway leaves the previous file untouched. The writer
    /// fills a hidden sibling in the same folder (same volume, so the final step is a rename, not
    /// a copy); only once it has finished is the sibling swapped in for the destination. A writer
    /// that throws leaves the destination exactly as it was and its sibling deleted.
    /// </summary>
    internal static class AtomicFile
    {
        internal static void Write(string destination, Action<string> write)
        {
            string full = Path.GetFullPath(destination);
            string dir = Path.GetDirectoryName(full)!;
            string temp = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                write(temp);
                if (!File.Exists(temp)) throw new IOException("The export produced no file.");
                if (File.Exists(full)) File.Replace(temp, full, null, ignoreMetadataErrors: true);
                else File.Move(temp, full);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort; never masks the real error */ }
            }
        }
    }
}

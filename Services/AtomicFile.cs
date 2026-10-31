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
            // Not named after the destination: on a machine with the 260 character limit, a sibling
            // longer than the real name could fail where the real name fits.
            string temp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
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

        /// <summary>Copies a closed file to the destination the same way. The copier stands in
        /// for the disk in tests.</summary>
        internal static void Copy(string source, string destination, Action<string, string>? copy = null) =>
            Write(destination, staged => (copy ?? File.Copy)(source, staged));
    }
}

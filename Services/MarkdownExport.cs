using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace KillerNotes.Services
{
    /// <summary>
    /// Export note as Markdown: a text file and a sibling folder of pictures and recordings, which
    /// no single rename can swap together. So each file in the folder is named for its content and
    /// an export never writes over a name that holds anything else. The new files land beside the
    /// old ones, the text changes over last, and only then do the old files leave. A failure
    /// anywhere removes this attempt's own files and leaves the previous export byte for byte.
    ///
    /// A file that could not be written fails the export while there is a previous one to protect.
    /// With nothing at the destination the text is still worth having, and the caller says what
    /// was left out.
    /// </summary>
    internal static class MarkdownExport
    {
        /// <summary>Writes the export. Returns true when something embedded was left out. The
        /// last two arguments stand in for the disk in tests.</summary>
        internal static bool Write(FlowDocument doc, string path, Func<string, Attachment?>? attachments = null,
            Action<string, byte[]>? writeAsset = null, Action<string, string>? publish = null)
        {
            path = Path.GetFullPath(path);
            var assets = new MarkdownAssets(path, writeAsset ?? File.WriteAllBytes);
            int lost;
            IReadOnlyList<string> previous;
            try
            {
                string markdown;
                (markdown, lost) = MarkdownWriter.FromDocument(doc, path, attachments, assets);
                if (assets.Failure != null && File.Exists(path)) ExceptionDispatchInfo.Capture(assets.Failure).Throw();
                previous = assets.LinkedFrom(path);
                (publish ?? PublishText)(path, markdown);
            }
            catch
            {
                assets.Rollback();
                throw;
            }
            // The new text is in place, so nothing past here can undo it. With something left out
            // of it, the old file may be the only copy on this disk.
            if (lost == 0) assets.Retire(previous);
            return lost > 0;
        }

        private static void PublishText(string path, string markdown) =>
            AtomicFile.Write(path, staged => File.WriteAllText(staged, markdown, Encoding.UTF8));
    }

    /// <summary>One export attempt's files in the assets folder.</summary>
    internal sealed class MarkdownAssets
    {
        private readonly string _folder, _folderName;
        private readonly Action<string, byte[]> _write;
        private readonly List<string> _written = [];
        private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
        private bool _madeFolder;

        /// <summary>The first file that could not be written, if any.</summary>
        internal Exception? Failure { get; private set; }

        internal MarkdownAssets(string markdownPath, Action<string, byte[]> write)
        {
            _folderName = Path.GetFileNameWithoutExtension(markdownPath) + ".assets";
            _folder = Path.Combine(Path.GetDirectoryName(markdownPath)!, _folderName);
            _write = write;
        }

        internal void Fail(Exception error) => Failure ??= error;

        /// <summary>Stores the bytes and returns their file name. The same bytes keep the name they
        /// already have; a name that holds anything else is left alone for the next one.</summary>
        internal string Add(string prefix, string extension, byte[] bytes)
        {
            string stem = prefix + "-" + Attachments.HashOf(bytes).Substring(0, 8);
            string name = stem + extension;
            for (int n = 2; ; n++)
            {
                string path = Path.Combine(_folder, name);
                if (!File.Exists(path))
                {
                    if (!Directory.Exists(_folder))
                    {
                        Directory.CreateDirectory(_folder);
                        _madeFolder = true;
                    }
                    AtomicFile.Write(path, staged => _write(staged, bytes));
                    _written.Add(path);
                    break;
                }
                if (File.ReadAllBytes(path).SequenceEqual(bytes)) break;
                name = $"{stem}-{n}{extension}";
            }
            _used.Add(name);
            return name;
        }

        /// <summary>The files in the assets folder a Markdown file links to.</summary>
        internal IReadOnlyList<string> LinkedFrom(string markdownPath)
        {
            try
            {
                string text = File.ReadAllText(markdownPath);
                return [.. Regex.Matches(text, @"\(<?" + Regex.Escape(_folderName) + @"/([^)>/\\\r\n]+)>?\)")
                    .Cast<Match>().Select(link => link.Groups[1].Value)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        }

        /// <summary>The new text is in place: removes what the old text linked to and this one does
        /// not. Only names exports make, so a file the user linked by hand stays. A file that
        /// will not delete, or a hand-edited link that names no file, is only clutter by now.</summary>
        internal void Retire(IEnumerable<string> previous)
        {
            foreach (string name in previous.Where(n => !_used.Contains(n) && Regex.IsMatch(n, "^(img|recording)-")))
                try { File.Delete(Path.Combine(_folder, name)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
            RemoveFolderIfEmpty();
        }

        internal void Rollback()
        {
            foreach (string path in _written)
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (_madeFolder) RemoveFolderIfEmpty();
        }

        private void RemoveFolderIfEmpty()
        {
            try
            {
                if (Directory.Exists(_folder) && !Directory.EnumerateFileSystemEntries(_folder).Any())
                    Directory.Delete(_folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

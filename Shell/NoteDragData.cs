using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace KillerNotes.Shell
{
    /// <summary>
    /// The payload of a note dragged out of the sidebar. The same drag serves in-list reorder,
    /// which only needs the note id, and dropping the note on Explorer, a mail client or another
    /// app, which needs a .knote file. The file is unencrypted (an unlocked notepad's notes are
    /// readable, and a drag is a deliberate share), so it is written only when a target actually
    /// asks for the file list: reorders, hovering over our own controls and canceled drags write
    /// nothing.
    ///
    /// Each written file sits in its own folder under TEMP\KillerNotes\drag so same-titled notes
    /// never collide and the receiver still sees a readable name. A target may copy the file after
    /// DoDragDrop returns, so it is not deleted then; the next start removes leftover drag
    /// folders (RemoveLeftovers).
    /// </summary>
    internal sealed class NoteDragData : IDataObject
    {
        private const string DragFolder = "drag";
        private readonly long _noteId;
        private readonly string _title;
        private readonly string _tempRoot;
        private readonly Action<string> _export;
        private string? _path;

        /// <param name="tempRoot">TEMP\KillerNotes in the app; a scratch folder in tests.</param>
        /// <param name="export">Writes the note's .knote to the given path.</param>
        internal NoteDragData(long noteId, string title, string tempRoot, Action<string> export)
        {
            _noteId = noteId;
            _title = title;
            _tempRoot = tempRoot;
            _export = export;
        }

        internal static string DefaultTempRoot => Path.Combine(Path.GetTempPath(), "KillerNotes");

        /// <summary>Whether a target has asked for the file, i.e. the note left the app.</summary>
        internal bool FileWritten => _path != null;

        private string[] FileList()
        {
            if (_path == null)
            {
                string dir = Path.Combine(_tempRoot, DragFolder, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, MainWindow.SafeFileName(_title) + ".knote");
                _export(path);
                _path = path;
            }
            return new[] { _path };
        }

        /// <summary>Deletes drag folders left by earlier sessions, and .knote files that versions
        /// before 1.6.0 wrote straight into TEMP\KillerNotes. Everything else there (recordings
        /// copied to the clipboard, other features' files) is left alone.</summary>
        internal static void RemoveLeftovers(string tempRoot)
        {
            try
            {
                string drag = Path.Combine(tempRoot, DragFolder);
                if (Directory.Exists(drag))
                    foreach (string dir in Directory.GetDirectories(drag))
                        try { Directory.Delete(dir, true); } catch { /* still open in a receiver; next start */ }
                if (Directory.Exists(tempRoot))
                    foreach (string file in Directory.GetFiles(tempRoot, "*.knote"))
                        try { File.Delete(file); } catch { }
            }
            catch { /* cleanup must never block startup */ }
        }

        private static bool IsFileDrop(string format) =>
            string.Equals(format, DataFormats.FileDrop, StringComparison.Ordinal);

        private static bool IsNoteId(string format) =>
            string.Equals(format, MainWindow.NoteIdFormat, StringComparison.Ordinal);

        public object? GetData(string format) =>
            IsFileDrop(format) ? FileList() : IsNoteId(format) ? _noteId : null;

        public object? GetData(string format, bool autoConvert) => GetData(format);

        public object? GetData(Type format) =>
            format == typeof(string[]) ? GetData(DataFormats.FileDrop) : null;

        public bool GetDataPresent(string format) => IsFileDrop(format) || IsNoteId(format);

        public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);

        public bool GetDataPresent(Type format) => format == typeof(string[]);

        public string[] GetFormats() => new[] { DataFormats.FileDrop, MainWindow.NoteIdFormat };

        public string[] GetFormats(bool autoConvert) => GetFormats();

        public void SetData(object data) => throw new NotSupportedException();
        public void SetData(string format, object data) => throw new NotSupportedException();
        public void SetData(string format, object data, bool autoConvert) => throw new NotSupportedException();
        public void SetData(Type format, object data) => throw new NotSupportedException();
    }
}

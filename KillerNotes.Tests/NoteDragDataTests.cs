using System;
using System.IO;
using System.Windows;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Dragging a note used to export an unencrypted .knote the moment the drag began, so an
    /// in-list reorder or a canceled drag still left a readable copy in TEMP. The drag payload now
    /// writes its file only when a target actually asks for the file list, each drag gets its own
    /// folder, and startup removes leftover drag folders without touching anything else.
    /// </summary>
    public sealed class NoteDragDataTests : IDisposable
    {
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "kn-dragtest-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_temp, true); } catch { }
        }

        [Fact]
        public void ReorderAndCanceledDragsNeverWriteAFile()
        {
            int exports = 0;
            var data = new NoteDragData(42, "Secret", _temp, path => { exports++; File.WriteAllText(path, "x"); });

            // What an in-list reorder, a hover over our own controls, and a canceled drag do.
            Assert.True(data.GetDataPresent(DataFormats.FileDrop));
            Assert.True(data.GetDataPresent(MainWindow.NoteIdFormat));
            Assert.Equal(42L, data.GetData(MainWindow.NoteIdFormat));
            Assert.Contains(DataFormats.FileDrop, data.GetFormats());

            Assert.Equal(0, exports);
            Assert.False(Directory.Exists(_temp) && Directory.GetFiles(_temp, "*", SearchOption.AllDirectories).Length > 0);
        }

        [Fact]
        public void AnExternalDropGetsOneFileInItsOwnFolder()
        {
            int exports = 0;
            var first = new NoteDragData(1, "Same: name?", _temp, path => { exports++; File.WriteAllText(path, "a"); });
            var second = new NoteDragData(2, "Same: name?", _temp, path => File.WriteAllText(path, "b"));

            var a = Assert.Single((string[])first.GetData(DataFormats.FileDrop)!);
            var again = Assert.Single((string[])first.GetData(DataFormats.FileDrop)!);
            var b = Assert.Single((string[])second.GetData(DataFormats.FileDrop)!);

            Assert.Equal(1, exports);
            Assert.Equal(a, again);
            Assert.NotEqual(a, b);
            Assert.Equal(".knote", Path.GetExtension(a));
            Assert.Equal("a", File.ReadAllText(a));
            Assert.Equal("b", File.ReadAllText(b));
        }

        [Fact]
        public void StartupRemovesOnlyLeftoverDragFiles()
        {
            var drag = new NoteDragData(1, "Old", _temp, path => File.WriteAllText(path, "x"));
            string dragged = ((string[])drag.GetData(DataFormats.FileDrop)!)[0];
            string legacy = Path.Combine(_temp, "Older version.knote");
            string recording = Path.Combine(_temp, "share", "Recording 1.wav");
            string unrelated = Path.Combine(_temp, "notes.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(recording)!);
            File.WriteAllText(legacy, "x");
            File.WriteAllText(recording, "x");
            File.WriteAllText(unrelated, "x");

            NoteDragData.RemoveLeftovers(_temp);

            Assert.False(File.Exists(dragged));
            Assert.False(File.Exists(legacy));
            Assert.True(File.Exists(recording));
            Assert.True(File.Exists(unrelated));
        }
    }
}

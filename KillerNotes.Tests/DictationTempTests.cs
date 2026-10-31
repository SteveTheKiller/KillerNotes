using System;
using System.IO;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The Windows speech engine needs the take as a file, so for the length of a transcription
    /// the audio of a locked notepad sits on disk unencrypted. It lives in the app's own temp
    /// folder, where a start or a lock can find and clear it.
    /// </summary>
    public sealed class DictationTempTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void AWorkingCopyIsKeptInTheAppsOwnTempFolder()
        {
            string path = DictationRecorder.NewTempWavPath(_root);

            Assert.Equal(Path.Combine(_root, "dictation"), Path.GetDirectoryName(path));
            Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
            Assert.StartsWith("kn-dict-", Path.GetFileName(path));
            Assert.EndsWith(".wav", path);
        }

        [Fact]
        public void SweepingClearsLeftoverWorkingCopiesAndNothingElse()
        {
            string legacy = Path.Combine(_root, "loose");
            Directory.CreateDirectory(legacy);
            string current = Path.GetDirectoryName(DictationRecorder.NewTempWavPath(_root))!;
            string oldCopy = Path.Combine(legacy, "kn-dict-0123.wav");
            string newCopy = Path.Combine(current, "kn-dict-4567.wav");
            string other = Path.Combine(legacy, "recording.wav");
            string shared = Path.Combine(_root, "share", "kn-dict-keep.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
            foreach (string f in new[] { oldCopy, newCopy, other, shared }) File.WriteAllBytes(f, new byte[] { 1 });

            DictationRecorder.SweepTempRecordings(_root, legacy);

            Assert.False(File.Exists(oldCopy));
            Assert.False(File.Exists(newCopy));
            Assert.True(File.Exists(other));
            Assert.True(File.Exists(shared));
        }

        [Fact]
        public void SweepingWithNothingThereDoesNotThrow() =>
            DictationRecorder.SweepTempRecordings(Path.Combine(_root, "missing"), Path.Combine(_root, "also-missing"));
    }
}

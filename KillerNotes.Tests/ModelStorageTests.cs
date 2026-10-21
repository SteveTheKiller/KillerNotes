using System;
using System.IO;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Speech models used to live inside the per-version native folder, so every app update
    /// hid a 75 to 466 MB download. They now live in one version-independent folder, and models an
    /// earlier version downloaded are moved there instead of downloaded again.
    /// </summary>
    public sealed class ModelStorageTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "kn-models-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string Native(string version) => Path.Combine(_root, "native", version, "models");
        private string Models => Path.Combine(_root, "models");

        [Fact]
        public void AModelFromAnEarlierVersionIsReused()
        {
            Directory.CreateDirectory(Native("1.5.1.0"));
            File.WriteAllBytes(Path.Combine(Native("1.5.1.0"), "ggml-base.en.bin"), new byte[] { 1, 2, 3 });

            WhisperSpeech.AdoptEarlierModels(Models, Path.Combine(_root, "native"));

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Models, "ggml-base.en.bin")));
            Assert.False(File.Exists(Path.Combine(Native("1.5.1.0"), "ggml-base.en.bin")));   // moved, not duplicated
        }

        [Fact]
        public void AModelAlreadyInPlaceIsNeverOverwritten()
        {
            Directory.CreateDirectory(Models);
            File.WriteAllBytes(Path.Combine(Models, "ggml-base.en.bin"), new byte[] { 9 });
            Directory.CreateDirectory(Native("1.5.1.0"));
            File.WriteAllBytes(Path.Combine(Native("1.5.1.0"), "ggml-base.en.bin"), new byte[] { 1, 2, 3 });

            WhisperSpeech.AdoptEarlierModels(Models, Path.Combine(_root, "native"));

            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(Models, "ggml-base.en.bin")));
            Assert.True(File.Exists(Path.Combine(Native("1.5.1.0"), "ggml-base.en.bin")));   // left for the user, not deleted
        }

        [Fact]
        public void PartialDownloadsAreNotAdopted()
        {
            Directory.CreateDirectory(Native("1.5.1.0"));
            File.WriteAllBytes(Path.Combine(Native("1.5.1.0"), "ggml-base.en.bin.part"), new byte[] { 1 });

            WhisperSpeech.AdoptEarlierModels(Models, Path.Combine(_root, "native"));

            Assert.False(File.Exists(Path.Combine(Models, "ggml-base.en.bin.part")));
        }
    }
}

using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [CollectionDefinition("Native audio", DisableParallelization = true)]
    public sealed class NativeAudioCollection { }

    [Collection("Native audio")]
    public sealed class WhisperInteropTests : IClassFixture<WhisperNativeFixture>
    {
        [Fact]
        public void EmbeddedNativeMatchesTheReviewedAbiVersion() =>
            Assert.Equal(WhisperSpeech.SupportedNativeVersion, WhisperSpeech.NativeVersion);

        [Fact]
        public void MissingModelReturnsFailureWithoutAMarshalingException()
        {
            string absent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "native-audio-tests", Guid.NewGuid().ToString("N") + ".bin");
            Assert.Equal(IntPtr.Zero, WhisperSpeech.CreateContext(absent));
        }

        [Fact]
        public void QuietParametersPreserveNativeThreadAndContextDefaults()
        {
            IntPtr defaults = Marshal.AllocHGlobal(4096);
            IntPtr actual = IntPtr.Zero;
            try
            {
                DefaultParameters(defaults, 0);
                actual = WhisperSpeech.CreateParameters();
                Assert.Equal(Marshal.ReadInt32(defaults, 4), Marshal.ReadInt32(actual, 4));
                Assert.Equal(Marshal.ReadInt32(defaults, 8), Marshal.ReadInt32(actual, 8));
                for (int offset = 24; offset <= 27; offset++)
                    Assert.Equal((byte)0, Marshal.ReadByte(actual, offset));
            }
            finally
            {
                Marshal.FreeHGlobal(defaults);
                if (actual != IntPtr.Zero) Marshal.FreeHGlobal(actual);
            }
        }

        [DllImport("libwhisper.dll", EntryPoint = "whisper_full_default_params", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DefaultParameters(IntPtr blob, int strategy);
    }

    public sealed class WhisperNativeFixture
    {
        public WhisperNativeFixture()
        {
            // The engine is downloaded by the app, not embedded; the tests use the repository's
            // copies, which are the files the download serves (third_party/audio).
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo != null && !File.Exists(Path.Combine(repo.FullName, "KillerNotes.csproj"))) repo = repo.Parent;
            string source = Path.Combine(repo?.FullName ?? throw new InvalidOperationException("Repository not found"), "third_party", "audio");
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-audio-tests");
            Directory.CreateDirectory(folder);
            foreach (var (name, sha, _) in SpeechEngine.Core)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(source, name));
                Assert.True(SpeechEngine.HashMatches(Path.Combine(source, name), sha), name + " no longer matches its pinned hash");
                string path = Path.Combine(folder, name);
                if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
                    File.WriteAllBytes(path, bytes);
                if (LoadLibrary(path) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);
    }
}

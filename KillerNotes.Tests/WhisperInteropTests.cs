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
            var assembly = typeof(WhisperSpeech).Assembly;
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-audio-tests");
            Directory.CreateDirectory(folder);
            foreach (string name in new[] { "ggml-base.dll", "ggml.dll", "libwhisper.dll" })
            {
                using var resource = assembly.GetManifestResourceStream("KillerNotes.AudioNative." + name)
                    ?? throw new InvalidOperationException("Embedded native library missing: " + name);
                using var data = new MemoryStream();
                resource.CopyTo(data);
                byte[] bytes = data.ToArray();
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

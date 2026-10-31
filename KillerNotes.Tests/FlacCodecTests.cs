using System;
using System.IO;
using System.Runtime.InteropServices;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// FLAC files come in from folder imports at any bit depth, and the decoder hands the app
    /// 16-bit PCM. The streams here are written with libFLAC itself, at the depth under test.
    /// </summary>
    public sealed class FlacCodecTests
    {
        [Theory]
        [InlineData(24, 8388607, 32767)]
        [InlineData(24, -8388608, -32768)]
        [InlineData(24, 4194304, 16384)]    // half scale stays half scale
        [InlineData(16, 1234, 1234)]
        [InlineData(8, 100, 25600)]         // quiet 8-bit audio is lifted to its 16-bit level
        [InlineData(8, -128, -32768)]
        public void SamplesKeepTheirLevelWhateverTheStreamsBitDepth(int bits, int sample, int expected)
        {
            Assert.True(FlacCodec.Available, "libFLAC must load for this test");
            byte[] flac = Encode(bits, sample, 4096);

            byte[]? wav = FlacCodec.ToWav(flac);

            Assert.NotNull(wav);
            Assert.Equal(16, BitConverter.ToInt16(wav!, 34));
            Assert.Equal(4096 * 2, wav!.Length - WavEdit.HeaderBytes);
            for (int i = 0; i < 4096; i++)
                Assert.Equal(expected, BitConverter.ToInt16(wav, WavEdit.HeaderBytes + i * 2));
        }

        /// <summary>A mono 16 kHz FLAC stream of one repeated sample at the given depth.</summary>
        private static byte[] Encode(int bits, int sample, int count)
        {
            var output = new MemoryStream();
            EncodeWrite write = (_, buffer, bytes, _, _, _) =>
            {
                int n = (int)bytes;
                var chunk = new byte[n];
                Marshal.Copy(buffer, chunk, 0, n);
                output.Write(chunk, 0, n);
                return 0;
            };
            IntPtr enc = FLAC__stream_encoder_new();
            try
            {
                FLAC__stream_encoder_set_channels(enc, 1);
                FLAC__stream_encoder_set_bits_per_sample(enc, (uint)bits);
                FLAC__stream_encoder_set_sample_rate(enc, 16000);
                Assert.Equal(0, FLAC__stream_encoder_init_stream(enc, write, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
                var samples = new int[count];
                for (int i = 0; i < count; i++) samples[i] = sample;
                Assert.NotEqual(0, FLAC__stream_encoder_process_interleaved(enc, samples, (uint)count));
                FLAC__stream_encoder_finish(enc);
                GC.KeepAlive(write);
            }
            finally { FLAC__stream_encoder_delete(enc); }
            return output.ToArray();
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EncodeWrite(IntPtr enc, IntPtr buffer, IntPtr bytes, uint samples, uint current, IntPtr client);

        private const string Flac = "libFLAC.dll";
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FLAC__stream_encoder_new();
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern void FLAC__stream_encoder_delete(IntPtr e);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_set_channels(IntPtr e, uint v);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_set_bits_per_sample(IntPtr e, uint v);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_set_sample_rate(IntPtr e, uint v);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)]
        private static extern int FLAC__stream_encoder_init_stream(IntPtr e, EncodeWrite write, IntPtr seek, IntPtr tell, IntPtr meta, IntPtr client);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)]
        private static extern int FLAC__stream_encoder_process_interleaved(IntPtr e, int[] buffer, uint samples);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_finish(IntPtr e);
    }
}

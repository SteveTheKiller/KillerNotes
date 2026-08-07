using System;
using System.Runtime.InteropServices;

namespace KillerNotes.Services
{
    /// <summary>
    /// MP3 encoding over libmp3lame. EXPORT ONLY - nothing in the app stores MP3.
    ///
    /// That restriction is deliberate and worth keeping: MP3 is lossy, so storing it would mean
    /// every slice, delete or paste costs a decode and a re-encode, and the generation loss
    /// compounds each time. FLAC is lossless, so the same edit cycle is bit-identical forever.
    /// MP3 earns its place only on the way OUT, where the copy is one-way and universal
    /// playability is the whole point.
    ///
    /// There is no decoder here for the same reason - nothing this app writes needs reading back.
    /// </summary>
    internal static class LameCodec
    {
        internal static bool Available
        {
            get { AudioNativeBootstrap.EnsureLoaded(); return AudioNativeBootstrap.LameAvailable; }
        }

        internal static string? LastError { get; private set; }

        /// <summary>
        /// Constant bitrate, in kbps. 64 is generous for 16kHz mono speech - the source is already
        /// band-limited well below what a higher rate could carry, so anything more would grow the
        /// file without adding anything audible.
        /// </summary>
        private const int BitrateKbps = 64;

        private const string Lame = "libmp3lame.dll";

        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr lame_init();
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)] private static extern int lame_close(IntPtr gfp);
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)] private static extern int lame_set_in_samplerate(IntPtr gfp, int v);
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)] private static extern int lame_set_num_channels(IntPtr gfp, int v);
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)] private static extern int lame_set_mode(IntPtr gfp, int v);
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)] private static extern int lame_set_brate(IntPtr gfp, int v);
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)] private static extern int lame_set_quality(IntPtr gfp, int v);
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)] private static extern int lame_init_params(IntPtr gfp);
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)]
        private static extern int lame_encode_buffer(IntPtr gfp, short[] left, short[] right, int samples, byte[] mp3, int mp3Size);
        [DllImport(Lame, CallingConvention = CallingConvention.Cdecl)]
        private static extern int lame_encode_flush(IntPtr gfp, byte[] mp3, int mp3Size);

        private const int ModeMono = 3;       // MPEG_mode.MONO
        private const int ModeJointStereo = 1;

        /// <summary>Encodes a 16-bit PCM WAV to MP3, or null if it cannot.</summary>
        internal static byte[]? FromWav(byte[] wav)
        {
            LastError = null;
            if (!Available) { LastError = "MP3 support is not installed."; return null; }
            if (wav == null || wav.Length <= WavEdit.HeaderBytes) return null;

            int rate = BitConverter.ToInt32(wav, 24);
            short channels = BitConverter.ToInt16(wav, 22);
            short bits = BitConverter.ToInt16(wav, 34);
            if (bits != 16 || channels < 1 || channels > 2 || rate <= 0)
            { LastError = "Only 16-bit mono or stereo PCM is encoded."; return null; }

            int totalSamples = WavEdit.DataLength(wav) / 2;
            int frames = totalSamples / channels;
            if (frames <= 0) return null;

            // Split into per-channel arrays: lame_encode_buffer takes planar, not interleaved.
            // Mono hands the SAME array as both left and right, which is what LAME expects.
            var left = new short[frames];
            var right = channels == 2 ? new short[frames] : left;
            for (int i = 0; i < frames; i++)
            {
                int o = WavEdit.HeaderBytes + i * channels * 2;
                left[i] = (short)(wav[o] | (wav[o + 1] << 8));
                if (channels == 2) right[i] = (short)(wav[o + 2] | (wav[o + 3] << 8));
            }

            IntPtr gfp = IntPtr.Zero;
            try
            {
                gfp = lame_init();
                if (gfp == IntPtr.Zero) { LastError = "MP3 encoder could not start."; return null; }

                lame_set_in_samplerate(gfp, rate);
                lame_set_num_channels(gfp, channels);
                lame_set_mode(gfp, channels == 1 ? ModeMono : ModeJointStereo);
                lame_set_brate(gfp, BitrateKbps);
                lame_set_quality(gfp, 2);      // 0 best / 9 worst; 2 is the usual "high" setting
                if (lame_init_params(gfp) < 0)
                { LastError = "MP3 encoder rejected those settings."; return null; }

                // LAME's own documented worst case for the output buffer. Undersizing it is the
                // classic way to get truncated audio with no error reported.
                const int Block = 8192;
                var mp3 = new byte[(int)(1.25 * Block) + 7200];
                using var outp = new System.IO.MemoryStream(frames / 2);

                var lb = new short[Block];
                var rb = channels == 2 ? new short[Block] : null;
                int done = 0;
                while (done < frames)
                {
                    int n = Math.Min(Block, frames - done);
                    Array.Copy(left, done, lb, 0, n);
                    if (rb != null) Array.Copy(right, done, rb, 0, n);

                    int wrote = lame_encode_buffer(gfp, lb, rb ?? lb, n, mp3, mp3.Length);
                    if (wrote < 0) { LastError = "MP3 encoding failed."; return null; }
                    if (wrote > 0) outp.Write(mp3, 0, wrote);
                    done += n;
                }

                // Flush is NOT optional: the encoder holds a partial frame back, and skipping this
                // silently clips the end of every recording.
                int tail = lame_encode_flush(gfp, mp3, mp3.Length);
                if (tail > 0) outp.Write(mp3, 0, tail);

                return outp.ToArray();
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
            finally { if (gfp != IntPtr.Zero) lame_close(gfp); }
        }
    }
}

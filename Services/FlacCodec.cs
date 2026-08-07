using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KillerNotes.Services
{
    /// <summary>
    /// FLAC encode and decode over libFLAC's C API. Lossless, roughly half the size of the WAV, and
    /// BSD-licensed - which is why it is the STORAGE format for recordings while MP3 is only ever
    /// an export: re-encoding a lossy format on every edit compounds generation loss, whereas a
    /// FLAC decode/edit/re-encode cycle is bit-identical every time.
    ///
    /// Everything here converts to and from plain 16-bit PCM WAV, so the rest of the app - WavEdit,
    /// the envelope, the waveform, waveOut - carries on working in PCM and needs no idea a codec
    /// exists. waveOut cannot play FLAC anyway, so a decode step was always going to be required.
    ///
    /// Two traps in this API, both silent:
    ///   - process_interleaved takes INT32 samples even for 16-bit audio. Handing it a short[]
    ///     produces noise rather than an error.
    ///   - The stream callbacks are function pointers. A delegate that is not held in a field gets
    ///     collected and the encoder calls into freed memory - the same trap as WaveInProc in
    ///     DictationRecorder.
    /// </summary>
    internal static class FlacCodec
    {
        internal static bool Available
        {
            get { AudioNativeBootstrap.EnsureLoaded(); return AudioNativeBootstrap.FlacAvailable; }
        }

        internal static string? LastError { get; private set; }

        // ---- libFLAC interop ----

        private const string Flac = "libFLAC.dll";

        // Encoder
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FLAC__stream_encoder_new();
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern void FLAC__stream_encoder_delete(IntPtr e);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_set_channels(IntPtr e, uint v);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_set_bits_per_sample(IntPtr e, uint v);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_set_sample_rate(IntPtr e, uint v);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_set_compression_level(IntPtr e, uint v);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_set_total_samples_estimate(IntPtr e, ulong v);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)]
        private static extern int FLAC__stream_encoder_init_stream(IntPtr e, EncWrite write, IntPtr seek, IntPtr tell, IntPtr meta, IntPtr client);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)]
        private static extern int FLAC__stream_encoder_process_interleaved(IntPtr e, int[] buffer, uint samples);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_encoder_finish(IntPtr e);

        // Decoder
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FLAC__stream_decoder_new();
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern void FLAC__stream_decoder_delete(IntPtr d);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)]
        private static extern int FLAC__stream_decoder_init_stream(IntPtr d, DecRead read, IntPtr seek, IntPtr tell,
            IntPtr length, IntPtr eof, DecWrite write, IntPtr meta, DecError error, IntPtr client);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_decoder_process_until_end_of_stream(IntPtr d);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] private static extern int FLAC__stream_decoder_finish(IntPtr d);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EncWrite(IntPtr enc, IntPtr buffer, IntPtr bytes, uint samples, uint current, IntPtr client);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DecRead(IntPtr dec, IntPtr buffer, ref IntPtr bytes, IntPtr client);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DecWrite(IntPtr dec, IntPtr frame, IntPtr buffers, IntPtr client);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DecError(IntPtr dec, int status, IntPtr client);

        /// <summary>Compression level. 5 is libFLAC's own default and the knee of the curve - 8 costs
        /// several times the CPU for around 1% more on speech.</summary>
        private const uint CompressionLevel = 5;

        // ---- encode ----

        /// <summary>Encodes a 16-bit PCM WAV to FLAC, or null if it cannot (no native, odd format).
        /// The caller stores the WAV unchanged when this returns null.</summary>
        internal static byte[]? FromWav(byte[] wav)
        {
            LastError = null;
            if (!Available) { LastError = "FLAC support is not installed."; return null; }
            if (wav == null || wav.Length <= WavEdit.HeaderBytes) return null;

            int rate = BitConverter.ToInt32(wav, 24);
            short channels = BitConverter.ToInt16(wav, 22);
            short bits = BitConverter.ToInt16(wav, 34);
            if (bits != 16 || channels < 1 || rate <= 0) { LastError = "Only 16-bit PCM is encoded."; return null; }

            int dataBytes = WavEdit.DataLength(wav);
            int totalSamples = dataBytes / 2;                 // 16-bit samples, all channels
            int frames = totalSamples / channels;
            if (frames <= 0) return null;

            // Widened to int32 because that is what process_interleaved takes, whatever the bit
            // depth. This is the step that silently produces noise if skipped.
            var samples = new int[totalSamples];
            for (int i = 0; i < totalSamples; i++)
                samples[i] = (short)(wav[WavEdit.HeaderBytes + i * 2] | (wav[WavEdit.HeaderBytes + i * 2 + 1] << 8));

            var outp = new MemoryStream(dataBytes / 2);
            IntPtr enc = IntPtr.Zero;
            // FIELD-equivalent: a local that stays referenced for the whole call. The encoder holds
            // this pointer, so it must outlive every call into libFLAC below.
            EncWrite write = (_, buffer, bytes, _, _, _) =>
            {
                int n = (int)bytes;
                if (n > 0)
                {
                    var chunk = new byte[n];
                    Marshal.Copy(buffer, chunk, 0, n);
                    outp.Write(chunk, 0, n);
                }
                return 0;   // FLAC__STREAM_ENCODER_WRITE_STATUS_OK
            };

            try
            {
                enc = FLAC__stream_encoder_new();
                if (enc == IntPtr.Zero) { LastError = "FLAC encoder could not start."; return null; }

                FLAC__stream_encoder_set_channels(enc, (uint)channels);
                FLAC__stream_encoder_set_bits_per_sample(enc, 16);
                FLAC__stream_encoder_set_sample_rate(enc, (uint)rate);
                FLAC__stream_encoder_set_compression_level(enc, CompressionLevel);
                FLAC__stream_encoder_set_total_samples_estimate(enc, (ulong)frames);

                if (FLAC__stream_encoder_init_stream(enc, write, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0)
                { LastError = "FLAC encoder could not be initialised."; return null; }

                // In blocks rather than one call: a long take would otherwise hand libFLAC a
                // multi-megabyte array in one go for no benefit.
                const int Block = 4096;
                int done = 0;
                var slice = new int[Block * channels];
                while (done < frames)
                {
                    int n = Math.Min(Block, frames - done);
                    Array.Copy(samples, done * channels, slice, 0, n * channels);
                    if (FLAC__stream_encoder_process_interleaved(enc, slice, (uint)n) == 0)
                    { LastError = "FLAC encoding failed."; return null; }
                    done += n;
                }

                FLAC__stream_encoder_finish(enc);
                GC.KeepAlive(write);
                return outp.ToArray();
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
            finally { if (enc != IntPtr.Zero) FLAC__stream_encoder_delete(enc); }
        }

        // ---- decode ----

        /// <summary>Decodes FLAC back to a 16-bit PCM WAV, or null if it cannot.</summary>
        internal static byte[]? ToWav(byte[] flac)
        {
            LastError = null;
            if (!Available) { LastError = "FLAC support is not installed."; return null; }
            if (flac == null || flac.Length < 8) return null;

            IntPtr dec = IntPtr.Zero;
            var pcm = new MemoryStream(flac.Length * 2);
            int rate = 0, channels = 1;
            int read = 0;

            // Every parameter spelled out, including the unused ones: a lambda with a `ref`
            // parameter cannot mix explicit and discarded types (CS0748).
            DecRead onRead = (IntPtr _d, IntPtr buffer, ref IntPtr bytes, IntPtr _c) =>
            {
                int want = (int)bytes;
                int left = flac.Length - read;
                if (left <= 0) { bytes = IntPtr.Zero; return 1; }   // END_OF_STREAM
                int n = Math.Min(want, left);
                Marshal.Copy(flac, read, buffer, n);
                read += n;
                bytes = (IntPtr)n;
                return 0;   // CONTINUE
            };

            DecWrite onWrite = (_, frame, buffers, _) =>
            {
                // FLAC__Frame: header first, and the header begins blocksize, sample_rate,
                // channels... each a 32-bit field. Read what is needed rather than marshalling the
                // whole struct, which carries a union this code never looks at.
                int blockSize = Marshal.ReadInt32(frame, 0);
                rate = Marshal.ReadInt32(frame, 4);
                channels = Marshal.ReadInt32(frame, 8);
                if (blockSize <= 0 || channels <= 0) return 0;

                // buffers is a const int32* const* - one planar array per channel, which has to be
                // interleaved back for a WAV.
                var planes = new IntPtr[channels];
                Marshal.Copy(buffers, planes, 0, channels);

                var outBytes = new byte[blockSize * channels * 2];
                for (int c = 0; c < channels; c++)
                {
                    for (int i = 0; i < blockSize; i++)
                    {
                        int v = Marshal.ReadInt32(planes[c], i * 4);
                        if (v > short.MaxValue) v = short.MaxValue;
                        else if (v < short.MinValue) v = short.MinValue;
                        int o = (i * channels + c) * 2;
                        outBytes[o] = (byte)(v & 0xFF);
                        outBytes[o + 1] = (byte)((v >> 8) & 0xFF);
                    }
                }
                pcm.Write(outBytes, 0, outBytes.Length);
                return 0;   // CONTINUE
            };

            DecError onError = (_, _, _) => { };

            try
            {
                dec = FLAC__stream_decoder_new();
                if (dec == IntPtr.Zero) { LastError = "FLAC decoder could not start."; return null; }

                if (FLAC__stream_decoder_init_stream(dec, onRead, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, onWrite, IntPtr.Zero, onError, IntPtr.Zero) != 0)
                { LastError = "FLAC decoder could not be initialised."; return null; }

                FLAC__stream_decoder_process_until_end_of_stream(dec);
                FLAC__stream_decoder_finish(dec);
                GC.KeepAlive(onRead); GC.KeepAlive(onWrite); GC.KeepAlive(onError);

                if (rate <= 0 || pcm.Length == 0) { LastError = "That recording could not be decoded."; return null; }
                return BuildWav(pcm.ToArray(), rate, channels);
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
            finally { if (dec != IntPtr.Zero) FLAC__stream_decoder_delete(dec); }
        }

        /// <summary>Canonical 44-byte 16-bit PCM header around decoded samples.</summary>
        private static byte[] BuildWav(byte[] pcm, int rate, int channels)
        {
            int align = channels * 2;
            using var ms = new MemoryStream(44 + pcm.Length);
            using var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + pcm.Length);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);              // PCM
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * align);
            w.Write((short)align);
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(pcm.Length);
            w.Write(pcm);
            w.Flush();
            return ms.ToArray();
        }
    }
}

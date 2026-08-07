using System;

namespace KillerNotes.Services
{
    /// <summary>
    /// Cut, copy and paste on a WAV held in memory. Everything here works on the raw PCM data chunk
    /// and rewrites the 44-byte canonical header, which is safe because the only WAVs that reach it
    /// are the ones DictationRecorder produced (16-bit mono PCM).
    ///
    /// Byte offsets are always snapped to a whole SAMPLE FRAME. A cut landing mid-frame shifts every
    /// following byte by one and turns the rest of the recording into static - the same trap the
    /// player's seek has.
    ///
    /// This is deliberately non-destructive at the call site: every operation returns a NEW array
    /// and never touches the input, so undo is just keeping the old reference.
    /// </summary>
    internal static class WavEdit
    {
        internal const int HeaderBytes = 44;

        /// <summary>Bytes per second of audio, read from the header rather than assumed, so a file
        /// recorded at another rate still cuts at the right place.</summary>
        internal static int BytesPerSec(byte[] wav)
        {
            if (wav == null || wav.Length < HeaderBytes) return 0;
            int rate = BitConverter.ToInt32(wav, 24);       // nSamplesPerSec
            short align = BitConverter.ToInt16(wav, 32);    // nBlockAlign
            if (align <= 0) align = 2;
            return rate > 0 ? rate * align : 0;
        }

        internal static short BlockAlign(byte[] wav)
        {
            if (wav == null || wav.Length < HeaderBytes) return 2;
            short a = BitConverter.ToInt16(wav, 32);
            return a > 0 ? a : (short)2;
        }

        internal static int DataLength(byte[] wav) => wav == null ? 0 : Math.Max(0, wav.Length - HeaderBytes);

        internal static int DurationMs(byte[] wav)
        {
            int bps = BytesPerSec(wav);
            return bps <= 0 ? 0 : (int)(DataLength(wav) * 1000L / bps);
        }

        /// <summary>Milliseconds to a frame-aligned byte offset inside the data chunk.</summary>
        internal static int OffsetOf(byte[] wav, int ms)
        {
            int bps = BytesPerSec(wav);
            if (bps <= 0) return 0;
            int off = (int)(Math.Max(0, ms) / 1000.0 * bps);
            int align = BlockAlign(wav);
            off -= off % align;
            return Math.Max(0, Math.Min(off, DataLength(wav)));
        }

        /// <summary>The audio between two points, as a standalone WAV. Used for copy.</summary>
        internal static byte[]? Extract(byte[] wav, int startMs, int endMs)
        {
            int a = OffsetOf(wav, Math.Min(startMs, endMs));
            int b = OffsetOf(wav, Math.Max(startMs, endMs));
            if (b - a <= 0) return null;
            var pcm = new byte[b - a];
            Buffer.BlockCopy(wav, HeaderBytes + a, pcm, 0, pcm.Length);
            return Rebuild(wav, pcm);
        }

        /// <summary>Everything except the audio between two points. Used for delete.</summary>
        internal static byte[]? Remove(byte[] wav, int startMs, int endMs)
        {
            int a = OffsetOf(wav, Math.Min(startMs, endMs));
            int b = OffsetOf(wav, Math.Max(startMs, endMs));
            if (b - a <= 0) return wav;
            int len = DataLength(wav) - (b - a);
            if (len <= 0) return null;   // deleting everything leaves no recording, not an empty one
            var pcm = new byte[len];
            Buffer.BlockCopy(wav, HeaderBytes, pcm, 0, a);
            Buffer.BlockCopy(wav, HeaderBytes + b, pcm, a, DataLength(wav) - b);
            return Rebuild(wav, pcm);
        }

        /// <summary>Splices one recording into another at a point. Used for paste.</summary>
        internal static byte[]? Insert(byte[] wav, int atMs, byte[] clip)
        {
            if (clip == null || DataLength(clip) <= 0) return wav;
            // Formats must agree or the splice plays at the wrong speed. Both come from this app's
            // own recorder, so this is a guard against a future format change, not a conversion.
            if (BytesPerSec(clip) != BytesPerSec(wav) || BlockAlign(clip) != BlockAlign(wav)) return null;

            int at = OffsetOf(wav, atMs);
            int own = DataLength(wav), add = DataLength(clip);
            var pcm = new byte[own + add];
            Buffer.BlockCopy(wav, HeaderBytes, pcm, 0, at);
            Buffer.BlockCopy(clip, HeaderBytes, pcm, at, add);
            Buffer.BlockCopy(wav, HeaderBytes + at, pcm, at + add, own - at);
            return Rebuild(wav, pcm);
        }

        /// <summary>Wraps new PCM in a copy of the original's header, with the sizes corrected.</summary>
        private static byte[] Rebuild(byte[] template, byte[] pcm)
        {
            var outp = new byte[HeaderBytes + pcm.Length];
            Buffer.BlockCopy(template, 0, outp, 0, HeaderBytes);
            Buffer.BlockCopy(pcm, 0, outp, HeaderBytes, pcm.Length);
            // RIFF size (whole file minus the 8-byte RIFF tag) and data chunk size.
            Buffer.BlockCopy(BitConverter.GetBytes(36 + pcm.Length), 0, outp, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(pcm.Length), 0, outp, 40, 4);
            return outp;
        }
    }
}

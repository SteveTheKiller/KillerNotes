using System;

namespace KillerNotes.Services
{
    /// <summary>
    /// The one place that knows a recording might not be a WAV.
    ///
    /// Recordings are STORED as FLAC and WORKED ON as PCM. Everything above this layer - WavEdit,
    /// the waveform, the envelope, DictationPlayer - stays in PCM and needs no idea a codec exists,
    /// which is what keeps slicing and playback unchanged by the format switch.
    ///
    /// The format is SNIFFED from the first four bytes rather than recorded in a column, so notes
    /// made before FLAC still open with no schema change and no migration pass. Their recordings
    /// stay WAV until they are next edited.
    /// </summary>
    internal static class AudioCodec
    {
        internal enum Format { Unknown, Wav, Flac, Mp3 }

        internal static Format Sniff(byte[]? data)
        {
            if (data == null || data.Length < 4) return Format.Unknown;
            if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F') return Format.Wav;
            if (data[0] == 'f' && data[1] == 'L' && data[2] == 'a' && data[3] == 'C') return Format.Flac;
            // ID3 tag, or a bare MPEG frame sync (11 set bits).
            if (data[0] == 'I' && data[1] == 'D' && data[2] == '3') return Format.Mp3;
            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return Format.Mp3;
            return Format.Unknown;
        }

        internal static string ExtensionFor(byte[]? data) => Sniff(data) switch
        {
            Format.Flac => ".flac",
            Format.Mp3 => ".mp3",
            _ => ".wav",
        };

        /// <summary>
        /// Prepares a freshly captured WAV for storage. FLAC when the native is present, otherwise
        /// the WAV unchanged - a missing codec must cost disk space, never a recording.
        /// </summary>
        internal static byte[] ForStorage(byte[] wav)
        {
            if (Sniff(wav) != Format.Wav) return wav;           // already compressed
            return FlacCodec.FromWav(wav) ?? wav;
        }

        /// <summary>
        /// Turns whatever came out of the database into 16-bit PCM WAV. Returns null only when the
        /// data is genuinely unusable, so callers can tell "no audio" from "audio in a format this
        /// build cannot read".
        /// </summary>
        internal static byte[]? ToPcm(byte[]? stored)
        {
            if (stored == null) return null;
            return Sniff(stored) switch
            {
                Format.Wav => stored,
                Format.Flac => FlacCodec.ToWav(stored),
                // Nothing writes MP3 into a note - it is an export format - but a file dragged in
                // later would land here, and saying so beats returning silence.
                _ => null,
            };
        }
    }
}

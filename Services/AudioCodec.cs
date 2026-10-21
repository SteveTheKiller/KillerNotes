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

        /// <summary>A WAV or FLAC payload's length from its header, for the chip of a recording that
        /// arrives as a file (folder import). 0 when the header cannot say.</summary>
        internal static int DurationMs(byte[]? data)
        {
            try
            {
                switch (Sniff(data))
                {
                    case Format.Wav:
                    {
                        int byteRate = 0;
                        for (int at = 12; at + 8 <= data!.Length;)
                        {
                            string id = System.Text.Encoding.ASCII.GetString(data, at, 4);
                            int size = BitConverter.ToInt32(data, at + 4);
                            if (id == "fmt " && size >= 16) byteRate = BitConverter.ToInt32(data, at + 16);
                            if (id == "data") return byteRate > 0 ? (int)(Math.Min(size, data.Length - at - 8) * 1000L / byteRate) : 0;
                            if (size < 0) return 0;
                            at += 8 + size + (size & 1);
                        }
                        return 0;
                    }
                    case Format.Flac:
                    {
                        // STREAMINFO follows "fLaC" and a 4-byte block header: 10 bytes of block and
                        // frame sizes, then a 20-bit sample rate and, after channels and bit depth,
                        // a 36-bit total sample count.
                        if (data!.Length < 26) return 0;
                        int rate = (data[18] << 12) | (data[19] << 4) | (data[20] >> 4);
                        long samples = ((long)(data[21] & 0x0F) << 32) | (uint)(data[22] << 24 | data[23] << 16 | data[24] << 8 | data[25]);
                        return rate > 0 ? (int)Math.Min(int.MaxValue, samples * 1000 / rate) : 0;
                    }
                    default:
                        return 0;
                }
            }
            catch (ArgumentException) { return 0; }   // a truncated header
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

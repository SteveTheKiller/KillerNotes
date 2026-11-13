using System;
using System.IO;
using System.Text;

namespace KillerNotes.Services
{
    /// <summary>Reads bounded RIFF chunks and supplies the canonical PCM layout used by editing.</summary>
    internal static class PcmWave
    {
        internal static byte[]? Normalize(byte[]? source)
        {
            if (source == null || source.Length < 12 || Tag(source, 0) != "RIFF" || Tag(source, 8) != "WAVE") return null;
            long end = 8L + BitConverter.ToUInt32(source, 4);
            if (end < 12 || end > source.Length) return null;
            int format = -1, data = -1, length = 0;
            for (long position = 12; position + 8 <= end;)
            {
                int at = (int)position;
                uint size = BitConverter.ToUInt32(source, at + 4);
                long next = position + 8 + size + (size & 1);
                if (next > end) return null;
                string tag = Tag(source, at);
                if (tag == "fmt " && format < 0)
                {
                    if (size < 16) return null;
                    format = at + 8;
                }
                if (tag == "data" && data < 0) { data = at + 8; length = (int)size; }
                position = next;
            }
            if (format < 0 || data < 0 || length == 0) return null;
            int channels = BitConverter.ToUInt16(source, format + 2);
            int rate = BitConverter.ToInt32(source, format + 4);
            int align = BitConverter.ToUInt16(source, format + 12);
            int bits = BitConverter.ToUInt16(source, format + 14);
            if (BitConverter.ToUInt16(source, format) != 1 || (bits != 8 && bits != 16 && bits != 24 && bits != 32) || channels < 1 || channels > 8 ||
                rate <= 0 || rate > int.MaxValue / (channels * 2) || align != channels * (bits / 8) || length % align != 0) return null;
            if (bits == 16 && format == 20 && data == 44 && source.Length == length + 44 &&
                BitConverter.ToInt32(source, format + 8) == rate * align) return source;
            long outputLength = (long)length / (bits / 8) * 2;
            if (outputLength > int.MaxValue - 44) return null;
            using var stream = new MemoryStream(44 + (int)outputLength);
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + (int)outputLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2);
            writer.Write((short)(channels * 2)); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write((int)outputLength);
            if (bits == 16) writer.Write(source, data, length);
            else
                for (int i = 0; i < length; i += bits / 8)
                {
                    int at = data + i;
                    short sample = bits == 8 ? (short)((source[at] - 128) << 8)
                        : bits == 24 ? (short)((source[at + 1]) | (source[at + 2] << 8))
                        : (short)(BitConverter.ToInt32(source, at) >> 16);
                    writer.Write(sample);
                }
            return stream.ToArray();
        }

        private static string Tag(byte[] source, int at) => Encoding.ASCII.GetString(source, at, 4);
    }
}

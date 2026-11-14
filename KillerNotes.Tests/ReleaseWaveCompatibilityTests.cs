using System;
using System.IO;
using System.Linq;
using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class ReleaseWaveCompatibilityTests
    {
        [Theory]
        [InlineData(0u, 4u, false)]
        [InlineData(uint.MaxValue, 4u, false)]
        [InlineData(40u, uint.MaxValue, false)]
        [InlineData(41u, 5u, true)]
        public void ImportedWaveRetainsCompleteSamplesDespiteInexactContainerSizes(uint riff, uint dataSize, bool partial)
        {
            byte[] source = Wave();
            if (partial) source = source.Concat(new byte[] { 255 }).ToArray();
            Buffer.BlockCopy(BitConverter.GetBytes(riff), 0, source, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(dataSize), 0, source, 40, 4);
            byte[]? result = AudioCodec.ToPcm(source);
            Assert.NotNull(result);
            Assert.Equal(new byte[] { 255, 127, 0, 128 }, result!.Skip(44).ToArray());
            Assert.Equal(40, BitConverter.ToInt32(result, 4));
            Assert.Equal(4, BitConverter.ToInt32(result, 40));
        }

        [Theory]
        [InlineData(24)]
        [InlineData(32)]
        public void ExtensibleIntegerPcmKeepsSignedSampleExtrema(int bits)
        {
            byte[]? result = AudioCodec.ToPcm(Wave(bits, true));
            Assert.NotNull(result);
            Assert.Equal(new byte[] { 255, 127, 0, 128 }, result!.Skip(44).ToArray());
        }

        private static byte[] Wave(int bits = 16, bool extensible = false)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(40);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(extensible ? 40 : 16);
            writer.Write((ushort)(extensible ? 65534 : 1)); writer.Write((ushort)1); writer.Write(1000); writer.Write(1000 * bits / 8);
            writer.Write((ushort)(bits / 8)); writer.Write((ushort)bits);
            if (extensible)
            {
                writer.Write((ushort)22); writer.Write((ushort)bits); writer.Write(0);
                writer.Write(new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray());
            }
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bits / 4);
            writer.Write(bits == 24 ? new byte[] { 255, 255, 127, 0, 0, 128 }
                : bits == 32 ? new byte[] { 255, 255, 255, 127, 0, 0, 0, 128 }
                : new byte[] { 255, 127, 0, 128 });
            stream.Position = 4; writer.Write((int)stream.Length - 8);
            return stream.ToArray();
        }
    }
}

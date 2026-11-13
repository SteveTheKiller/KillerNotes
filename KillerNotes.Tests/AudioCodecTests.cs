using System;
using System.IO;
using System.Linq;
using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class AudioCodecTests
    {
        [Fact]
        public void ImportedWaveMetadataIsNotEditedAsAudio()
        {
            byte[] original = WaveWithMetadata();
            byte[] before = original.ToArray();
            byte[]? pcm = AudioCodec.ToPcm(original);
            Assert.NotNull(pcm);
            Assert.Equal(4, WavEdit.DurationMs(pcm!));
            byte[]? clip = WavEdit.Extract(pcm!, 0, 2);
            Assert.NotNull(clip);
            Assert.Equal(new byte[] { 1, 0, 2, 0 }, clip!.Skip(44).ToArray());
            Assert.Equal(before, original);
        }

        [Fact]
        public void EightBitImportedAudioKeepsItsLevelWhenPreparedForEditing()
        {
            byte[]? pcm = AudioCodec.ToPcm(WaveWithMetadata(8, new byte[] { 0, 128, 255, 128 }));
            Assert.NotNull(pcm);
            Assert.Equal(new short[] { -32768, 0, 32512, 0 }, Enumerable.Range(0, 4).Select(i => BitConverter.ToInt16(pcm!, 44 + i * 2)));
        }

        [Theory]
        [InlineData(24)]
        [InlineData(32)]
        public void HighDepthImportedAudioKeepsItsLevelWhenPreparedForEditing(int bits)
        {
            byte[] samples = bits == 24 ? new byte[] { 255, 255, 127, 0, 0, 128 }
                : new byte[] { 255, 255, 255, 127, 0, 0, 0, 128 };
            byte[]? pcm = AudioCodec.ToPcm(WaveWithMetadata(bits, samples));
            Assert.NotNull(pcm);
            Assert.Equal(32767, BitConverter.ToInt16(pcm!, 44));
            Assert.Equal(-32768, BitConverter.ToInt16(pcm!, 46));
        }

        [Fact]
        public void FlacEncodingSkipsImportedMetadata()
        {
            Assert.True(FlacCodec.Available);
            byte[]? encoded = FlacCodec.FromWav(WaveWithMetadata());
            Assert.NotNull(encoded);
            byte[]? decoded = FlacCodec.ToWav(encoded!);
            Assert.NotNull(decoded);
            Assert.Equal(new byte[] { 1, 0, 2, 0, 3, 0, 4, 0 }, decoded!.Skip(44).ToArray());
        }

        [Theory]
        [InlineData(4)]
        [InlineData(16)]
        public void TruncatedOrOversizedChunksAreRejected(int sizeOffset)
        {
            byte[] source = WaveWithMetadata();
            Buffer.BlockCopy(BitConverter.GetBytes(uint.MaxValue), 0, source, sizeOffset, 4);
            Assert.Null(AudioCodec.ToPcm(source));
        }

        private static byte[] WaveWithMetadata(int bits = 16, byte[]? samples = null)
        {
            samples ??= new byte[] { 1, 0, 2, 0, 3, 0, 4, 0 };
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            void Tag(string tag) => writer.Write(Encoding.ASCII.GetBytes(tag));
            Tag("RIFF"); writer.Write(0); Tag("WAVE");
            Tag("JUNK"); writer.Write(3); writer.Write(new byte[] { 99, 98, 97, 0 });
            Tag("fmt "); writer.Write(18);
            writer.Write((short)1); writer.Write((short)1); writer.Write(1000); writer.Write(1000 * bits / 8);
            writer.Write((short)(bits / 8)); writer.Write((short)bits); writer.Write((short)0);
            Tag("data"); writer.Write(samples.Length); writer.Write(samples);
            if ((samples.Length & 1) != 0) writer.Write((byte)0);
            Tag("LIST"); writer.Write(4); Tag("INFO");
            stream.Position = 4; writer.Write((int)stream.Length - 8);
            return stream.ToArray();
        }
    }
}

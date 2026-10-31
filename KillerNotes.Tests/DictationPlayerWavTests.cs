using System;
using System.IO;
using System.Reflection;
using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A recording can arrive inside an imported note or a folder import, so its WAV header is
    /// untrusted. Playing a damaged one must not throw out of the click handler that starts it.
    /// </summary>
    public sealed class DictationPlayerWavTests
    {
        private static (bool Ok, int Offset, int Length) Parse(byte[] wav)
        {
            var method = typeof(DictationPlayer).GetMethod("ParseWav", BindingFlags.Static | BindingFlags.NonPublic)!;
            object?[] args = { wav, null, 0, 0 };
            bool ok = (bool)method.Invoke(null, args)!;
            return (ok, (int)args[2]!, (int)args[3]!);
        }

        /// <summary>RIFF/WAVE, a 16 kHz mono 16-bit fmt chunk, then the given chunk with a claimed size.</summary>
        private static byte[] WavWith(string id, int claimedSize, int actualBytes)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(0); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes(id)); w.Write(claimedSize);
            w.Write(new byte[actualBytes]);
            return ms.ToArray();
        }

        [Fact]
        public void ADataChunkClaimingNearlyTwoGigabytesIsCutToWhatTheFileHolds()
        {
            byte[] wav = WavWith("data", int.MaxValue, 10);

            var (ok, offset, length) = Parse(wav);

            Assert.True(ok);
            Assert.Equal(wav.Length - 10, offset);
            Assert.Equal(10, length);
        }

        [Fact]
        public void AnotherChunkClaimingNearlyTwoGigabytesEndsTheWalkWithoutThrowing()
        {
            byte[] wav = WavWith("LIST", int.MaxValue, 10);
            // Nothing after the oversized chunk can be found, so the file is simply not playable.
            var (ok, _, _) = Parse(wav);

            Assert.False(ok);
        }
    }
}

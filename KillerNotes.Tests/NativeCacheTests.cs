using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The extracted native libraries are checked against the copy embedded in the app by
    /// SHA-256, not by size, so a damaged or swapped file of the same length is replaced before
    /// it is loaded. Uses scratch files only; nothing is loaded.
    /// </summary>
    public sealed class NativeCacheTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kn-native-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static byte[] Embedded => Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray();

        [Fact]
        public void ASameLengthChangeIsRepaired()
        {
            string target = Path.Combine(_dir, "lib.dll");
            Directory.CreateDirectory(_dir);
            var damaged = Embedded;
            damaged[100] ^= 0xFF;
            File.WriteAllBytes(target, damaged);

            NativeCache.PublishVerified(target, Embedded);

            Assert.Equal(Embedded, File.ReadAllBytes(target));
            Assert.Single(Directory.GetFiles(_dir));
        }

        [Fact]
        public void AMissingCacheIsWrittenAndAGoodOneLeftAlone()
        {
            string target = Path.Combine(_dir, "lib.dll");
            NativeCache.PublishVerified(target, Embedded);
            var written = File.GetLastWriteTimeUtc(target);
            NativeCache.PublishVerified(target, Embedded);
            Assert.Equal(Embedded, File.ReadAllBytes(target));
            Assert.Equal(written, File.GetLastWriteTimeUtc(target));
        }

        [Fact]
        public void ConcurrentPublishersAgree()
        {
            string target = Path.Combine(_dir, "lib.dll");
            Parallel.For(0, 8, _ => NativeCache.PublishVerified(target, Embedded));
            Assert.Equal(Embedded, File.ReadAllBytes(target));
            Assert.Single(Directory.GetFiles(_dir));
        }
    }
}

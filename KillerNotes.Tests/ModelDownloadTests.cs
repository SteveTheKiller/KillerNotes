using System;
using System.IO;
using System.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>A speech model takes its real name only when the whole download arrived, and a
    /// download that stops leaves no partial file behind.</summary>
    public sealed class ModelDownloadTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
        private string Target => Path.Combine(_root, "ggml-base.en.bin");

        public ModelDownloadTests() => Directory.CreateDirectory(_root);

        public void Dispose() { try { Directory.Delete(_root, true); } catch { /* best effort */ } }

        [Fact]
        public void AWholeDownloadTakesItsRealNameAndReplacesAnOlderModel()
        {
            File.WriteAllBytes(Target, new byte[] { 9 });

            WhisperSpeech.Receive(new MemoryStream(new byte[300_000]), 300_000, Target, _ => { }, CancellationToken.None);

            Assert.Equal(300_000, new FileInfo(Target).Length);
            Assert.False(File.Exists(Target + ".part"));
        }

        [Fact]
        public void ADownloadWithNoStatedLengthIsAccepted()
        {
            WhisperSpeech.Receive(new MemoryStream(new byte[1000]), null, Target, _ => { }, CancellationToken.None);

            Assert.Equal(1000, new FileInfo(Target).Length);
        }

        [Fact]
        public void ADownloadThatEndsEarlyIsNotInstalled()
        {
            File.WriteAllBytes(Target, new byte[] { 9 });

            Assert.ThrowsAny<IOException>(() =>
                WhisperSpeech.Receive(new MemoryStream(new byte[1000]), 5000, Target, _ => { }, CancellationToken.None));

            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Target));
            Assert.False(File.Exists(Target + ".part"));
        }

        [Fact]
        public void ADownloadThatEndsEarlyDoesNotCreateAModel()
        {
            Assert.ThrowsAny<IOException>(() =>
                WhisperSpeech.Receive(new MemoryStream(new byte[1000]), 5000, Target, _ => { }, CancellationToken.None));

            Assert.False(File.Exists(Target));
            Assert.False(File.Exists(Target + ".part"));
        }

        [Fact]
        public void ACanceledDownloadLeavesNoPartFile()
        {
            using var cancel = new CancellationTokenSource();
            var source = new Script(onRead: reads => { if (reads == 2) cancel.Cancel(); });

            Assert.ThrowsAny<OperationCanceledException>(() =>
                WhisperSpeech.Receive(source, 10_000_000, Target, _ => { }, cancel.Token));

            Assert.False(File.Exists(Target));
            Assert.False(File.Exists(Target + ".part"));
        }

        [Fact]
        public void AFailedDownloadLeavesNoPartFile()
        {
            var source = new Script(onRead: reads => { if (reads == 2) throw new IOException("The connection was closed."); });

            Assert.Throws<IOException>(() =>
                WhisperSpeech.Receive(source, 10_000_000, Target, _ => { }, CancellationToken.None));

            Assert.False(File.Exists(Target));
            Assert.False(File.Exists(Target + ".part"));
        }

        private sealed class Script : Stream
        {
            private readonly Action<int> _onRead;
            private int _reads;
            internal Script(Action<int> onRead) => _onRead = onRead;
            public override int Read(byte[] buffer, int offset, int count)
            {
                _onRead(++_reads);
                int n = Math.Min(count, 4096);
                Array.Clear(buffer, offset, n);
                return n;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}

using System;
using System.IO;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class SpeechEngineTests
    {
        [Theory]
        [InlineData(true, true, true, "ggml-cpu-haswell.dll")]
        [InlineData(false, true, true, "ggml-cpu-sandybridge.dll")]
        [InlineData(false, false, true, "ggml-cpu-sse42.dll")]
        [InlineData(false, false, false, "ggml-cpu-x64.dll")]
        public void TheFastestBuildTheProcessorRunsIsChosen(bool avx2, bool avx, bool sse42, string file) =>
            Assert.Equal(file, SpeechEngine.CpuBuildFor(avx2, avx, sse42).File);

        [Fact]
        public async System.Threading.Tasks.Task CancelingStopsADownloadThatHasStalledMidBody()
        {
            using var stalled = new StalledStream(new byte[] { 1, 2, 3 });
            using var cts = new System.Threading.CancellationTokenSource();
            long received = 0;
            var copy = System.Threading.Tasks.Task.Run(() =>
                SpeechEngine.CopyBody(stalled, new MemoryStream(), n =>
                {
                    received += n;
                    cts.Cancel();   // the user presses Cancel while the connection is silent
                }, cts.Token));

            var first = await System.Threading.Tasks.Task.WhenAny(copy, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5)));
            stalled.Dispose();   // frees the worker if the copy was still stuck, so the test ends either way

            Assert.True(first == copy, "Cancel left the download blocked on a read that never returns");
            Assert.Equal(3, received);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copy);
        }

        /// <summary>Hands out its bytes, then blocks every read until it is disposed, as a socket does
        /// when the server stops sending.</summary>
        private sealed class StalledStream : Stream
        {
            private readonly MemoryStream _head;
            private readonly System.Threading.ManualResetEventSlim _closed = new();
            public StalledStream(byte[] head) => _head = new MemoryStream(head);
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = _head.Read(buffer, offset, count);
                if (n > 0) return n;
                _closed.Wait();
                throw new ObjectDisposedException(nameof(StalledStream));
            }
            protected override void Dispose(bool disposing) { if (disposing) _closed.Set(); base.Dispose(disposing); }
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

        [Fact]
        public void EveryHostedEngineFileMatchesItsPinnedHashAndSize()
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo != null && !File.Exists(Path.Combine(repo.FullName, "KillerNotes.csproj"))) repo = repo.Parent;
            Assert.NotNull(repo);
            string dir = Path.Combine(repo!.FullName, "third_party", "audio");
            foreach (var (file, sha, bytes) in SpeechEngine.Core)
            {
                Assert.True(SpeechEngine.HashMatches(Path.Combine(dir, file), sha), file);
                Assert.Equal(bytes, new FileInfo(Path.Combine(dir, file)).Length);
            }
            foreach (var (_, file, sha, bytes) in SpeechEngine.CpuBuilds)
            {
                Assert.True(SpeechEngine.HashMatches(Path.Combine(dir, file), sha), file);
                Assert.Equal(bytes, new FileInfo(Path.Combine(dir, file)).Length);
            }
        }

        [Fact]
        public void TheEngineIsNotEmbeddedInTheApp()
        {
            foreach (string name in typeof(SpeechEngine).Assembly.GetManifestResourceNames())
                Assert.DoesNotContain("ggml", name, StringComparison.OrdinalIgnoreCase);
        }
    }
}

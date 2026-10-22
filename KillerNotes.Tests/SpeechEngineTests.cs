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

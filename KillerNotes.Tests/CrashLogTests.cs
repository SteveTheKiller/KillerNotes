using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The crash log keeps its history when trimmed and writes one entry per failure.</summary>
    public sealed class CrashLogTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kn-crashlog-" + Guid.NewGuid().ToString("N"));
        private string LogPath => Path.Combine(_dir, "crash.log");

        public CrashLogTests() => Directory.CreateDirectory(_dir);

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private int Entries() => Regex.Matches(File.ReadAllText(LogPath), "^=== ", RegexOptions.Multiline).Count;

        [Fact]
        public void TrimmingKeepsTheLastEntryWhenItIsLargerThanHalfTheCap()
        {
            string small = "=== 2026-01-01 00:00:00  dispatcher ===\nsmall\n\n";
            string large = "=== 2026-01-02 00:00:00  dispatcher ===\n" + new string('x', 6000) + "\n\n";
            File.WriteAllText(LogPath, small + large);

            App.TrimCrashLog(LogPath, 4096);

            string kept = File.ReadAllText(LogPath);
            Assert.NotEqual("", kept);
            Assert.StartsWith("=== 2026-01-02", kept);
        }

        [Fact]
        public void TheSameExceptionIsLoggedOnceWhenBothHandlersSeeIt()
        {
            var ex = new InvalidOperationException("boom");
            App.WriteCrashLog("dispatcher", ex, LogPath);
            App.WriteCrashLog("appdomain", ex, LogPath);
            Assert.Equal(1, Entries());

            App.WriteCrashLog("appdomain", new InvalidOperationException("another"), LogPath);
            Assert.Equal(2, Entries());
        }
    }
}

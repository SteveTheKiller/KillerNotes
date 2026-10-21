using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The single-instance pipe takes one line (a forwarded file path) per connection. A client
    /// that connects and never finishes its line, or sends far more than a path, must be dropped
    /// within the deadline so the next double-click launch still gets through.
    /// </summary>
    public sealed class LaunchMessageTests
    {
        [Fact]
        public void ACompleteLineIsRead()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("C:\\notes\\shared.knote\nignored"));
            Assert.Equal("C:\\notes\\shared.knote", LaunchMessage.ReadLine(stream, TimeSpan.FromSeconds(2), 65536));
        }

        [Fact]
        public void AnEmptyLineMeansJustActivate()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\r\n"));
            Assert.Equal("", LaunchMessage.ReadLine(stream, TimeSpan.FromSeconds(2), 65536));
        }

        [Fact]
        public void AClientThatStallsIsDroppedAtTheDeadline()
        {
            using var server = new AnonymousPipeServerStream(PipeDirection.In);
            using var client = new AnonymousPipeClientStream(PipeDirection.Out, server.ClientSafePipeHandle);
            client.Write(Encoding.UTF8.GetBytes("C:\\half"), 0, 7);   // no newline, never closes
            client.Flush();

            var sw = Stopwatch.StartNew();
            Assert.Null(LaunchMessage.ReadLine(server, TimeSpan.FromMilliseconds(500), 65536));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), sw.Elapsed.ToString());
        }

        [Fact]
        public void AnOversizedMessageIsRefused()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 70000) + "\n"));
            Assert.Null(LaunchMessage.ReadLine(stream, TimeSpan.FromSeconds(2), 65536));
        }
    }
}

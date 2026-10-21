using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using KillerNotes.Cli;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A CLI write waits for KillerNotes' answer for a bounded time. A busy or hung app makes the
    /// CLI stop with its own exit code (the change may or may not have happened), and a cut-off
    /// or oversized answer fails quickly instead of hanging.
    /// </summary>
    public sealed class CliResponseDeadlineTests
    {
        [Fact]
        public void AnAnswerThatNeverComesTimesOut()
        {
            using var server = new AnonymousPipeServerStream(PipeDirection.In);
            using var client = new AnonymousPipeClientStream(PipeDirection.Out, server.ClientSafePipeHandle);
            var sw = Stopwatch.StartNew();
            var (body, outcome) = Program.ReadResponseFrame(server, TimeSpan.FromMilliseconds(400));
            Assert.Null(body);
            Assert.Equal(Program.ResponseOutcome.TimedOut, outcome);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), sw.Elapsed.ToString());
        }

        [Fact]
        public void ACutOffAnswerFails()
        {
            using var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            w.Write(10); w.Write(new byte[] { 1, 2, 3 }); w.Flush();
            stream.Position = 0;
            var (body, outcome) = Program.ReadResponseFrame(stream, TimeSpan.FromSeconds(2));
            Assert.Null(body);
            Assert.Equal(Program.ResponseOutcome.Invalid, outcome);
        }

        [Fact]
        public void AnOversizedAnswerIsRefusedBeforeReadingIt()
        {
            using var stream = new MemoryStream();
            new BinaryWriter(stream).Write(50_000_000);
            stream.Position = 0;
            Assert.Equal(Program.ResponseOutcome.Invalid, Program.ReadResponseFrame(stream, TimeSpan.FromSeconds(2)).Outcome);
        }

        [Fact]
        public void ACompleteAnswerIsRead()
        {
            using var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            w.Write(3); w.Write(new byte[] { 7, 8, 9 }); w.Flush();
            stream.Position = 0;
            var (body, outcome) = Program.ReadResponseFrame(stream, TimeSpan.FromSeconds(2));
            Assert.Equal(Program.ResponseOutcome.Complete, outcome);
            Assert.Equal(new byte[] { 7, 8, 9 }, body);
        }
    }
}

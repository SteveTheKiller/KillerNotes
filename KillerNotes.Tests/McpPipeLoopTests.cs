using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using KillerNotes.Cli;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The MCP pipe loop pauses when its pipe cannot be made, and answers a request it
    /// refuses instead of dropping the connection without a word.</summary>
    public sealed class McpPipeLoopTests
    {
        [Theory]
        [InlineData("busy")]
        [InlineData("denied")]
        public void ATakenPipeNameIsRetriedWithAPauseNotASpin(string kind)
        {
            string name = "KillerNotes-test-" + Guid.NewGuid().ToString("N");
            using var holder = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var delays = new ConcurrentQueue<TimeSpan>();
            int attempts = 0;
            using var stop = new CancellationTokenSource();
            var loop = Task.Run(() => App.RunMcpPipeLoop(
                () =>
                {
                    Interlocked.Increment(ref attempts);
                    if (kind == "denied") throw new UnauthorizedAccessException();
                    return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                },
                _ => "",
                delay => { delays.Enqueue(delay); stop.Token.WaitHandle.WaitOne(20); },
                stop.Token));

            // Generous limit: a full parallel run can starve the loop's thread for a while.
            Assert.True(SpinWait.SpinUntil(() => delays.Count >= 3, 10000), "The loop did not pause and retry.");
            stop.Cancel();

            Assert.True(loop.Wait(5000), "The loop did not stop.");
            Assert.NotEmpty(delays);
            Assert.All(delays, delay => Assert.True(delay > TimeSpan.Zero));
            Assert.True(attempts <= delays.Count + 1, attempts + " attempts for " + delays.Count + " pauses");
        }

        [Fact]
        public void ARequestOverTheLimitGetsAnErrorAnswer()
        {
            using var pipe = new Duplex(BitConverter.GetBytes(3_000_000));

            App.ServeMcpExchange(pipe, _ => throw new InvalidOperationException("The command must not run."));

            byte[] answer = pipe.Written.ToArray();
            Assert.True(answer.Length > 4, "The client got no answer.");
            Assert.Equal(answer.Length - 4, BitConverter.ToInt32(answer, 0));
            var body = new byte[answer.Length - 4];
            Buffer.BlockCopy(answer, 4, body, 0, body.Length);
            var response = Program.Deserialize<McpResponse>(body);
            Assert.False(response.Success);
            Assert.Equal("The request is too large.", response.Error);
        }

        [Fact]
        public void ARequestJustUnderTheLimitStillRuns()
        {
            byte[] payload = Program.Serialize(new McpRequest { Arguments = new[] { "create", new string('a', 1_500_000) } });
            var frame = new byte[4 + payload.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, frame, 0, 4);
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
            using var pipe = new Duplex(frame);
            string? seen = null;

            App.ServeMcpExchange(pipe, arguments => { seen = arguments[0]; return "{}"; });

            Assert.Equal("create", seen);
        }

        private sealed class Duplex : Stream
        {
            private readonly MemoryStream _in;
            internal readonly MemoryStream Written = new MemoryStream();
            internal Duplex(byte[] incoming) => _in = new MemoryStream(incoming);
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, count);
            public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}

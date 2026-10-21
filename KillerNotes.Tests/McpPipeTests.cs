using System.IO;
using System.Text;
using KillerNotes.Cli;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class McpPipeTests
    {
        [Fact]
        public async System.Threading.Tasks.Task ClientThatStopsReadingDoesNotBlockTheNextClient()
        {
            string name = "KillerNotes-test-" + System.Guid.NewGuid().ToString("N");
            var pipes = new System.Collections.Concurrent.ConcurrentBag<System.IO.Pipes.NamedPipeServerStream>();
            using var stop = new System.Threading.CancellationTokenSource();
            var server = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    bool dropped = false;
                    for (int i = 0; i < 2; i++)
                    {
                        using var pipe = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut,
                            1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous, 4096, 4096);
                        pipes.Add(pipe);
                        await pipe.WaitForConnectionAsync(stop.Token);
                        bool written = App.WriteMcpResponse(pipe, i == 0 ? new byte[8 * 1024 * 1024] : new byte[] { 42 },
                            i == 0 ? System.TimeSpan.FromMilliseconds(300) : System.TimeSpan.FromSeconds(10));
                        if (i == 0) dropped = !written;
                        else return dropped && written;
                    }
                }
                catch (System.OperationCanceledException) { }
                catch (System.ObjectDisposedException) { }
                catch (IOException) { }
                return false;
            });
            using var stalled = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut);
            using var next = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.Asynchronous);
            try
            {
                // Generous outer limits: they only prove nothing hangs, and a full parallel test run
                // can starve this test for seconds. The 300 ms stall deadline above is the one under test.
                stalled.Connect(10000);
                next.Connect(10000);
                byte[] response = new byte[5];
                async System.Threading.Tasks.Task<int> ReadReply()
                {
                    int count = 0;
                    while (count < response.Length)
                    {
                        int n = await next.ReadAsync(response, count, response.Length - count);
                        if (n == 0) break;
                        count += n;
                    }
                    return count;
                }
                var read = ReadReply();
                Assert.Same(read, await System.Threading.Tasks.Task.WhenAny(read, System.Threading.Tasks.Task.Delay(10000)));
                Assert.Equal(5, await read);
                Assert.Equal(new byte[] { 1, 0, 0, 0, 42 }, response);
                Assert.Same(server, await System.Threading.Tasks.Task.WhenAny(server, System.Threading.Tasks.Task.Delay(10000)));
                Assert.True(await server);
            }
            finally
            {
                stop.Cancel();
                stalled.Dispose();
                next.Dispose();
                foreach (var pipe in pipes) pipe.Dispose();
                Assert.Same(server, await System.Threading.Tasks.Task.WhenAny(server, System.Threading.Tasks.Task.Delay(5000)));
            }
        }

        [Theory]
        [InlineData(0, "")]
        [InlineData(2_000_001, "")]
        [InlineData(1, "{")]
        [InlineData(5, "{")]
        public void InvalidRequestFrameIsRejected(int declaredLength, string body)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(declaredLength);
                writer.Write(Encoding.UTF8.GetBytes(body));
            }
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            Assert.Null(App.ReadMcpRequest(reader));
        }

        [Fact]
        public void ValidRequestFrameIsAccepted()
        {
            byte[] payload = Program.Serialize(new McpRequest { Arguments = ["list"] });
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(payload.Length);
                writer.Write(payload);
            }
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            Assert.Equal("list", Assert.Single(App.ReadMcpRequest(reader)!.Arguments));
        }

        // A client that connects and never finishes its request must not hold the single pipe
        // instance forever, or every later CLI and MCP call times out until the app restarts.
        [Theory]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 10, 0 })]
        [InlineData(new byte[] { 10, 0, 0, 0, 1 })]
        public void StalledClientIsDroppedAfterTheTimeout(byte[] partial)
        {
            using var server = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
            using var client = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, server.ClientSafePipeHandle);
            client.Write(partial, 0, partial.Length);
            client.Flush();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.Null(App.ReadMcpRequest(server, System.TimeSpan.FromMilliseconds(300)));
            Assert.True(clock.Elapsed < System.TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        }
    }
}

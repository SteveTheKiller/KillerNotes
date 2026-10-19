using System.IO;
using System.Text;
using KillerNotes.Cli;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class McpPipeTests
    {
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

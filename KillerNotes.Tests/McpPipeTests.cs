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
    }
}

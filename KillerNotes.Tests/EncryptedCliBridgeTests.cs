using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using KillerNotes.Cli;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class EncryptedCliBridgeTests
    {
        [Fact]
        public async Task BusyAppPipeCanFinishItsCurrentRequestBeforeAcceptingTheNext()
        {
            string name = "KillerNotes-busy-test-" + Guid.NewGuid().ToString("N");
            using var occupied = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var occupant = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            var connected = occupied.WaitForConnectionAsync();
            occupant.Connect(1000); await connected;
            using var stop = new System.Threading.CancellationTokenSource();
            NamedPipeServerStream? next = null;
            var served = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, stop.Token);
                    occupied.Dispose();
                    next = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await next.WaitForConnectionAsync();
                    App.ServeMcpExchange(next, _ => "{\"fixture\":true}");
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            });
            var previousOut = Console.Out; var previousError = Console.Error;
            using var output = new StringWriter(); using var error = new StringWriter();
            Console.SetOut(output); Console.SetError(error);
            try
            {
                int exit = Program.Run(new[] { "stats" }, name);
                Assert.True(exit == 0, "A busy app was reported closed: " + error);
                Assert.Contains("fixture", output.ToString());
            }
            finally
            {
                Console.SetOut(previousOut); Console.SetError(previousError);
                stop.Cancel(); occupied.Dispose(); next?.Dispose(); await served;
            }
        }

        [Fact]
        public void ClosedAppFailsPromptlyWithAnUnlockInstruction()
        {
            var previous = Console.Error;
            using var error = new StringWriter();
            Console.SetError(error);
            try
            {
                var clock = Stopwatch.StartNew();
                Assert.Equal(1, Program.Run(new[] { "stats" }, "KillerNotes-closed-test-" + Guid.NewGuid().ToString("N")));
                Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(200), "Closed-app failure took " + clock.Elapsed);
                Assert.Contains("Open and unlock KillerNotes", error.ToString());
            }
            finally { Console.SetError(previous); }
        }

        [Theory]
        [InlineData("get")]
        [InlineData("search")]
        [InlineData("list")]
        [InlineData("groups")]
        [InlineData("tags")]
        [InlineData("backlinks")]
        [InlineData("links")]
        [InlineData("history")]
        [InlineData("stats")]
        [InlineData("templates")]
        [InlineData("trash-list")]
        [InlineData("tag-definitions")]
        [InlineData("version-get")]
        public async Task ActiveReadUsesTheUnlockedAppPipeWithoutOpeningTheEncryptedFile(string command)
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Encrypted bridge fixture");
            NoteStore.Save(id, "Encrypted bridge fixture", new byte[] { 1 }, "Saved fixture content");
            NoteStore.Snapshot(id, true);
            long version = NoteStore.ListHistory(id)[0].Id;
            NoteStore.SetPassword("isolated fixture password");
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            string name = "KillerNotes-read-test-" + Guid.NewGuid().ToString("N");
            using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var served = Task.Run(async () =>
            {
                try { await server.WaitForConnectionAsync(); App.ServeMcpExchange(server, shell.ExecuteMcpCommand); }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            });
            var previousOut = Console.Out;
            var previousError = Console.Error;
            using var output = new StringWriter();
            using var error = new StringWriter();
            Console.SetOut(output); Console.SetError(error);
            try
            {
                string[] arguments = command == "version-get" ? new[] { command, "--id", id.ToString(), "--version-id", version.ToString() } : command == "search" ? new[] { command, "Encrypted bridge" } :
                    command is "get" or "backlinks" or "links" or "history" ? new[] { command, id.ToString() } : new[] { command };
                int exit = Program.Run(arguments, name);
                Assert.True(exit == 0, error.ToString());
                if (command is "get" or "search" or "list" or "version-get") Assert.Contains("Encrypted bridge fixture", output.ToString());
                else if (command == "stats") Assert.Contains("\"notes\":1", output.ToString());
                else Assert.StartsWith("[", output.ToString());
            }
            finally
            {
                Console.SetOut(previousOut); Console.SetError(previousError);
                server.Dispose();
                await served;
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using KillerNotes.Cli;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>KillerNotes' pipes open only to the signed-in user, refuse a name someone else
    /// already holds, and keep listening after any failure; the CLI talks only to a server that
    /// runs as the same user.</summary>
    public sealed class PipeSecurityTests
    {
        private static string NewName() => "KillerNotes-test-" + Guid.NewGuid().ToString("N");

        [Theory]
        [InlineData(PipeDirection.InOut)]
        [InlineData(PipeDirection.In)]
        public void ThePipeGrantsAccessOnlyToTheCurrentUser(PipeDirection direction)
        {
            using var pipe = App.CreateOwnerOnlyPipe(NewName(), direction);
            var me = WindowsIdentity.GetCurrent().User!;

            var rules = pipe.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<PipeAccessRule>().ToList();

            Assert.NotEmpty(rules);
            Assert.All(rules, r => Assert.Equal(me, r.IdentityReference));
        }

        [Fact]
        public void ANameAnotherServerAlreadyHoldsIsRefused()
        {
            string name = NewName();
            using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            var ex = Record.Exception(() => App.CreateOwnerOnlyPipe(name, PipeDirection.InOut).Dispose());

            Assert.True(ex is IOException || ex is UnauthorizedAccessException, "Created a second instance: " + ex);
        }

        [Fact]
        public async Task AClientCanStillReachTheServer()
        {
            string name = NewName();
            using var server = App.CreateOwnerOnlyPipe(name, PipeDirection.In);
            var accepted = server.WaitForConnectionAsync();
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out);
            await client.ConnectAsync(3000);
            Assert.True(await Task.WhenAny(accepted, Task.Delay(3000)) == accepted, "The server did not accept the client.");
            await accepted;
            Assert.True(Program.ServerRunsAsCurrentUser(client));
        }

        [Fact]
        public void TheServerIdentityCheckTellsUsersApart()
        {
            Assert.True(Program.ProcessRunsAsCurrentUser((uint)Process.GetCurrentProcess().Id));
            // The System process runs as SYSTEM, never as the signed-in user.
            Assert.False(Program.ProcessRunsAsCurrentUser(4));
            Assert.False(Program.ProcessRunsAsCurrentUser(0));
        }

        [Theory]
        [InlineData("busy")]
        [InlineData("denied")]
        [InlineData("other")]
        public async Task TheLaunchPipeRetriesWithAPauseAndNeverEndsTheThread(string kind)
        {
            var delays = new ConcurrentQueue<TimeSpan>();
            int attempts = 0;
            using var stop = new CancellationTokenSource();
            var loop = Task.Run(() => App.RunLaunchPipeLoop(
                () =>
                {
                    Interlocked.Increment(ref attempts);
                    if (kind == "busy") throw new IOException("All pipe instances are busy.");
                    if (kind == "denied") throw new UnauthorizedAccessException();
                    throw new InvalidOperationException();
                },
                _ => { },
                delay => { delays.Enqueue(delay); stop.Token.WaitHandle.WaitOne(20); },
                stop.Token));

            bool paused = SpinWait.SpinUntil(() => delays.Count >= 3 || loop.IsCompleted, 10000);
            stop.Cancel();
            Assert.True(await Task.WhenAny(loop, Task.Delay(5000)) == loop, "The loop did not stop.");
            Assert.False(loop.IsFaulted, "The loop thread ended with " + loop.Exception?.InnerException);
            Assert.True(paused && delays.Count >= 3, "The loop did not pause and retry.");
            Assert.All(delays, d => Assert.True(d > TimeSpan.Zero));
            Assert.True(attempts <= delays.Count + 1, attempts + " attempts for " + delays.Count + " pauses");
        }

        [Fact]
        public async Task TheMcpPipeLoopSurvivesAnUnexpectedFailure()
        {
            var delays = new ConcurrentQueue<TimeSpan>();
            using var stop = new CancellationTokenSource();
            var loop = Task.Run(() => App.RunMcpPipeLoop(
                () => throw new InvalidOperationException(),
                _ => "",
                delay => { delays.Enqueue(delay); stop.Token.WaitHandle.WaitOne(20); },
                stop.Token));

            SpinWait.SpinUntil(() => delays.Count >= 3 || loop.IsCompleted, 10000);
            stop.Cancel();
            Assert.True(await Task.WhenAny(loop, Task.Delay(5000)) == loop, "The loop did not stop.");
            Assert.False(loop.IsFaulted, "The loop thread ended with " + loop.Exception?.InnerException);
            Assert.True(delays.Count >= 3);
        }
    }
}

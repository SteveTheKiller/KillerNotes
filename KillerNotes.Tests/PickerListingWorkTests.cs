using System;
using System.Threading;
using System.Threading.Tasks;
using KillerNotes.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class PickerListingWorkTests
    {
        // A directory read on a dead network path cannot be interrupted, so the request is
        // abandoned while its read keeps blocking. Two such reads must not leave the dialog unable
        // to list even a local folder.
        [Fact]
        public async Task AbandonedHungReadsDoNotBlockTheNextListing()
        {
            using var work = new PickerListingWork();
            using var hang = new ManualResetEventSlim();
            using var firstStarted = new ManualResetEventSlim();
            using var secondStarted = new ManualResetEventSlim();
            try
            {
                _ = work.RunAsync<int>(_ => { firstStarted.Set(); hang.Wait(); return 0; }, (_, _) => Task.CompletedTask, _ => { });
                Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(30)));
                _ = work.RunAsync<int>(_ => { secondStarted.Set(); hang.Wait(); return 0; }, (_, _) => Task.CompletedTask, _ => { });
                Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(30)));

                var listed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = work.RunAsync<string>(_ => "local", (rows, _) => { listed.TrySetResult(rows); return Task.CompletedTask; },
                    error => listed.TrySetException(error));

                Task finished = await Task.WhenAny(listed.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                Assert.Same(listed.Task, finished);
                Assert.Equal("local", await listed.Task);
            }
            finally { hang.Set(); }
        }
    }
}

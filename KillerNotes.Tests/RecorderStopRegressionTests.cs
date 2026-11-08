using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [CollectionDefinition("Recorder capture", DisableParallelization = true)]
    public sealed class RecorderCaptureCollection { }

    [Collection("Recorder capture")]
    public sealed class RecorderStopRegressionTests
    {
        [Fact]
        public void StopIncludesPartialBuffersReturnedByStopAndReset()
        {
            using var device = new FakeCapture();
            Assert.True(DictationRecorder.Start());
            byte[]? wav = DictationRecorder.Stop();
            Assert.NotNull(wav);
            Assert.Equal(3244, wav!.Length);
            Assert.Equal(Enumerable.Repeat((byte)0x12, 1600).Concat(Enumerable.Repeat((byte)0x34, 1600)), wav.Skip(44));
            Assert.Equal(8, device.Unprepared);
            Assert.True(device.Closed);
            Assert.False(device.RequeuedAfterStop);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StopAndCancelWaitForDelayedReturnsBeforeFreeingHeaders(bool cancel)
        {
            using var device = new FakeCapture(delayReset: true);
            Assert.True(DictationRecorder.Start());
            var operation = Task.Run(() =>
            {
                if (!cancel) return DictationRecorder.Stop();
                DictationRecorder.Cancel();
                return null;
            });
            try
            {
                Assert.True(device.ResetReached.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(operation.IsCompleted);
                Assert.Equal(0, device.Unprepared);
            }
            finally { device.AllowReset.Set(); }
            Assert.Same(operation, await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(5))));
            byte[]? result = await operation;
            Assert.Null(device.ResetFailure);
            if (cancel)
            {
                Assert.Null(result);
                Assert.Equal(0, DictationRecorder.ElapsedMs);
                Assert.Null(DictationRecorder.Stop());
            }
            else
            {
                Assert.NotNull(result);
                Assert.Equal(3244, result!.Length);
                Assert.Equal(Enumerable.Repeat((byte)0x12, 1600).Concat(Enumerable.Repeat((byte)0x34, 1600)), result.Skip(44));
            }
            Assert.Equal(8, device.Unprepared);
            Assert.True(device.Closed);
            Assert.False(device.RequeuedAfterStop);
        }

        private sealed class FakeCapture : IDisposable
        {
            private readonly DictationRecorder.CaptureApi previous = DictationRecorder.Capture;
            private readonly List<IntPtr> pending = new();
            private DictationRecorder.WaveInProc callback = null!;
            private bool stopping;
            private Thread? resetWorker;
            public ManualResetEventSlim ResetReached { get; } = new();
            public ManualResetEventSlim AllowReset { get; } = new();
            public int Unprepared { get; private set; }
            public bool Closed { get; private set; }
            public bool RequeuedAfterStop { get; private set; }
            public Exception? ResetFailure { get; private set; }

            public FakeCapture(bool delayReset = false)
            {
                DictationRecorder.Capture = new DictationRecorder.CaptureApi
                {
                    DeviceCount = () => 1,
                    Open = Open,
                    Prepare = (_, _, _) => 0,
                    Unprepare = (_, header, _) => { lock (pending) Assert.DoesNotContain(header, pending); Unprepared++; return 0; },
                    AddBuffer = (_, header, _) => { lock (pending) { if (stopping) RequeuedAfterStop = true; pending.Add(header); } return 0; },
                    Start = _ => 0,
                    Stop = _ => { stopping = true; CompleteFirst(1600, 0x12); return 0; },
                    Reset = _ =>
                    {
                        if (delayReset)
                        {
                            resetWorker = new Thread(() =>
                            {
                                try { AllowReset.Wait(); CompleteReset(); }
                                catch (Exception ex) { ResetFailure = ex; }
                            }) { IsBackground = true };
                            resetWorker.Start();
                            ResetReached.Set();
                        }
                        else CompleteReset();
                        return 0;
                    },
                    Close = _ => { lock (pending) Assert.Empty(pending); Closed = true; return 0; },
                };
            }

            private void CompleteReset()
            {
                CompleteFirst(1600, 0x34);
                while (true)
                {
                    lock (pending) if (pending.Count == 0) break;
                    CompleteFirst(0, 0);
                }
            }

            private int Open(out IntPtr handle, int device, ref DictationRecorder.WAVEFORMATEX format,
                DictationRecorder.WaveInProc completed, IntPtr instance, int flags)
            {
                handle = new IntPtr(123);
                callback = completed;
                return 0;
            }

            private void CompleteFirst(int count, byte value)
            {
                IntPtr header;
                lock (pending)
                {
                    if (pending.Count == 0) return;
                    header = pending[0];
                    pending.RemoveAt(0);
                }
                var data = Marshal.PtrToStructure<DictationRecorder.WAVEHDR>(header);
                Marshal.Copy(Enumerable.Repeat(value, count).ToArray(), 0, data.lpData, count);
                data.dwBytesRecorded = count;
                Marshal.StructureToPtr(data, header, false);
                Exception? failure = null;
                var driver = new Thread(() =>
                {
                    try { callback(new IntPtr(123), 0x3C0, IntPtr.Zero, header, IntPtr.Zero); }
                    catch (Exception ex) { failure = ex; }
                }) { IsBackground = true };
                driver.Start();
                Assert.True(driver.Join(TimeSpan.FromSeconds(5)), "The capture callback blocked during native Stop or Reset.");
                Assert.Null(failure);
            }

            public void Dispose()
            {
                AllowReset.Set();
                try
                {
                    resetWorker?.Join();
                    DictationRecorder.Cancel();
                }
                finally
                {
                    DictationRecorder.Capture = previous;
                    ResetReached.Dispose();
                    AllowReset.Dispose();
                }
            }
        }
    }
}

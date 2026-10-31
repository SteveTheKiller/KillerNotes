using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization;
using System.Threading;
using KillerNotes.Cli;

namespace KillerNotes
{
    public partial class App
    {
        internal static string McpPipeName => $"KillerNotes-MCP-{Process.GetCurrentProcess().SessionId}";

        internal static McpRequest? ReadMcpRequest(BinaryReader reader)
        {
            try
            {
                int length = reader.ReadInt32();
                if (length < 1 || length > Program.MaxFrameBytes) return null;
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length) return null;
                return Program.Deserialize<McpRequest>(payload);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException ||
                                       ex is SerializationException || ex is System.Xml.XmlException ||
                                       ex is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>How long a connected client has to deliver its whole request. The pipe has
        /// one instance, so a client that stalls would otherwise block every later call.</summary>
        internal static readonly TimeSpan McpRequestTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Reads one request frame, giving up once the timeout passes. The caller
        /// disposes the stream afterward, which cancels a read still pending.</summary>
        internal static McpRequest? ReadMcpRequest(Stream stream, TimeSpan timeout) => ReadMcpRequest(stream, timeout, out _);

        /// <summary>As above; <paramref name="tooLarge"/> tells a request over the size limit
        /// from one that never arrived, so the client can be told.</summary>
        internal static McpRequest? ReadMcpRequest(Stream stream, TimeSpan timeout, out bool tooLarge)
        {
            tooLarge = false;
            var clock = Stopwatch.StartNew();
            byte[]? head = ReadExactly(stream, 4, timeout, clock);
            if (head == null) return null;
            int length = BitConverter.ToInt32(head, 0);
            if (length > Program.MaxFrameBytes) { tooLarge = true; return null; }
            if (length < 1) return null;
            byte[]? payload = ReadExactly(stream, length, timeout, clock);
            if (payload == null) return null;
            var frame = new byte[4 + length];
            Buffer.BlockCopy(head, 0, frame, 0, 4);
            Buffer.BlockCopy(payload, 0, frame, 4, length);
            using var reader = new BinaryReader(new MemoryStream(frame));
            return ReadMcpRequest(reader);
        }

        private static byte[]? ReadExactly(Stream stream, int count, TimeSpan timeout, Stopwatch clock)
        {
            var buffer = new byte[count];
            int read = 0;
            try
            {
                while (read < count)
                {
                    TimeSpan left = timeout - clock.Elapsed;
                    if (left <= TimeSpan.Zero) return null;
                    var pending = stream.ReadAsync(buffer, read, count - read);
                    if (!pending.Wait(left)) return null;
                    if (pending.Result == 0) return null;
                    read += pending.Result;
                }
                return buffer;
            }
            catch (Exception ex) when (ex is AggregateException || ex is IOException || ex is ObjectDisposedException)
            {
                return null;
            }
        }

        internal static bool WriteMcpResponse(Stream stream, byte[] payload, TimeSpan timeout)
            => WriteMcpResponseAsync(stream, payload, timeout).GetAwaiter().GetResult();

        private static async System.Threading.Tasks.Task<bool> WriteMcpResponseAsync(Stream stream, byte[] payload, TimeSpan timeout)
        {
            try
            {
                byte[] frame = new byte[4 + payload.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, frame, 0, 4);
                Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
                var pending = stream.WriteAsync(frame, 0, frame.Length);
                using var deadline = new CancellationTokenSource();
                var delay = System.Threading.Tasks.Task.Delay(timeout, deadline.Token);
                if (await System.Threading.Tasks.Task.WhenAny(pending, delay).ConfigureAwait(false) != pending)
                {
                    stream.Dispose();
                    try { await pending.ConfigureAwait(false); }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                    catch (OperationCanceledException) { }
                    return false;
                }
                deadline.Cancel();
                await pending.ConfigureAwait(false);
                return true;
            }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
            catch (OperationCanceledException) { return false; }
        }

        /// <summary>One client: reads its request, runs it, and writes the answer.</summary>
        internal static void ServeMcpExchange(Stream pipe, Func<string[], string> execute)
        {
            var request = ReadMcpRequest(pipe, McpRequestTimeout, out bool tooLarge);
            if (tooLarge)
            {
                WriteMcpResponse(pipe, Program.Serialize(new McpResponse { Success = false, Error = "The request is too large." }), McpRequestTimeout);
                return;
            }
            if (request == null) return;
            McpResponse response;
            try { response = new McpResponse { Success = true, Json = execute(request.Arguments) }; }
            catch (Exception ex) { response = new McpResponse { Success = false, Error = ex.Message }; }
            byte[] payload = Program.Serialize(response);
            WriteMcpResponse(pipe, payload, McpRequestTimeout);
        }

        /// <summary>Serves clients one at a time until stopped. <paramref name="create"/> makes the
        /// server end of the pipe for each client.</summary>
        internal static void RunMcpPipeLoop(Func<NamedPipeServerStream> create, Func<string[], string> execute,
            Action<TimeSpan> pause, CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                try { pipe = create(); }
                // The name is taken by another copy (a demo beside a normal run) or by another account:
                // wait before asking again instead of spinning, and never end the thread, which would end the process.
                catch (IOException) { pause(McpPipeRetryDelay); continue; }
                catch (UnauthorizedAccessException) { pause(McpPipeRetryDelay); continue; }
                using (pipe)
                {
                    try
                    {
                        pipe.WaitForConnection();
                        ServeMcpExchange(pipe, execute);
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { return; }
                }
            }
        }

        private static readonly TimeSpan McpPipeRetryDelay = TimeSpan.FromSeconds(1);

        private void StartMcpPipeServer()
        {
            var thread = new Thread(() => RunMcpPipeLoop(
                // Asynchronous, so disposing the pipe cancels a read a stalled client left pending.
                () => new NamedPipeServerStream(McpPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
                arguments => Dispatcher.Invoke(() => ((Shell.MainWindow)MainWindow).ExecuteMcpCommand(arguments)),
                Thread.Sleep, CancellationToken.None))
            { IsBackground = true, Name = "KillerNotes MCP pipe" };
            thread.Start();
        }
    }
}

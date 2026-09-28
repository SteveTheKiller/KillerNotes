using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization.Json;
using System.Threading;
using KillerNotes.Cli;

namespace KillerNotes
{
    public partial class App
    {
        internal static string McpPipeName => $"KillerNotes-MCP-{Process.GetCurrentProcess().SessionId}";

        private void StartMcpPipeServer()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var pipe = new NamedPipeServerStream(McpPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                        pipe.WaitForConnection();
                        var reader = new BinaryReader(pipe, System.Text.Encoding.UTF8, true);
                        int requestLength = reader.ReadInt32();
                        if (requestLength < 1 || requestLength > 2_000_000) throw new InvalidDataException("Invalid request length");
                        var request = Program.Deserialize<McpRequest>(reader.ReadBytes(requestLength));
                        McpResponse response;
                        try
                        {
                            string json = Dispatcher.Invoke(() => ((Shell.MainWindow)MainWindow).ExecuteMcpCommand(request.Arguments));
                            response = new McpResponse { Success = true, Json = json };
                        }
                        catch (Exception ex) { response = new McpResponse { Success = false, Error = ex.Message }; }
                        byte[] payload = Program.Serialize(response);
                        var writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, true);
                        writer.Write(payload.Length); writer.Write(payload); writer.Flush();
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { return; }
                }
            }) { IsBackground = true, Name = "KillerNotes MCP pipe" };
            thread.Start();
        }
    }
}

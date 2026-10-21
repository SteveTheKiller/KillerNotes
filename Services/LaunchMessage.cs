using System;
using System.IO;
using System.Text;

namespace KillerNotes.Services
{
    /// <summary>
    /// Reads the one line a second launch forwards to the running instance (a file path, or
    /// nothing). Bounded in time and size: the pipe accepts one client at a time, so a client that
    /// connected and never finished its line used to hold it forever and every later double-click
    /// launch failed to reach the app.
    /// </summary>
    internal static class LaunchMessage
    {
        /// <summary>The line without its terminator; null when the deadline passes or the
        /// message grows past maxBytes. The caller disposes the stream either way, which abandons
        /// any read still pending.</summary>
        internal static string? ReadLine(Stream stream, TimeSpan deadline, int maxBytes)
        {
            var due = DateTime.UtcNow + deadline;
            var bytes = new MemoryStream();
            var buffer = new byte[512];
            while (true)
            {
                var remaining = due - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return null;
                var read = stream.ReadAsync(buffer, 0, buffer.Length);
                try { if (!read.Wait(remaining)) return null; }
                catch (AggregateException) { return null; }
                int n = read.Result;
                int newline = Array.IndexOf(buffer, (byte)'\n', 0, n);
                int take = newline >= 0 ? newline : n;
                if (bytes.Length + take > maxBytes) return null;
                bytes.Write(buffer, 0, take);
                if (newline >= 0 || n == 0) break;   // a line, or the client closed without one
            }
            return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r');
        }
    }
}

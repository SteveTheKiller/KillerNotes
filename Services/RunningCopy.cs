using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KillerNotes.Services
{
    /// <summary>Finds other processes running a given exe, in any desktop session.</summary>
    internal static class RunningCopy
    {
        private const uint QueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>True when a process other than <paramref name="exceptId"/> or
        /// <paramref name="launcher"/> runs the exe at this path. A process whose path cannot be read
        /// is not counted.</summary>
        internal static bool IsRunning(string exe, int exceptId, int? launcher = null)
        {
            string full = Path.GetFullPath(exe);
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))
                using (process)
                {
                    if (process.Id == exceptId || process.Id == launcher) continue;
                    string? path = ImagePath(process.Id);
                    if (path != null && string.Equals(path, full, StringComparison.OrdinalIgnoreCase)) return true;
                }
            return false;
        }

        private static string? ImagePath(int id)
        {
            IntPtr handle = OpenProcess(QueryLimitedInformation, false, id);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var name = new StringBuilder(1024);
                int size = name.Capacity;
                return QueryFullProcessImageName(handle, 0, name, ref size) ? name.ToString() : null;
            }
            finally { CloseHandle(handle); }
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace KillerNotes.Features.Security
{
    /// <summary>
    /// When Lock now happens without being asked: Windows locking or going to sleep (on by
    /// default), and the whole computer sitting idle (off by default). Idle is measured across
    /// Windows, not inside this app, so working in another program never locks the notes.
    /// </summary>
    internal static class AutoLock
    {
        internal const string WindowsLockSetting = "LockWithWindows";
        internal const string IdleSetting = "IdleLockMinutes";

        /// <summary>The idle choices the About card offers; 0 is off.</summary>
        internal static readonly int[] IdleChoices = [0, 5, 15, 30, 60];

        internal static int ParseIdleMinutes(string? stored)
            => int.TryParse(stored, out int minutes) && Array.IndexOf(IdleChoices, minutes) >= 0 ? minutes : 0;

        internal static bool WindowsLockEnabled(string? stored) => stored != "0";

        internal static bool IdleDue(int minutes, TimeSpan idle) => minutes > 0 && idle >= TimeSpan.FromMinutes(minutes);

        /// <summary>Time since the last keyboard or mouse input anywhere in this Windows session.</summary>
        internal static TimeSpan SystemIdle()
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            // Both are milliseconds since boot in 32 bits; the unsigned subtraction survives the
            // 49.7 day wrap.
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LastInputInfo info);
    }
}

using System;
using System.Runtime.InteropServices;

namespace KillerNotes.Services
{
    /// <summary>
    /// Lets the windowed app print to the terminal that started it in command-line mode. A
    /// windowed program starts with no console, so without this "killernotes --cli" typed in a
    /// terminal prints nothing. Handles inherited from a pipe or a file are left alone so
    /// redirected output and tools that read it keep working.
    /// </summary>
    internal static class CliConsole
    {
        private const int AttachParentProcess = -1;
        private const int StdOutputHandle = -11;
        private const int StdErrorHandle = -12;
        private const uint FileTypeDisk = 0x0001;
        private const uint FileTypePipe = 0x0003;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFileType(IntPtr handle);

        internal static void AttachToParent()
        {
            try
            {
                if (!IsRedirected(StdOutputHandle) && !IsRedirected(StdErrorHandle))
                    AttachConsole(AttachParentProcess);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
        }

        private static bool IsRedirected(int handle)
        {
            IntPtr stream = GetStdHandle(handle);
            if (stream == IntPtr.Zero || stream == new IntPtr(-1)) return false;
            uint type = GetFileType(stream);
            return type == FileTypeDisk || type == FileTypePipe;
        }
    }
}

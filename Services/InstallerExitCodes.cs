#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading;

namespace KillerFamily.Deployment
{
    internal static class InstallerExitCodes
    {
        internal const int Success = 0;
        internal const int Failure = 1;
        internal const int AccessDenied = 5;
        internal const int MissingRuntime = 10;
        internal const int DiskFull = 112;
        internal const int Cancelled = 1223;
        internal const int Busy = 1618;
        internal const int SecurityRejected = 1625;

#if NET8_0_OR_GREATER
        private static readonly System.Buffers.SearchValues<char> PathSeparators = System.Buffers.SearchValues.Create("\\/");
#else
        private static readonly char[] PathSeparators = ['\\', '/'];
#endif

        internal static int FromException(Exception error)
        {
            for (Exception? current = error; current != null; current = current.InnerException)
            {
                if (current is OperationCanceledException) return Cancelled;
                if (current is UnauthorizedAccessException) return AccessDenied;
                if (current is SecurityException) return SecurityRejected;
                int code = current is Win32Exception native ? native.NativeErrorCode :
                    current is IOException ? current.HResult & 0xffff : 0;
                if (code == 39 || code == DiskFull) return DiskFull;
                if (code == AccessDenied || code == Cancelled || code == Busy || code == SecurityRejected)
                    return code;
            }
            return Failure;
        }

        /// <summary>
        /// A machine-wide silent install writes to Program Files. Run without admin rights, as the
        /// Microsoft Store runs installers, it relaunches the same exe with the same arguments
        /// through UAC and returns that copy's exit code. A build that fails <paramref name="trusted"/>
        /// is refused before any prompt, and a declined prompt returns Cancelled. Returns null when
        /// the process is already elevated and should install itself.
        /// </summary>
        internal static int? ElevateIfNeeded(string arguments, Func<bool> trusted)
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return null;
            }
            if (!trusted()) return SecurityRejected;

            IntPtr owner = IntPtr.Zero, largeIcon = IntPtr.Zero, smallIcon = IntPtr.Zero, process = IntPtr.Zero;
            try
            {
                using var current = Process.GetCurrentProcess();
                string? path = current.MainModule?.FileName;
                if (path is null || path.Length == 0) return Failure;

                // The UAC prompt takes its taskbar button from its owner window. Without one it shows
                // the generic Windows icon and no name, so an off-screen window carrying the app's
                // name and icon owns the prompt while it is open.
                var version = FileVersionInfo.GetVersionInfo(path);
                string title = !string.IsNullOrWhiteSpace(version.ProductName) ? version.ProductName!
                    : Path.GetFileNameWithoutExtension(path);
                owner = CreateWindowEx(WsExAppWindow, "Static", title, WsPopup,
                    -32000, -32000, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (owner != IntPtr.Zero)
                {
                    if (ExtractIconEx(path, 0, out largeIcon, out smallIcon, 1) > 0)
                    {
                        SendMessage(owner, WmSetIcon, (IntPtr)1, largeIcon);
                        SendMessage(owner, WmSetIcon, IntPtr.Zero, smallIcon);
                    }
                    ShowWindow(owner, SwShowNoActivate);
                }

                // The prompt runs on a worker thread so this thread keeps pumping messages for the
                // owner window, which would otherwise show as not responding.
                bool started = false;
                int error = 0;
                using var finished = new ManualResetEvent(false);
                var worker = new Thread(() =>
                {
                    var info = new ShellExecuteInfo
                    {
                        cbSize = Marshal.SizeOf(typeof(ShellExecuteInfo)),
                        fMask = SeeMaskNoCloseProcess | SeeMaskNoAsync,
                        hwnd = owner,
                        lpVerb = "runas",
                        lpFile = path,
                        lpParameters = arguments,
                        nShow = SwHide,
                    };
                    started = ShellExecuteEx(ref info);
                    error = started ? 0 : Marshal.GetLastWin32Error();
                    process = info.hProcess;
                    finished.Set();
                });
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
                PumpUntil(finished);
                worker.Join();

                if (owner != IntPtr.Zero) { DestroyWindow(owner); owner = IntPtr.Zero; }
                if (!started) return FromException(new Win32Exception(error));
                if (process == IntPtr.Zero) return Failure;
                WaitForSingleObject(process, Infinite);
                return GetExitCodeProcess(process, out uint code) ? unchecked((int)code) : Failure;
            }
            catch (Exception error)
            {
                return FromException(error);
            }
            finally
            {
                if (process != IntPtr.Zero) CloseHandle(process);
                if (owner != IntPtr.Zero) DestroyWindow(owner);
                if (largeIcon != IntPtr.Zero) DestroyIcon(largeIcon);
                if (smallIcon != IntPtr.Zero) DestroyIcon(smallIcon);
            }
        }

        private static void PumpUntil(WaitHandle signal)
        {
            IntPtr[] handles = [signal.SafeWaitHandle.DangerousGetHandle()];
            while (MsgWaitForMultipleObjects(1, handles, false, Infinite, QsAllInput) != 0)
            {
                while (PeekMessage(out NativeMessage message, IntPtr.Zero, 0, 0, PmRemove))
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
        }

        private const int WsExAppWindow = 0x00040000;
        private const uint WsPopup = 0x80000000;
        private const uint WmSetIcon = 0x0080;
        private const int SwHide = 0;
        private const int SwShowNoActivate = 4;
        private const uint SeeMaskNoCloseProcess = 0x00000040;
        private const uint SeeMaskNoAsync = 0x00000100;
        private const uint QsAllInput = 0x04FF;
        private const uint PmRemove = 0x0001;
        private const uint Infinite = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellExecuteInfo
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string? lpVerb;
            public string? lpFile;
            public string? lpParameters;
            public string? lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public string? lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIconOrMonitor;
            public IntPtr hProcess;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds, uint wakeMask);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private static bool ContainsPathSeparator(string value) =>
#if NET8_0_OR_GREATER
            value.AsSpan().ContainsAny(PathSeparators);
#else
            value.IndexOfAny(PathSeparators) >= 0;
#endif

        internal static IDisposable Acquire(string product)
        {
            if (string.IsNullOrWhiteSpace(product) || ContainsPathSeparator(product))
                throw new ArgumentException("A product name is required.", nameof(product));
            var mutex = new Mutex(false, @"Global\KillerTools-Install-" + product);
            bool owned;
            try
            {
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; }
            }
            catch { mutex.Dispose(); throw; }
            if (!owned)
            {
                mutex.Dispose();
                throw new Win32Exception(Busy, "Another installation of this application is in progress.");
            }
            return new Lease(mutex);
        }

        private sealed class Lease : IDisposable
        {
            private Mutex? _mutex;
            internal Lease(Mutex mutex) { _mutex = mutex; }
            public void Dispose()
            {
                var mutex = _mutex;
                if (mutex == null) return;
                _mutex = null;
                try { mutex.ReleaseMutex(); }
                finally { mutex.Dispose(); }
            }
        }
    }
}

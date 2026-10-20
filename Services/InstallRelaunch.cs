using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace KillerNotes.Services
{
    internal interface IInstallRelaunchProcess : IDisposable
    {
        int Id { get; }
        long CreationFileTime { get; }
        bool WaitForExit(int milliseconds);
    }

    internal interface IInstallRelaunchProcesses
    {
        IInstallRelaunchProcess Current();
        IInstallRelaunchProcess? Find(int id);
        bool Start(ProcessStartInfo start);
    }

    /// <summary>Hands installation startup to a helper that never enters the normal app.</summary>
    internal sealed class InstallRelaunch
    {
        private const string HelperArgument = "--install-relaunch-after";
        private readonly IInstallRelaunchProcesses _processes;

        public InstallRelaunch() : this(new SystemProcesses()) { }
        public InstallRelaunch(IInstallRelaunchProcesses processes) => _processes = processes;

        public bool StartInstalled(string installedExe, Action shutdown)
        {
            try
            {
                using var parent = _processes.Current();
                string arguments = HelperArgument + " " + parent.Id.ToString(CultureInfo.InvariantCulture)
                    + " " + parent.CreationFileTime.ToString(CultureInfo.InvariantCulture);
                if (!_processes.Start(new ProcessStartInfo(installedExe, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(installedExe) ?? "",
                })) return false;
            }
            catch { return false; }

            shutdown();
            return true;
        }

        /// <summary>Returns true for every helper request, including invalid ones, so it cannot
        /// fall through to the UI, command line, single-instance lock, or stores.</summary>
        public bool TryRunHelper(string[] args, string installedExe, out int exitCode)
        {
            exitCode = 1;
            if (!Array.Exists(args, a => string.Equals(a, HelperArgument, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (args.Length != 3 || !string.Equals(args[0], HelperArgument, StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0
                || !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out long created)
                || created <= 0 || created > DateTime.MaxValue.ToFileTimeUtc()) return true;

            try
            {
                using var current = _processes.Current();
                if (id == current.Id) return true;
                using var parent = _processes.Find(id);
                // A missing PID has already exited. An existing PID must still identify the exact
                // parent; a reused PID, inaccessible process, or stalled shutdown cannot launch.
                if (parent != null && (parent.CreationFileTime != created || !parent.WaitForExit(30000)))
                    return true;
                if (_processes.Start(new ProcessStartInfo(installedExe)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(installedExe) ?? "",
                })) exitCode = 0;
            }
            catch { /* A helper failure must never open the normal application. */ }
            return true;
        }

        internal sealed class SystemProcesses : IInstallRelaunchProcesses
        {
            private readonly Func<int, Process> _findProcess;
            internal SystemProcesses(Func<int, Process>? findProcess = null)
                => _findProcess = findProcess ?? Process.GetProcessById;

            public IInstallRelaunchProcess Current() => new SystemProcess(Process.GetCurrentProcess());
            public IInstallRelaunchProcess? Find(int id)
            {
                Process process;
                try { process = _findProcess(id); }
                catch (ArgumentException) { return null; }
                try { return new SystemProcess(process); }
                // The parent can exit between PID lookup and opening its process handle.
                catch (InvalidOperationException) { process.Dispose(); return null; }
                catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 87)
                { process.Dispose(); return null; }
                catch { process.Dispose(); throw; }
            }
            public bool Start(ProcessStartInfo start)
            {
                using var process = Process.Start(start);
                return process != null;
            }
        }

        private sealed class SystemProcess : IInstallRelaunchProcess
        {
            private readonly Process _process;
            public SystemProcess(Process process)
            {
                // Retain one process handle before reading its identity and waiting. PID reuse
                // cannot redirect the later wait to a different process.
                _ = process.Handle;
                _process = process;
            }
            public int Id => _process.Id;
            public long CreationFileTime => _process.StartTime.ToFileTimeUtc();
            public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);
            public void Dispose() => _process.Dispose();
        }
    }
}

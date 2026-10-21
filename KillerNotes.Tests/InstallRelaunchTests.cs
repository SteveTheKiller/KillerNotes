using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public class InstallRelaunchTests
    {
        [Fact]
        public void InstalledCopyWaitsForTheExactOldProcessBeforeNormalStartup()
        {
            var boundary = new Processes();
            var relaunch = new InstallRelaunch(boundary);
            bool shutdown = false;
            Assert.True(relaunch.StartInstalled(@"C:\Installed App\KillerNotes.exe", () =>
            {
                shutdown = true;
                boundary.Parent.Exited = true;
            }));
            Assert.True(shutdown);
            var helper = Assert.Single(boundary.Starts);
            Assert.False(helper.UseShellExecute);
            Assert.True(helper.CreateNoWindow);
            Assert.Equal(ProcessWindowStyle.Hidden, helper.WindowStyle);
            Assert.Equal("--install-relaunch-after 17 123456", helper.Arguments);
            boundary.CurrentId = 31;
            Assert.True(relaunch.TryRunHelper(helper.Arguments.Split(' '), helper.FileName, out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal(30000, boundary.Parent.WaitMilliseconds);
            Assert.Equal(2, boundary.Starts.Count);
            var normal = boundary.Starts[1];
            Assert.Equal(helper.FileName, normal.FileName);
            Assert.Equal("", normal.Arguments);
            Assert.False(normal.CreateNoWindow);
            Assert.Equal(ProcessWindowStyle.Normal, normal.WindowStyle);
        }

        [Theory]
        [InlineData("timeout")]
        [InlineData("reused")]
        [InlineData("query")]
        [InlineData("wait")]
        [InlineData("launch")]
        public void HelperFailuresNeverLaunchTheNormalApplication(string failure)
        {
            var boundary = new Processes { CurrentId = 31 };
            boundary.Parent.Exited = failure != "timeout";
            if (failure == "reused") boundary.Parent.CreationFileTime++;
            if (failure == "query") boundary.FindThrows = true;
            if (failure == "wait") boundary.Parent.WaitThrows = true;
            if (failure == "launch") boundary.StartSucceeds = false;
            Assert.True(new InstallRelaunch(boundary).TryRunHelper(
                new[] { "--install-relaunch-after", "17", "123456" }, @"C:\Installed\App.exe", out int code));
            Assert.Equal(1, code);
            Assert.Equal(failure == "launch" ? 1 : 0, boundary.Starts.Count);
            if (failure == "timeout") Assert.Equal(30000, boundary.Parent.WaitMilliseconds);
        }

        [Theory]
        [InlineData("--install-relaunch-after")]
        [InlineData("--install-relaunch-after nope 123456")]
        [InlineData("--install-relaunch-after 0 123456")]
        [InlineData("--install-relaunch-after 17 -1")]
        [InlineData("--install-relaunch-after 17 9223372036854775807")]
        [InlineData("--install-relaunch-after 17 123456 extra")]
        [InlineData("extra --install-relaunch-after 17 123456")]
        [InlineData("--install-relaunch-after 31 123456")]
        public void MalformedOrSelfHelperRequestsAreHandledWithoutNormalStartup(string arguments)
        {
            var boundary = new Processes { CurrentId = 31 };
            Assert.True(new InstallRelaunch(boundary).TryRunHelper(arguments.Split(' '), "app.exe", out int code));
            Assert.Equal(1, code);
            Assert.Empty(boundary.Starts);
            Assert.Equal(0, boundary.FindCalls);
        }

        [Fact]
        public void AlreadyExitedParentAllowsOneVisibleNormalLaunchAndOtherArgumentsPassThrough()
        {
            var boundary = new Processes { CurrentId = 31, MissingParent = true };
            var service = new InstallRelaunch(boundary);
            Assert.False(service.TryRunHelper(new[] { "--minimized" }, "app.exe", out _));
            Assert.Empty(boundary.Starts);
            Assert.True(service.TryRunHelper(new[] { "--install-relaunch-after", "17", "123456" },
                "app.exe", out int code));
            Assert.Equal(0, code);
            var normal = Assert.Single(boundary.Starts);
            Assert.False(normal.CreateNoWindow);
            Assert.Equal(ProcessWindowStyle.Normal, normal.WindowStyle);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("throw")]
        [InlineData("identity")]
        public void FailedHelperStartLeavesTheOldApplicationRunning(string failure)
        {
            var boundary = new Processes
            {
                StartSucceeds = failure != "null",
                StartThrows = failure == "throw",
                CurrentThrows = failure == "identity",
            };
            bool shutdown = false;
            Assert.False(new InstallRelaunch(boundary).StartInstalled("app.exe", () => shutdown = true));
            Assert.False(shutdown);
        }

        [Fact]
        public void RealHelperWaitsForOwnedParentExitBeforeClaimingItsMutex()
        {
            string root = Path.Combine(Path.GetTempPath(), "KillerNotesInstallRelaunchTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string executable = Path.Combine(root, "OwnedHandoff.exe");
            string marker = Path.Combine(root, "normal.txt");
            string helperMarker = Path.Combine(root, "helper.txt");
            string name = "Local\\InstallRelaunch-" + Guid.NewGuid().ToString("N");
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, name + "-ready");
            using var release = new EventWaitHandle(false, EventResetMode.ManualReset, name + "-release");
            using var provider = new CSharpCodeProvider();
            var parameters = new CompilerParameters(new[] { "System.dll" }, executable)
            {
                GenerateExecutable = true,
                CompilerOptions = "/target:winexe /platform:x64",
                TempFiles = new TempFileCollection(root),
            };
            var compiled = provider.CompileAssemblyFromSource(parameters, OwnedChild);
            Assert.False(compiled.Errors.HasErrors, string.Join("\n",
                compiled.Errors.Cast<CompilerError>().Select(error => error.ToString())));
            var start = new ProcessStartInfo(executable, "--parent") { UseShellExecute = false, CreateNoWindow = true };
            start.EnvironmentVariables["INSTALL_RELAUNCH_ASSEMBLY"] = typeof(InstallRelaunch).Assembly.Location;
            start.EnvironmentVariables["INSTALL_RELAUNCH_NAME"] = name;
            start.EnvironmentVariables["INSTALL_RELAUNCH_MARKER"] = marker;
            start.EnvironmentVariables["INSTALL_RELAUNCH_HELPER"] = helperMarker;
            using var parent = Process.Start(start);
            Assert.NotNull(parent);
            try
            {
                Assert.True(ready.WaitOne(10000), "Owned parent did not reach its shutdown boundary.");
                Assert.True(SpinWait.SpinUntil(() => File.Exists(helperMarker), 10000), "Owned helper did not start.");
                Assert.False(SpinWait.SpinUntil(() => File.Exists(marker), 300), "Normal copy launched while its parent still held the mutex.");
                Assert.False(parent!.HasExited);
                release.Set();
                Assert.True(parent.WaitForExit(10000), "Owned parent did not exit.");
                Assert.Equal(0, parent.ExitCode);
                // The file exists from the moment the child opens it to write, so it is read only
                // once the write is complete: the old read here met the child's open handle and
                // failed with a sharing violation now and then.
                string written = "";
                Assert.True(SpinWait.SpinUntil(() => (written = TryReadShared(marker)).Length > 0, 10000),
                    "Installed copy did not launch after parent exit.");
                Assert.Equal("first", written);
            }
            finally
            {
                release.Set();
                if (parent != null && !parent.WaitForExit(10000)) { parent.Kill(); parent.WaitForExit(5000); }
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    try { Directory.Delete(root, true); return true; }
                    catch (IOException) { return false; }
                    catch (UnauthorizedAccessException) { return false; }
                }, 5000), "Owned handoff fixture files remained in use after process exit.");
            }
        }

        /// <summary>The file's text once another process has finished writing it, or "" while it is
        /// missing, empty or still held open for writing.</summary>
        private static string TryReadShared(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }

        private const string OwnedChild = @"
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
internal static class OwnedHandoff
{
    private static int Main(string[] args)
    {
        try
        {
            string name = Environment.GetEnvironmentVariable(""INSTALL_RELAUNCH_NAME"");
            var type = Assembly.LoadFrom(Environment.GetEnvironmentVariable(""INSTALL_RELAUNCH_ASSEMBLY""))
                .GetType(""KillerNotes.Services.InstallRelaunch"", true);
            object service = Activator.CreateInstance(type);
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            if (args.Length > 0 && args[0] == ""--parent"")
            {
                bool first;
                using (var held = new Mutex(true, name, out first))
                using (var ready = EventWaitHandle.OpenExisting(name + ""-ready""))
                using (var release = EventWaitHandle.OpenExisting(name + ""-release""))
                {
                    if (!first) return 31;
                    Action shutdown = () => { ready.Set(); if (!release.WaitOne(15000)) throw new Exception(""Fixture shutdown timed out.""); };
                    bool started = (bool)type.GetMethod(""StartInstalled"").Invoke(service, new object[] { exe, shutdown });
                    return started ? 0 : 32;
                }
            }
            if (args.Length > 0)
            {
                File.WriteAllText(Environment.GetEnvironmentVariable(""INSTALL_RELAUNCH_HELPER""), ""started"");
                object[] request = { args, exe, 1 };
                bool handled = (bool)type.GetMethod(""TryRunHelper"").Invoke(service, request);
                return handled ? (int)request[2] : 33;
            }
            bool claimed;
            using (var claim = new Mutex(true, name, out claimed))
                File.WriteAllText(Environment.GetEnvironmentVariable(""INSTALL_RELAUNCH_MARKER""), claimed ? ""first"" : ""blocked"");
            return 0;
        }
        catch { return 34; }
    }
}";

        [Fact]
        public void ParentExitBetweenLookupAndHandleStillAllowsNormalStartup()
        {
            using var child = Process.Start(new ProcessStartInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30") { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                string id = child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string created = child.StartTime.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture);
                var boundary = new ExitingProcesses(child);
                Assert.True(new InstallRelaunch(boundary).TryRunHelper(
                    new[] { "--install-relaunch-after", id, created }, @"C:\Owned\App.exe", out int code));
                Assert.True(child.HasExited);
                Assert.Equal(0, code);
                Assert.Equal(1, boundary.Starts);
            }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
        }

        private sealed class ExitingProcesses : IInstallRelaunchProcesses
        {
            private readonly InstallRelaunch.SystemProcesses _system;
            internal int Starts;
            internal ExitingProcesses(Process child)
            {
                _system = new InstallRelaunch.SystemProcesses(id =>
                {
                    Process stale = Process.GetProcessById(id);
                    child.Kill();
                    Assert.True(child.WaitForExit(5000));
                    return stale;
                });
            }
            public IInstallRelaunchProcess Current() => _system.Current();
            public IInstallRelaunchProcess? Find(int id) => _system.Find(id);
            public bool Start(ProcessStartInfo start) { Starts++; return true; }
        }

        private sealed class Processes : IInstallRelaunchProcesses
        {
            public readonly List<ProcessStartInfo> Starts = new List<ProcessStartInfo>();
            public readonly ParentProcess Parent = new ParentProcess();
            public int CurrentId = 17;
            public int FindCalls;
            public bool MissingParent, FindThrows, StartThrows, CurrentThrows;
            public bool StartSucceeds = true;
            public IInstallRelaunchProcess Current() => CurrentThrows
                ? throw new System.ComponentModel.Win32Exception(5) : new ParentProcess { Id = CurrentId };
            public IInstallRelaunchProcess? Find(int id)
            {
                FindCalls++;
                Assert.Equal(17, id);
                if (FindThrows) throw new System.ComponentModel.Win32Exception(5);
                return MissingParent ? null : Parent;
            }
            public bool Start(ProcessStartInfo start)
            {
                Starts.Add(start);
                if (StartThrows) throw new System.ComponentModel.Win32Exception(5);
                return StartSucceeds;
            }
        }

        private sealed class ParentProcess : IInstallRelaunchProcess
        {
            public int Id { get; set; } = 17;
            public long CreationFileTime { get; set; } = 123456;
            public bool Exited;
            public bool WaitThrows;
            public int WaitMilliseconds;
            public bool WaitForExit(int milliseconds)
            {
                WaitMilliseconds = milliseconds;
                if (WaitThrows) throw new System.ComponentModel.Win32Exception(5);
                return Exited;
            }
            public void Dispose() { }
        }
    }
}

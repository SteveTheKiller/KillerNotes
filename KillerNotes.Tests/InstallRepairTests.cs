using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using KillerNotes.Services;
using Microsoft.Win32;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Install, uninstall and command-line launch behavior that does not need a window.</summary>
    public sealed class InstallRepairTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

        public InstallRepairTests() => Directory.CreateDirectory(_root);

        public void Dispose() { try { Directory.Delete(_root, true); } catch { /* best effort */ } }

        [Fact]
        public void ARelativeCommandLinePathIsPassedOnAsAFullPath()
        {
            string file = Path.Combine(_root, "shared.knote");
            File.WriteAllText(file, "x");
            string relative = Uri.UnescapeDataString(new Uri(Environment.CurrentDirectory + "\\")
                .MakeRelativeUri(new Uri(file)).ToString()).Replace('/', '\\');
            Assert.False(Path.IsPathRooted(relative), relative);

            Assert.Equal(file, App.OpenFileArgument(new[] { relative }));
        }

        [Fact]
        public void AnIconOfTheSameSizeWithNewBytesIsRewritten()
        {
            string icon = Path.Combine(_root, "kn-note.ico");
            File.WriteAllBytes(icon, new byte[] { 1, 2, 3, 4 });

            App.WriteIconIfChanged(icon, new byte[] { 4, 3, 2, 1 });

            Assert.Equal(new byte[] { 4, 3, 2, 1 }, File.ReadAllBytes(icon));
        }

        [Fact]
        public void APerUserUninstallKeepsTheSettingsKey()
        {
            string path = @"Software\KillerNotesTests-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(path))
                {
                    key.SetValue("Installed", 1);
                    key.SetValue("InstallPath", @"C:\x\KillerNotes.exe");
                    key.SetValue("Version", "1.0.0");
                    using var settings = key.CreateSubKey("Settings");
                    settings.SetValue("DataFolder", @"D:\Notes");
                }

                App.RemoveInstallMarkers(Registry.CurrentUser, path, machine: false);

                using var after = Registry.CurrentUser.OpenSubKey(path);
                Assert.NotNull(after);
                Assert.Null(after!.GetValue("Installed"));
                Assert.Null(after.GetValue("InstallPath"));
                Assert.Null(after.GetValue("Version"));
                using var kept = after.OpenSubKey("Settings");
                Assert.Equal(@"D:\Notes", kept?.GetValue("DataFolder"));
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        }

        [Fact]
        public void AnElevatedUninstallReturnsWhatTheElevatedCopyReturned()
        {
            ProcessStartInfo? started = null;

            int code = App.RunElevatedUninstall(@"C:\Program Files\KillerNotes\KillerNotes.exe", silent: true, 77, info =>
            {
                started = info;
                return KillerFamily.Deployment.InstallerExitCodes.Busy;
            });

            Assert.Equal(KillerFamily.Deployment.InstallerExitCodes.Busy, code);
            Assert.Equal("runas", started!.Verb);
            Assert.Equal("/uninstall-silent /launcher:77", started.Arguments);
        }

        [Fact]
        public void ADeclinedElevationPromptReportsTheCancelledCode()
        {
            int code = App.RunElevatedUninstall("KillerNotes.exe", silent: true, 77,
                _ => throw new System.ComponentModel.Win32Exception(1223));

            Assert.Equal(KillerFamily.Deployment.InstallerExitCodes.Cancelled, code);
        }

        [Fact]
        public void AnElevatedUninstallThatCannotStartIsNotASuccess()
        {
            int code = App.RunElevatedUninstall("KillerNotes.exe", silent: true, 77,
                _ => throw new System.ComponentModel.Win32Exception(2));

            Assert.NotEqual(KillerFamily.Deployment.InstallerExitCodes.Success, code);
        }

        [Fact]
        public void AMachineUninstallRemovesTheWholeKey()
        {
            string path = @"Software\KillerNotesTests-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(path))
                    key.SetValue("Installed", 1);

                App.RemoveInstallMarkers(Registry.CurrentUser, path, machine: true);

                Assert.Null(Registry.CurrentUser.OpenSubKey(path));
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        }

        [Fact]
        public void InstallingOverItselfLeavesTheExecutableAlone()
        {
            string exe = Path.Combine(_root, "KillerNotes.exe");
            File.WriteAllText(exe, "installed executable");

            App.InstallExecutable(exe, exe);

            Assert.Equal("installed executable", File.ReadAllText(exe));
        }

        [Fact]
        public void AnInstallThatFailsPartwayKeepsTheWorkingExecutable()
        {
            string source = Path.Combine(_root, "portable.exe");
            string installed = Path.Combine(_root, "installed", "KillerNotes.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
            File.WriteAllText(source, "the new and longer executable");
            File.WriteAllText(installed, "old executable");
            var disk = new ExecutableDeployment.FileOperations
            {
                Copy = (input, output) => { output.WriteByte((byte)input.ReadByte()); throw new IOException("There is not enough space on the disk."); },
            };

            Assert.Throws<IOException>(() => App.InstallExecutable(source, installed, disk));

            Assert.Equal("old executable", File.ReadAllText(installed));
            Assert.Equal(new[] { installed }, Directory.GetFiles(Path.GetDirectoryName(installed)!));
        }

        [Fact]
        public void AnInstallReplacesTheExecutableWithTheNewOne()
        {
            string source = Path.Combine(_root, "portable.exe");
            string installed = Path.Combine(_root, "installed", "KillerNotes.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
            File.WriteAllText(source, "the new executable");
            File.WriteAllText(installed, "old executable");

            App.InstallExecutable(source, installed);

            Assert.Equal("the new executable", File.ReadAllText(installed));
            Assert.Equal(new[] { installed }, Directory.GetFiles(Path.GetDirectoryName(installed)!));
        }

        [Fact]
        public void AnInstallOverARunningCopyIsRefusedAsBusyAndChangesNothing()
        {
            string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            string running = Path.Combine(_root, "InstallBusy" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            File.Copy(shell, running);
            string source = Path.Combine(_root, "portable.exe");
            File.WriteAllText(source, "the new executable");
            byte[] before = File.ReadAllBytes(running);
            using var child = Process.Start(new ProcessStartInfo(running, "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30")
            { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                Assert.True(SpinWaitFor(() => App.IsRunningElsewhere(running)), "The copy did not start.");

                var error = Assert.Throws<System.ComponentModel.Win32Exception>(() => App.InstallExecutable(source, running));

                Assert.Equal(1618, error.NativeErrorCode);
                Assert.Equal(before, File.ReadAllBytes(running));
            }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
        }

        [Fact]
        public void ARunningCopyAtThatPathIsFound()
        {
            string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            using var child = Process.Start(new ProcessStartInfo(shell, "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30")
            { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                Assert.True(SpinWaitFor(() => App.IsRunningElsewhere(shell)), "A running copy was not found.");
                Assert.False(App.IsRunningElsewhere(Path.Combine(_root, "NotRunning", "powershell.exe")));
            }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
        }

        /// <summary>The unelevated copy that asks for elevation is the installed exe, and it stays alive
        /// waiting for the elevated copy's result, so counting it would make every all-users uninstall
        /// without admin rights call itself busy.</summary>
        [Fact]
        public void TheLauncherWaitingForTheElevatedCopyIsNotARunningCopy()
        {
            string exe = Path.Combine(_root, "launcher", "ping.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE"), exe);
            using var launcher = Process.Start(new ProcessStartInfo(exe, "-n 30 127.0.0.1")
            { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                Assert.True(SpinWaitFor(() => App.IsRunningElsewhere(exe)), "The waiting copy was not found.");
                Assert.False(App.IsRunningElsewhere(exe, launcher.Id));
            }
            finally { if (!launcher.HasExited) { launcher.Kill(); launcher.WaitForExit(5000); } }
        }

        [Theory]
        [InlineData("/uninstall /launcher:4242", 4242)]
        [InlineData("/uninstall-silent /launcher:7", 7)]
        [InlineData("/uninstall", null)]
        [InlineData("/uninstall /launcher:x", null)]
        public void TheElevatedCopyReadsWhoLaunchedIt(string args, int? expected) =>
            Assert.Equal(expected, App.LauncherId(args.Split(' ')));

        private static bool SpinWaitFor(Func<bool> condition) => System.Threading.SpinWait.SpinUntil(condition, 5000);

        [Fact]
        public void TheCleanupScriptRemovesAnInstallUnderANonAsciiPath()
        {
            string folder = Path.Combine(_root, "J\u00F6s\u00E9 M\u00FCller", "KillerNotes");
            Directory.CreateDirectory(Path.Combine(folder, "icons"));
            File.WriteAllText(Path.Combine(folder, "KillerNotes.exe"), "installed executable");
            File.WriteAllText(Path.Combine(folder, "icons", "kn-note.ico"), "icon");
            File.WriteAllText(Path.Combine(folder, "notes.db"), "kept");
            string script = UninstallScript.Build(folder, new[] { "KillerNotes.exe", @"icons\kn-note.ico" }, pings: 1);
            string bat = UninstallScript.Write(script, elevated: false);
            try
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c \"" + bat + "\"")
                { UseShellExecute = false, CreateNoWindow = true })!;
                Assert.True(process.WaitForExit(15000));

                Assert.False(File.Exists(Path.Combine(folder, "KillerNotes.exe")));
                Assert.False(File.Exists(Path.Combine(folder, "icons", "kn-note.ico")));
                Assert.Equal("kept", File.ReadAllText(Path.Combine(folder, "notes.db")));
            }
            finally { try { File.Delete(bat); } catch { /* best effort */ } }
        }

        /// <summary>The app starts this script and then finishes its own registry and shell work, which
        /// can outlast the script's opening wait, so a first delete that fails is not the last word.</summary>
        [Fact]
        public void TheCleanupScriptWaitsForAnExeThatIsStillInUse()
        {
            string folder = Path.Combine(_root, "InUse");
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, "KillerNotes.exe");
            File.WriteAllText(exe, "installed executable");
            File.SetAttributes(exe, FileAttributes.ReadOnly);
            string bat = UninstallScript.Write(UninstallScript.Build(folder, new[] { "KillerNotes.exe" }, pings: 1), elevated: false);
            try
            {
                Process process;
                using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    process = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c \"" + bat + "\"")
                    { UseShellExecute = false, CreateNoWindow = true })!;
                    System.Threading.Thread.Sleep(2500);
                }
                using (process) Assert.True(process.WaitForExit(15000));

                Assert.False(File.Exists(exe));
            }
            finally { try { File.Delete(bat); } catch { /* best effort */ } }
        }

        [Fact]
        public void EachUninstallScriptGetsAFreshRandomName()
        {
            string first = UninstallScript.Write("@echo off\r\n", elevated: false);
            string second = UninstallScript.Write("@echo off\r\n", elevated: false);
            try
            {
                Assert.NotEqual(first, second);
                Assert.NotEqual("killernotes_uninstall.bat", Path.GetFileName(first).ToLowerInvariant());
                Assert.True(File.Exists(first));
                Assert.True(File.Exists(second));
            }
            finally
            {
                try { File.Delete(first); File.Delete(second); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void TheElevatedScriptFolderIsNewRandomAndAdministratorsOnly()
        {
            string first = UninstallScript.CreateAdminOnlyFolder(_root);
            string second = UninstallScript.CreateAdminOnlyFolder(_root);

            Assert.NotEqual(first, second);
            foreach (string folder in new[] { first, second })
            {
                Assert.Equal(_root, Path.GetDirectoryName(folder));
                Assert.Matches("^KillerTools-[0-9a-f]{32}$", Path.GetFileName(folder));
                var security = new DirectoryInfo(folder).GetAccessControl();
                Assert.True(security.AreAccessRulesProtected, "The folder inherits permissions from its parent.");
                var allowed = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                    .Where(rule => rule.AccessControlType == AccessControlType.Allow)
                    .Select(rule => rule.IdentityReference.Value).Distinct().OrderBy(sid => sid).ToArray();
                Assert.Equal(new[] { "S-1-5-18", "S-1-5-32-544" }, allowed);
            }
        }
    }
}

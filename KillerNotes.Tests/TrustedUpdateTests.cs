using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class TrustedUpdateTests
    {
        // Text inside a C# string is checked by nothing until a real update runs it, and a syntax
        // error there would strand someone mid-update. This is Windows PowerShell's own parser.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TheGeneratedHelperParsesInWindowsPowerShell(bool elevated)
        {
            string folder = Path.Combine(Path.GetTempPath(), "KillerNotes-helper-parse-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string download = Path.Combine(folder, "update.exe");
                UpdateImagePublication.RememberVerified(download, new string('0', 64), new string('B', 40));
                string script = Path.Combine(folder, "helper.ps1");
                File.WriteAllText(script, UpdateImagePublication.Script(download, Path.Combine(folder, "app it.exe"),
                    elevated, "https://example.invalid/releases", "KillerNotes", "9.9.9", "HKLM"), new UTF8Encoding(true));
                var check = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"$e=$null;$null=[Management.Automation.Language.Parser]::ParseFile('"
                    + script + "',[ref]$null,[ref]$e);$e | ForEach-Object { $_.Message };'errors=' + @($e).Count\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var process = Process.Start(check)!;
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                Assert.EndsWith("errors=0", output.Trim());
            }
            finally { Directory.Delete(folder, true); }
        }

        // The whole helper travels on the command line, and a helper that outgrows it throws
        // instead of updating. Long install paths appear several times over, so leave real room.
        [Fact]
        public void TheElevatedHelperLeavesRoomOnTheCommandLine()
        {
            string folder = Path.Combine(Path.GetTempPath(), "KillerNotes-helper-size-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string deep = Path.Combine(folder, new string('n', 120));
                Directory.CreateDirectory(deep);
                string staged = Path.Combine(deep, "KillerNotes-update.exe");
                UpdateImagePublication.RememberVerified(staged, new string('0', 64), new string('B', 40));
                string arguments = TrustedUpdate.Publication(staged, Path.Combine(deep, "KillerNotes.exe"), "9.9.9",
                    new string('0', 64), 1, 1, 2, 2).Arguments;
                Assert.True(arguments.Length < 20000, "Helper is " + arguments.Length);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void TheHelperRunsAsPassedAndKeepsItsExitCode()
        {
            string folder = Path.Combine(Path.GetTempPath(), "KillerNotes helper run " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string marker = Path.Combine(folder, "it's done.txt");
                string script = "[IO.File]::WriteAllText('" + marker.Replace("'", "''") + "','quote \" and \u00e9 and ' + $env:PSModulePath)\nexit 7";
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    ImmutablePowerShell.Arguments(script)) { UseShellExecute = false, CreateNoWindow = true };
                using var process = Process.Start(start)!;
                process.WaitForExit();

                Assert.Equal(7, process.ExitCode);
                string written = File.ReadAllText(marker);
                Assert.StartsWith("quote \" and \u00e9 and ", written);
                Assert.DoesNotContain("Documents", written);
            }
            finally { Directory.Delete(folder, true); }
        }

        // The helper reaches the wait just as the app closes, so a process can vanish between the
        // lookup and the start-time read. The System process (id 4) is found by id but yields no
        // start time to a normal user, which fails the same read; the wait must carry on, not abort
        // an update that is otherwise good.
        [Fact]
        public void WaitingOnAProcessWhoseStartTimeCannotBeReadDoesNotAbortTheHelper()
        {
            string script = "$ErrorActionPreference='Stop';" + TrustedUpdate.Wait(4, 1, "exit 1618") + "exit 0";
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                ImmutablePowerShell.Arguments(script)) { UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(start)!;
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
        }

        [Fact]
        public void WaitStillWaitsForTheProcessWithTheMatchingStartTime()
        {
            string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            using var target = Process.Start(new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 6")
            { UseShellExecute = false, CreateNoWindow = true })!;
            long ticks = target.StartTime.ToUniversalTime().Ticks;
            string script = "$ErrorActionPreference='Stop';" + TrustedUpdate.Wait(target.Id, ticks, "exit 1618") + "exit 0";
            using var helper = Process.Start(new ProcessStartInfo(powershell, ImmutablePowerShell.Arguments(script))
            { UseShellExecute = false, CreateNoWindow = true })!;
            helper.WaitForExit();
            bool targetGone = target.HasExited;
            if (!targetGone) target.Kill();

            Assert.Equal(0, helper.ExitCode);
            Assert.True(targetGone, "The helper returned while the process it was waiting for still ran.");
        }

        [Fact]
        public void InvalidApplyRequestIsRejected()
        {
            Assert.Equal(1625, TrustedUpdate.Apply(new[] { "/apply-verified-update" }));
        }

        [Fact]
        public void UntrustedInheritedWritePermissionsAreRejected()
        {
            var security = new DirectorySecurity();
            security.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"),
                FileSystemRights.WriteData, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.InheritOnly, AccessControlType.Allow));
            Assert.Throws<SecurityException>(() => TrustedUpdate.ValidateMachineOwner(security));
        }

        [Fact]
        public void TrustedOwnerAndReadOnlyUsersAreAccepted()
        {
            var security = new DirectorySecurity();
            security.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"),
                FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            TrustedUpdate.ValidateMachineOwner(security, new SecurityIdentifier("S-1-5-32-544"));
            Assert.Throws<SecurityException>(() => TrustedUpdate.ValidateMachineOwner(security,
                new SecurityIdentifier("S-1-5-32-545")));
        }
        [Fact]
        public void NonwritableUntrustedProcessIsRejectedBeforeAnyLaunch()
        {
            string folder = Path.Combine(Path.GetTempPath(), "KillerNotes-trusted-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string download = Path.Combine(folder, "update.exe");
            File.WriteAllText(download, "retained download");
            UpdateImagePublication.RememberVerified(download, new string('0', 64), new string('B', 40));
            int launches = 0;
            try
            {
                Assert.Throws<SecurityException>(() => UpdateService.StartSwap(download, "9.9.9", new UpdateService.Platform
                {
                    CanWrite = _ => false,
                    Start = _ => { launches++; return null; }
                }));
                Assert.Equal(0, launches);
                Assert.Equal("retained download", File.ReadAllText(download));
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}

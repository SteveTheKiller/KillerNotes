using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The elevated uninstall never resolves cmd, its AutoRun, or the commands in its script
    /// through a folder the signed-in user can write.</summary>
    public sealed class UninstallStartTests
    {
        private static readonly string System32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

        [Fact]
        public void TheCleanupScriptStartsTheSystemCmdWithoutAutoRunFromSystem32()
        {
            var start = App.UninstallCleanupStart(@"C:\Windows\Temp\KillerTools-x\killernotes_uninstall_y.bat");

            Assert.Equal(Path.Combine(System32, "cmd.exe"), start.FileName, ignoreCase: true);
            Assert.StartsWith("/d /c ", start.Arguments);
            Assert.Contains("\"C:\\Windows\\Temp\\KillerTools-x\\killernotes_uninstall_y.bat\"", start.Arguments);
            Assert.Equal(System32, start.WorkingDirectory, ignoreCase: true);
        }

        [Fact]
        public void TheElevatedUninstallStartsInSystem32()
        {
            ProcessStartInfo? started = null;
            App.RunElevatedUninstall(@"C:\Program Files\KillerNotes\KillerNotes.exe", silent: true, 77, info =>
            {
                started = info;
                return KillerFamily.Deployment.InstallerExitCodes.Success;
            });

            Assert.Equal(System32, started!.WorkingDirectory, ignoreCase: true);
        }

        [Fact]
        public void TheScriptCallsItsExternalCommandsByFullPath()
        {
            string script = UninstallScript.Build(@"C:\Program Files\KillerNotes", new[] { "KillerNotes.exe" }, pings: 3);
            // The script's own opening lines; the removal block after them comes from the shared kit
            // (InstalledFileCleanup), whose retry ping is covered by starting cmd in System32.
            var lines = script.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray();

            Assert.DoesNotContain(lines, l => l.StartsWith("ping ", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(lines, l => l.StartsWith("chcp ", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, l => l.StartsWith("\"" + Path.Combine(System32, "PING.EXE") + "\" -n 3 ", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, l => l.StartsWith("\"" + Path.Combine(System32, "chcp.com") + "\" 65001", StringComparison.OrdinalIgnoreCase));
        }
    }
}

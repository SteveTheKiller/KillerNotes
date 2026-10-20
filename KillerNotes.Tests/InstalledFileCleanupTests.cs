using System;
using System.IO;
using KillerFamily.Deployment;
using Xunit;

namespace KillerNotes.Tests
{
    public class InstalledFileCleanupTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UninstallCleanupPreservesUserFilesAtRootAndInDataFolder(bool deferred)
        {
            string root = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            try
            {
                File.WriteAllText(Path.Combine(root, "KillerNotes.exe"), "installed executable");
                File.WriteAllText(Path.Combine(root, "notes.db"), "root database");
                File.WriteAllText(Path.Combine(root, "Data", "notes.db"), "nested database");
                File.WriteAllText(Path.Combine(root, "sentinel.txt"), "user file");
                if (deferred)
                {
                    string batch = Path.Combine(root, "cleanup.bat");
                    File.WriteAllText(batch, "@echo off\r\n" + InstalledFileCleanup.RemovalBatch(root, "KillerNotes.exe") + "exit /b 0\r\n");
                    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "cmd.exe", "/d /c \"" + batch + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    Assert.True(process!.WaitForExit(5000));
                    Assert.Equal(0, process.ExitCode);
                }
                else InstalledFileCleanup.Remove(root, "KillerNotes.exe");
                Assert.Equal("root database", File.ReadAllText(Path.Combine(root, "notes.db")));
                Assert.Equal("nested database", File.ReadAllText(Path.Combine(root, "Data", "notes.db")));
                Assert.Equal("user file", File.ReadAllText(Path.Combine(root, "sentinel.txt")));
                Assert.False(File.Exists(Path.Combine(root, "KillerNotes.exe")));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}

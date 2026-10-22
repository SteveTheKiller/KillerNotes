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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PublicationWaitsForOwnedProcessAndPreservesChangedTarget(bool changed)
        {
            string root = Path.Combine(Path.GetTempPath(), "KillerNotes-trusted-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string target = Path.Combine(root, "target.exe"), staged = Path.Combine(root, "staged.exe");
                byte[] original = Encoding.UTF8.GetBytes("original image");
                byte[] replacement = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
                File.WriteAllBytes(target, original);
                File.WriteAllBytes(staged, replacement);
                UpdateImagePublication.RememberVerified(staged, Hash(replacement));
                string marker = Path.Combine(root, "relaunched.txt");
                string metadata = Path.Combine(root, "metadata.txt");
                string overrides = "$ops.Metadata = { param($plan) [IO.File]::WriteAllText('" + metadata + "', $plan.Hive + ':' + $plan.Version) }\n"
                    + "$ops.Relaunch = { param($plan) [IO.File]::WriteAllText('" + marker + "', 'yes') }\n"
                    + "$ops.Releases = { param($url) }";
                using var owner = Process.Start(UpdateHelperLaunch.StartInfo("Start-Sleep -Seconds 3"))!;
                var start = TrustedUpdate.Publication(staged, target, "9.9.9", changed ? new string('0', 64) : Hash(original),
                    owner.Id, owner.StartTime.ToUniversalTime().Ticks, int.MaxValue, 0, overrides);
                Assert.False(start.UseShellExecute);
                Assert.True(start.CreateNoWindow);
                Assert.Equal("", start.Verb);
                using var helper = Process.Start(start)!;
                Assert.False(helper.WaitForExit(300));
                Assert.Equal(original, File.ReadAllBytes(target));
                Assert.True(helper.WaitForExit(20000));
                Assert.True(owner.HasExited);
                Assert.Equal(changed ? 1 : 0, helper.ExitCode);
                Assert.Equal(changed ? original : replacement, File.ReadAllBytes(target));
                Assert.Equal(!changed, File.Exists(marker));
                if (!changed) Assert.Equal("HKLM:9.9.9", File.ReadAllText(metadata));
                else Assert.True(File.Exists(staged));
            }
            finally { Directory.Delete(root, true); }
        }

        private static string Hash(byte[] bytes)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        [Fact]
        public void NonwritableUntrustedProcessIsRejectedBeforeAnyLaunch()
        {
            string folder = Path.Combine(Path.GetTempPath(), "KillerNotes-trusted-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string download = Path.Combine(folder, "update.exe");
            File.WriteAllText(download, "retained download");
            UpdateImagePublication.RememberVerified(download, new string('0', 64));
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

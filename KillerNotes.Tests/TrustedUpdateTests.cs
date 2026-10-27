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

using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using KillerFamily.Deployment;
using Microsoft.Win32;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class DeploymentOwnershipRegressionTests
    {
        [Fact]
        public void InteractiveInstallReleasesItsTargetWhenTheOperationFails()
        {
            string target = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
            Assert.Throws<IOException>(() => App.RunInteractiveInstall(target, () => throw new IOException("copy failed")));
            bool completed = false;
            Exception? failure = null;
            var next = new Thread(() =>
            {
                try { App.RunInteractiveInstall(target, () => completed = true); }
                catch (Exception error) { failure = error; }
            });
            next.Start();
            Assert.True(next.Join(10000));
            Assert.Null(failure);
            Assert.True(completed);
        }

        [Fact]
        public void InteractiveInstallRejectsAHeldTargetBeforeWritingFilesOrRegistry()
        {
            string target = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
            string registryPath = @"Software\KillerNotesTests\" + Guid.NewGuid().ToString("N");
            using var ready = new ManualResetEvent(false);
            using var release = new ManualResetEvent(false);
            Exception? holderFailure = null;
            var holder = new Thread(() =>
            {
                try
                {
                    using var lease = InstallerExitCodes.Acquire("KillerNotes", target);
                    ready.Set();
                    release.WaitOne();
                }
                catch (Exception error) { holderFailure = error; ready.Set(); }
            });
            holder.Start();
            try
            {
                Assert.True(ready.WaitOne(10000));
                Assert.Null(holderFailure);
                var error = Assert.Throws<Win32Exception>(() => App.RunInteractiveInstall(target, () =>
                {
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "published.exe"), "replacement");
                    using var registration = Registry.CurrentUser.CreateSubKey(registryPath);
                    registration.SetValue("Installed", 1);
                }));
                Assert.Equal(InstallerExitCodes.Busy, error.NativeErrorCode);
                Assert.False(Directory.Exists(target));
                using var registration = Registry.CurrentUser.OpenSubKey(registryPath);
                Assert.Null(registration);
            }
            finally
            {
                release.Set();
                Assert.True(holder.Join(10000));
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Registry.CurrentUser.DeleteSubKeyTree(registryPath, false);
            }
        }
    }
}

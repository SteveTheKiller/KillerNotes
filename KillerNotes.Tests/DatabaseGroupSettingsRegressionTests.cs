using System;
using System.IO;
using System.Runtime.InteropServices;
using KillerNotes.Services;
using KillerNotes.Shell;
using Microsoft.Win32;
using Xunit;

namespace KillerNotes.Tests
{
    [CollectionDefinition("Database group settings isolation", DisableParallelization = true)]
    public sealed class DatabaseGroupSettingsCollection { }

    [Collection("Database group settings isolation")]
    public sealed class DatabaseGroupSettingsRegressionTests
    {
        [Theory]
        [InlineData("TemplatesGroup")]
        [InlineData("DailyGroup")]
        public void SameFilenameInAnotherFolderCannotInheritLegacyGroup(string setting)
        {
            using var settings = new IsolatedSettings();
            using var store = new TempStore();
            NoteStore.Close();
            NoteStore.DemoDbFile = null;
            App.SetSetting("ActiveDatabase", "test.db");
            App.SetSetting(setting, "test.db|Work");
            NoteStore.DbDirOverride = Path.Combine(NoteStore.DbDir, "other");
            NoteStore.Open();

            MainWindow.RepointGroupSettings("Work", "Foreign");

            Assert.Equal("test.db|Work", App.GetSetting(setting));
        }

        [Theory]
        [InlineData("TemplatesGroup")]
        [InlineData("DailyGroup")]
        public void ProvenPathMigratesAndFollowsMoveAndPasswordChange(string setting)
        {
            using var settings = new IsolatedSettings();
            using var store = new TempStore();
            NoteStore.Close();
            NoteStore.DemoDbFile = null;
            App.SetSetting("ActiveDatabase", "test.db");
            NoteStore.Open();
            App.SetSetting(setting, NoteStore.DbPath + "|Work");
            MainWindow.RepointGroupSettings("Work", "Job");
            Assert.Equal("id:" + NoteStore.DatabaseId + "|Job", App.GetSetting(setting));

            string originalPath = NoteStore.DbPath;
            string otherFolder = Path.Combine(NoteStore.DbDir, "moved");
            Directory.CreateDirectory(otherFolder);
            NoteStore.Close();
            File.Move(originalPath, Path.Combine(otherFolder, "renamed.db"));
            NoteStore.DbDirOverride = otherFolder;
            App.SetSetting("ActiveDatabase", "renamed.db");
            NoteStore.Open();
            MainWindow.RepointGroupSettings("Job", "Moved");
            Assert.Equal("id:" + NoteStore.DatabaseId + "|Moved", App.GetSetting(setting));

            NoteStore.SetPassword("test-password");
            MainWindow.RepointGroupSettings("Moved", "Protected");
            Assert.Equal("id:" + NoteStore.DatabaseId + "|Protected", App.GetSetting(setting));

            string? protectedSetting = App.GetSetting(setting);
            NoteStore.Close();
            NoteStore.DbDirOverride = Path.GetDirectoryName(originalPath);
            App.SetSetting("ActiveDatabase", "test.db");
            NoteStore.Open();
            MainWindow.RepointGroupSettings("Protected", "Foreign");
            Assert.Equal(protectedSetting, App.GetSetting(setting));
        }

        [Fact]
        public void MissingIdentityUsesCanonicalPathAndUpgradesWhenIdentityBecomesAvailable()
        {
            using var settings = new IsolatedSettings();
            using var store = new TempStore();
            string folder = NoteStore.DbDir;
            string path = NoteStore.DbPath;
            NoteStore.Close();
            NoteStore.DemoDbFile = null;
            App.SetSetting("ActiveDatabase", "test.db");
            App.SetSetting("TemplatesGroup", "path:" + path + "|Work");
            NoteStore.DbDirOverride = Path.Combine(folder, "unused", "..");

            MainWindow.RepointGroupSettings("Work", "Job");
            Assert.Equal("path:" + path + "|Job", App.GetSetting("TemplatesGroup"));

            NoteStore.DbDirOverride = Path.Combine(folder, "other");
            MainWindow.RepointGroupSettings("Job", "Foreign");
            Assert.Equal("path:" + path + "|Job", App.GetSetting("TemplatesGroup"));

            NoteStore.DbDirOverride = folder;
            NoteStore.Open();
            MainWindow.RepointGroupSettings("Job", "Identified");
            Assert.Equal("id:" + NoteStore.DatabaseId + "|Identified", App.GetSetting("TemplatesGroup"));
        }

        private sealed class IsolatedSettings : IDisposable
        {
            private static readonly IntPtr CurrentUser = new(unchecked((int)0x80000001));
            private readonly string path = @"Software\KillerNotesTests\" + Guid.NewGuid().ToString("N");
            private readonly RegistryKey key;

            public IsolatedSettings()
            {
                key = Registry.CurrentUser.CreateSubKey(path);
                int result = RegOverridePredefKey(CurrentUser, key.Handle.DangerousGetHandle());
                if (result != 0) throw new System.ComponentModel.Win32Exception(result);
            }

            public void Dispose()
            {
                int result = RegOverridePredefKey(CurrentUser, IntPtr.Zero);
                key.Dispose();
                if (result != 0) throw new System.ComponentModel.Win32Exception(result);
                Registry.CurrentUser.DeleteSubKeyTree(path);
            }

            [DllImport("advapi32.dll")]
            private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);
        }
    }
}

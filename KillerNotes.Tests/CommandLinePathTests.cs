using System;
using System.IO;
using KillerNotes.Services;
using Microsoft.Win32;
using Xunit;

namespace KillerNotes.Tests;

public sealed class CommandLinePathTests
{
    [Fact]
    public void AddsTheInstallFolderOnceWhateverItsSpelling()
    {
        string folder = Path.Combine(Path.GetTempPath(), "KillerNotes Test");
        string path = @"C:\Windows;C:\Tools";

        string added = CommandLinePath.WithEntry(path, folder);

        Assert.Equal(path + ";" + folder, added);
        Assert.Equal(added, CommandLinePath.WithEntry(added, folder + "\\"));
        Assert.Equal(added, CommandLinePath.WithEntry(added, folder.ToUpperInvariant()));
        string quoted = path + ";\"" + folder + "\"";
        Assert.Equal(quoted, CommandLinePath.WithEntry(quoted, folder));
    }

    [Fact]
    public void RemovesEverySpellingOfTheInstallFolderAndKeepsTheRest()
    {
        string folder = Path.Combine(Path.GetTempPath(), "KillerNotes Test");
        string path = @"%SystemRoot%\system32;" + folder + ";C:\\Tools;\"" + folder.ToUpperInvariant() + "\\\"";

        Assert.Equal(@"%SystemRoot%\system32;C:\Tools", CommandLinePath.WithoutEntry(path, folder));
        Assert.Equal(@"C:\Windows;;C:\Tools", CommandLinePath.WithoutEntry(@"C:\Windows;;C:\Tools", folder));
    }

    [Fact]
    public void KeepsUnexpandedEntriesAndTheRegistryTypeOfPath()
    {
        string name = @"Software\KillerNotes-PathTest-" + Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Path.GetTempPath(), "KillerNotes Test");
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(name, writable: true);
            key.SetValue("Path", @"%USERPROFILE%\bin", RegistryValueKind.ExpandString);

            Assert.True(CommandLinePath.AddTo(key, folder));
            Assert.False(CommandLinePath.AddTo(key, folder));
            Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("Path"));
            Assert.Equal(@"%USERPROFILE%\bin;" + folder,
                key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames));

            Assert.True(CommandLinePath.RemoveFrom(key, folder));
            Assert.False(CommandLinePath.RemoveFrom(key, folder));
            Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("Path"));
            Assert.Equal(@"%USERPROFILE%\bin",
                key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames));

            key.DeleteValue("Path");
            Assert.False(CommandLinePath.RemoveFrom(key, folder));
            Assert.Null(key.GetValue("Path"));
            Assert.True(CommandLinePath.AddTo(key, folder));
            Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("Path"));
            Assert.Equal(folder, key.GetValue("Path"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }
}

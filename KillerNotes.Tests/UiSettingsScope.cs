using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KillerNotes.Tests
{
    // UI fixtures run in a serialized collection because the registry override is process-wide.
    internal sealed class UiSettingsScope : IDisposable
    {
        private static readonly IntPtr CurrentUser = new(unchecked((int)0x80000001));
        private readonly string _path = @"Software\KillerNotesTests\" + Guid.NewGuid().ToString("N");
        private readonly RegistryKey _key;

        public UiSettingsScope()
        {
            _key = Registry.CurrentUser.CreateSubKey(_path);
            int result = RegOverridePredefKey(CurrentUser, _key.Handle.DangerousGetHandle());
            if (result != 0) throw new System.ComponentModel.Win32Exception(result);
            App.SetSetting("QuickNoteHotkey", "");
            App.SetSetting("ShowWindowHotkey", "");
        }

        public void Dispose()
        {
            int result = RegOverridePredefKey(CurrentUser, IntPtr.Zero);
            _key.Dispose();
            if (result != 0) throw new System.ComponentModel.Win32Exception(result);
            Registry.CurrentUser.DeleteSubKeyTree(_path);
        }

        [DllImport("advapi32.dll")]
        private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);
    }
}

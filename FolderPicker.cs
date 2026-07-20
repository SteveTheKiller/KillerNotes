using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KillerNotes
{
    // Modern folder picker (Vista+ IFileOpenDialog with FOS_PICKFOLDERS). WPF only
    // ships file open/save dialogs, and referencing WinForms for its FolderBrowserDialog
    // would pull a whole assembly in for one window - this is plain COM interop with no
    // new dependencies, and it gets the real Explorer-style dialog rather than the old
    // tree picker. Used by Manage databases > Change data folder (#6).
    internal static class FolderPicker
    {
        /// <summary>Shows the picker. Returns the chosen folder path, or null on cancel.</summary>
        public static string? Show(Window? owner, string? initialDir, string title)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                dialog.GetOptions(out uint opts);
                dialog.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
                dialog.SetTitle(title);
                if (!string.IsNullOrEmpty(initialDir) &&
                    SHCreateItemFromParsingName(initialDir!, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem? start) == 0 &&
                    start != null)
                    dialog.SetFolder(start);

                IntPtr hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                if (dialog.Show(hwnd) != 0) return null;   // cancelled (HRESULT_FROM_WIN32(ERROR_CANCELLED))

                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr pszPath);
                try { return Marshal.PtrToStringUni(pszPath); }
                finally { Marshal.FreeCoTaskMem(pszPath); }
            }
            finally { Marshal.ReleaseComObject(dialog); }
        }

        private const uint FOS_PICKFOLDERS     = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint SIGDN_FILESYSPATH   = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem? ppv);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW { }

        // IFileDialog vtable subset (through IFileOpenDialog's base). Member ORDER is the
        // COM contract - do not reorder or remove entries above the ones we call.
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr hwndParent);                       // IModalWindow
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);         // IFileDialog...
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid([MarshalAs(UnmanagedType.LPStruct)] Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid,
                [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}

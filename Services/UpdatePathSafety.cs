using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KillerNotes.Services
{
    /// <summary>Pins update paths against ancestor replacement.</summary>
    internal static class UpdatePathSafety
    {
        /// <summary>Keep every existing directory ancestor immovable during update validation.</summary>
        internal static IDisposable PinDirectory(string directory)
        {
            var lease = new DirectoryLease();
            try
            {
                var ancestors = new Stack<string>();
                for (string? item = Path.GetFullPath(directory); item != null; item = Path.GetDirectoryName(item))
                    ancestors.Push(item);
                foreach (string item in ancestors)
                {
                    // Deny delete sharing so existing ancestors cannot be renamed out from
                    // under validation. Write sharing permits our owned child-file renames.
                    var handle = CreateFile(item, 0x80000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                    if (handle.IsInvalid)
                    {
                        int code = Marshal.GetLastWin32Error();
                        handle.Dispose();
                        if (code == 2 || code == 3) continue;
                        throw new Win32Exception(code);
                    }
                    lease.Handles.Add(handle);
                    FileInformation information;
                    if (!GetFileInformationByHandle(handle, out information))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if ((information.Attributes & 0x400) != 0 || (information.Attributes & 0x10) == 0)
                        throw new UnauthorizedAccessException("Updates cannot traverse a linked installation path: " + item);
                }
                return lease;
            }
            catch { lease.Dispose(); throw; }
        }

        private sealed class DirectoryLease : IDisposable
        {
            internal readonly List<SafeFileHandle> Handles = new List<SafeFileHandle>();
            public void Dispose()
            {
                for (int index = Handles.Count - 1; index >= 0; index--) Handles[index].Dispose();
                Handles.Clear();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
            IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    }
}

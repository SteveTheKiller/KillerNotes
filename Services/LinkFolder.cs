using System;
using System.Runtime.InteropServices;

namespace KillerNotes.Services
{
    /// <summary>Tells a junction or symbolic link from the other things that carry the reparse-point
    /// attribute. An online-only OneDrive folder carries it too, and that one holds real files.</summary>
    internal static class LinkFolder
    {
        private const uint MountPoint = 0xA0000003, SymbolicLink = 0xA000000C;

        internal static bool IsLinkTag(uint tag) => tag is MountPoint or SymbolicLink;

        /// <summary>A folder whose tag cannot be read counts as a link, so a walk does not follow it.</summary>
        internal static bool IsLink(string folder)
        {
            IntPtr find = FindFirstFileW(folder, out var data);
            if (find == new IntPtr(-1)) return true;
            FindClose(find);
            return IsLinkTag(data.dwReserved0);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FindData
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime;
            public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string path, out FindData data);

        [DllImport("kernel32.dll")]
        private static extern bool FindClose(IntPtr find);
    }
}

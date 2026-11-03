using System;
using System.Runtime.InteropServices;

namespace LockPad.Core
{
    /// <summary>
    /// Detects a notebook path that is itself a file symbolic link (RT-M2). Saving through such a path can never work:
    /// File.Replace (ReplaceFile) fails with ERROR_SYMLINK_NOT_SUPPORTED, so the notebook is opened read-only instead of
    /// letting the user make edits that cannot be saved. Only IO_REPARSE_TAG_SYMLINK counts: OneDrive / cloud-files
    /// placeholders and other reparse points stay writable. A junction or link in a parent directory is not checked.
    /// </summary>
    internal static class FileLinks
    {
        private const uint FileAttributeReparsePoint = 0x400;
        private const uint IoReparseTagSymlink = 0xA000000C;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Win32FindData
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0; // the reparse tag when FILE_ATTRIBUTE_REPARSE_POINT is set
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string lpFileName, out Win32FindData lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindClose(IntPtr hFindFile);

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        internal enum LinkCheck { NotALink, SymbolicLink, CannotVerify }

        /// <summary>
        /// SymbolicLink when <paramref name="fullPath"/> (a full local path) is a symbolic link; FindFirstFile reports
        /// the link itself, not its target. CannotVerify for a path containing '?' or '*': FindFirstFile would treat
        /// them as wildcards, so it is never called and the caller opens the notebook read-only (defence in depth:
        /// LocalPath already refuses the \\?\ prefix, the only realistic source of a '?'). NotALink otherwise, also
        /// when the file cannot be queried (missing, too long): such a path is left to the normal open and save checks.
        /// </summary>
        internal static LinkCheck Check(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return LinkCheck.NotALink;
            if (fullPath.IndexOfAny(new[] { '*', '?' }) >= 0) return LinkCheck.CannotVerify;
            Win32FindData data;
            IntPtr handle = FindFirstFileW(fullPath, out data);
            if (handle == InvalidHandleValue || handle == IntPtr.Zero) return LinkCheck.NotALink;
            FindClose(handle);
            return IsSymbolicLinkTag(data.dwFileAttributes, data.dwReserved0) ? LinkCheck.SymbolicLink : LinkCheck.NotALink;
        }

        /// <summary>The WIN32_FIND_DATA test: the reparse attribute is set and the tag is IO_REPARSE_TAG_SYMLINK
        /// (dwReserved0 is only defined when the attribute is set).</summary>
        internal static bool IsSymbolicLinkTag(uint attributes, uint reparseTag)
        {
            return (attributes & FileAttributeReparsePoint) != 0 && reparseTag == IoReparseTagSymlink;
        }
    }
}

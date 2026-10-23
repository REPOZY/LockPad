using System;
using System.IO;

namespace LockPad.Core
{
    public static class LocalPath
    {
        private const string NotLocalMessage = "Only local file paths are allowed.";

        /// <summary>
        /// Returns the full path, or throws <see cref="NonLocalPathException"/> for anything that is not a file on a local
        /// drive: URLs, device-namespace paths, UNC paths (<c>\\server\share</c>, either slash direction) and paths on a
        /// mapped network drive (user decision 2026-10-18: local drives only). Runs before any file-system access to the
        /// path: the drive type (GetDriveType on the drive root, a local lookup) of the input's drive, or of the current
        /// directory for a relative or root-relative input, is checked before GetFullPath, which may expand 8.3 short
        /// names through the file system. The full path is checked again afterwards.
        /// </summary>
        public static string RequireLocalFile(string path)
        {
            return RequireLocalFile(path, DriveTypeOfRoot);
        }

        /// <summary>As <see cref="RequireLocalFile(string)"/>, with the drive-type lookup injected (test seam). The lookup
        /// receives the drive root of the full path (e.g. <c>Z:\</c>) and is never called for a UNC path.</summary>
        internal static string RequireLocalFile(string path, Func<string, DriveType> driveTypeOf)
        {
            return RequireLocalFile(path, driveTypeOf, Directory.GetCurrentDirectory, Path.GetFullPath);
        }

        /// <summary>As above, with the current-directory read and the full-path expansion injected too (test seams that
        /// let tests record the order of the drive-type lookups and the expansion).</summary>
        internal static string RequireLocalFile(string path, Func<string, DriveType> driveTypeOf,
            Func<string> currentDirectory, Func<string, string> getFullPath)
        {
            if (driveTypeOf == null) throw new ArgumentNullException(nameof(driveTypeOf));
            if (currentDirectory == null) throw new ArgumentNullException(nameof(currentDirectory));
            if (getFullPath == null) throw new ArgumentNullException(nameof(getFullPath));
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidPathException("Path is empty.");
            if (path.IndexOf("://", StringComparison.Ordinal) >= 0
                || path.TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || IsDeviceNamespacePath(path)
                || IsUncPath(path))
                throw new NonLocalPathException(NotLocalMessage);
            // F2: GetFullPath expands 8.3 short names, which queries the file system, so the drive is checked first:
            // the input's own drive when it names one, otherwise the current directory it resolves against.
            string raw = path.TrimStart();
            if (StartsWithDriveLetter(raw))
            {
                if (driveTypeOf(raw.Substring(0, 2) + "\\") == DriveType.Network)
                    throw new NonLocalPathException(NotLocalMessage);
            }
            else
            {
                string cwd = currentDirectory();
                if (string.IsNullOrEmpty(cwd) || IsUncPath(cwd))
                    throw new NonLocalPathException(NotLocalMessage);
                if (StartsWithDriveLetter(cwd) && driveTypeOf(cwd.Substring(0, 2) + "\\") == DriveType.Network)
                    throw new NonLocalPathException(NotLocalMessage);
            }
            string full;
            try
            {
                full = getFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new InvalidPathException("Invalid file path."); // malformed or too long, not non-local
            }
            // A relative or root-relative path resolves against the current directory, which may itself be a share.
            if (IsUncPath(full) || IsOnNetworkDrive(full, driveTypeOf))
                throw new NonLocalPathException(NotLocalMessage);
            return full;
        }

        /// <summary>Two leading slashes (either direction): <c>\\server\share\...</c> or <c>//server/share/...</c>.
        /// Device-namespace prefixes start the same way and are refused separately.</summary>
        private static bool IsUncPath(string path)
        {
            string p = path.TrimStart();
            return p.Length >= 2 && IsSlash(p[0]) && IsSlash(p[1]);
        }

        /// <summary>An ASCII drive letter followed by a colon: <c>C:\...</c>, <c>c:/...</c> or drive-relative <c>C:x</c>.</summary>
        private static bool StartsWithDriveLetter(string p)
        {
            if (p.Length < 2 || p[1] != ':') return false;
            char c = p[0];
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        /// <summary>True when the drive root of <paramref name="fullPath"/> (<c>X:\</c>) is a mapped network drive.</summary>
        private static bool IsOnNetworkDrive(string fullPath, Func<string, DriveType> driveTypeOf)
        {
            if (fullPath.Length < 2 || fullPath[1] != ':') return false;
            string root = fullPath.Substring(0, 2) + "\\";
            return driveTypeOf(root) == DriveType.Network;
        }

        /// <summary>GetDriveType through DriveInfo: a local lookup of the drive letter's mapping, no server contact.
        /// A lookup failure is not treated as a network drive (the later file access reports any problem).</summary>
        private static DriveType DriveTypeOfRoot(string root)
        {
            try
            {
                return new DriveInfo(root).DriveType;
            }
            catch (ArgumentException)
            {
                return DriveType.Unknown;
            }
        }

        /// <summary>
        /// <c>\\?\</c> and <c>\\.\</c> (either slash direction): Win32 device-namespace paths. GetFullPath leaves them
        /// unnormalized (no 8.3 expansion, a literal '?' in the path), which defeated the backup-name and symlink
        /// checks. <c>\??\</c> (either slash direction) is the NT object-manager prefix, which Win32 passes through
        /// unnormalized too. No file picker produces them, so they are refused.
        /// </summary>
        private static bool IsDeviceNamespacePath(string path)
        {
            string p = path.TrimStart();
            if (p.Length < 4) return false;
            if (IsSlash(p[0]) && p[1] == '?' && p[2] == '?' && IsSlash(p[3])) return true;
            return IsSlash(p[0]) && IsSlash(p[1]) && (p[2] == '?' || p[2] == '.') && IsSlash(p[3]);
        }

        private static bool IsSlash(char c)
        {
            return c == '\\' || c == '/';
        }
    }
}

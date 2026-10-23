using System;
using System.IO;
using System.Security.Cryptography;

namespace LockPad.Core
{
    /// <summary>SHA-256 fingerprint of a file's bytes, used to detect external changes.</summary>
    public sealed class FileFingerprint
    {
        public static readonly FileFingerprint Missing = new FileFingerprint(null);

        private readonly byte[] _hash;

        private FileFingerprint(byte[] hash)
        {
            _hash = hash;
        }

        public bool Exists
        {
            get { return _hash != null; }
        }

        public static FileFingerprint Compute(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                {
                    return new FileFingerprint(sha.ComputeHash(fs));
                }
            }
            catch (FileNotFoundException)
            {
                return Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return Missing;
            }
        }

        /// <summary>The fingerprint of bytes already read from a file.</summary>
        internal static FileFingerprint Of(byte[] content)
        {
            if (content == null) throw new ArgumentNullException("content");
            using (var sha = SHA256.Create())
                return new FileFingerprint(sha.ComputeHash(content));
        }

        public bool SameContentAs(FileFingerprint other)
        {
            if (other == null) return false;
            if (_hash == null || other._hash == null) return _hash == null && other._hash == null;
            if (_hash.Length != other._hash.Length) return false;
            for (int i = 0; i < _hash.Length; i++)
                if (_hash[i] != other._hash[i]) return false;
            return true;
        }
    }
}

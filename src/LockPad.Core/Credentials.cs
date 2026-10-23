using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using KeePassLib.Keys;

namespace LockPad.Core
{
    public sealed class Credentials : IDisposable
    {
        private CompositeKey _key;

        public string KeyFilePath { get; }

        internal CompositeKey CompositeKey
        {
            get
            {
                if (_key == null) throw new ObjectDisposedException(nameof(Credentials));
                return _key;
            }
        }

        public Credentials(byte[] passwordUtf8, string keyFilePath)
        {
            KeePassLibPolicy.Apply(); // a CompositeKey is only ever used for key derivation
            if (passwordUtf8 == null || passwordUtf8.Length == 0)
                throw new ArgumentException("Password is required.", nameof(passwordUtf8));

            // Validate locality before KeePassLib sees the path (it would issue web requests for URLs).
            string fullKeyPath = keyFilePath == null ? null : LocalPath.RequireLocalFile(keyFilePath);

            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword(passwordUtf8, false));
            if (fullKeyPath != null)
                key.AddUserKey(new KcpKeyFile(fullKeyPath));

            _key = key;
            KeyFilePath = fullKeyPath;
        }

        public static Credentials FromSecureString(SecureString password, string keyFilePath)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            IntPtr ptr = IntPtr.Zero;
            char[] chars = null;
            byte[] bytes = null;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(password);
                chars = new char[password.Length];
                Marshal.Copy(ptr, chars, 0, chars.Length);
                bytes = Encoding.UTF8.GetBytes(chars);
                return new Credentials(bytes, keyFilePath);
            }
            finally
            {
                if (chars != null) Array.Clear(chars, 0, chars.Length);
                if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                if (ptr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        public bool Matches(Credentials other)
        {
            if (other == null) return false;
            return CompositeKey.EqualsValue(other.CompositeKey);
        }

        public static void CreateKeyFile(string path)
        {
            string full = LocalPath.RequireLocalFile(path);
            if (File.Exists(full))
                throw new IOException("File already exists.");
            KcpKeyFile.Create(full, null);
        }

        public void Dispose()
        {
            _key = null;
        }
    }
}

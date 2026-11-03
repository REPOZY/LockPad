using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Security;
using KeePassLib.Cryptography;

namespace LockPad.Core
{
    public enum StrengthRating { Weak, Fair, Strong }

    public static class PasswordStrength
    {
        public const uint WarnBelowBits = 80;
        public const uint ConfirmBelowBits = 60;

        private static readonly object DictLock = new object();
        private static bool _dictTried;

        // KeePassLib ships with an empty popular-password dictionary; the KeePass app loads it at startup.
        // Loaded once, thread-safely. Failure is swallowed: ratings then skip the common-password check.
        private static void EnsureDictionary()
        {
            if (_dictTried) return;
            lock (DictLock)
            {
                if (_dictTried) return;
                try
                {
                    using (Stream s = typeof(PasswordStrength).Assembly.GetManifestResourceStream("MostPopularPasswords.txt.gz"))
                    {
                        if (s != null)
                        {
                            var ms = new MemoryStream();
                            s.CopyTo(ms);
                            PopularPasswords.Add(ms.ToArray(), true);
                        }
                    }
                }
                catch (Exception) { }
                _dictTried = true;
            }
        }

        public static uint EstimateBits(SecureString password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            IntPtr ptr = IntPtr.Zero;
            char[] chars = null;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(password);
                chars = new char[password.Length];
                Marshal.Copy(ptr, chars, 0, chars.Length);
                return EstimateBits(chars);
            }
            finally
            {
                if (chars != null) Array.Clear(chars, 0, chars.Length);
                if (ptr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        /// <summary>Does not modify or retain <paramref name="password"/>.</summary>
        public static uint EstimateBits(char[] password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            if (password.Length == 0) return 0;
            EnsureDictionary();
            return QualityEstimation.EstimatePasswordBits(password);
        }

        public static StrengthRating Rate(uint bits)
        {
            if (bits < ConfirmBelowBits) return StrengthRating.Weak;
            if (bits < WarnBelowBits) return StrengthRating.Fair;
            return StrengthRating.Strong;
        }
    }
}

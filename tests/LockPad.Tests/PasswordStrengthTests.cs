using System;
using System.Linq;
using System.Security;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class PasswordStrengthTests
    {
        private static SecureString Secure(string s)
        {
            var ss = new SecureString();
            foreach (char c in s) ss.AppendChar(c);
            return ss;
        }

        [TestCase(59u, StrengthRating.Weak)]
        [TestCase(60u, StrengthRating.Fair)]
        [TestCase(79u, StrengthRating.Fair)]
        [TestCase(80u, StrengthRating.Strong)]
        public void Rate_Boundaries(uint bits, StrengthRating expected)
        {
            Assert.That(PasswordStrength.Rate(bits), Is.EqualTo(expected));
        }

        [Test]
        public void Thresholds_AreSpecValues()
        {
            Assert.That(PasswordStrength.WarnBelowBits, Is.EqualTo(80u));
            Assert.That(PasswordStrength.ConfirmBelowBits, Is.EqualTo(60u));
        }

        [Test]
        public void CommonPassword_IsWeak()
        {
            Assert.That(PasswordStrength.Rate(PasswordStrength.EstimateBits("password".ToCharArray())),
                Is.EqualTo(StrengthRating.Weak));
        }

        [Test]
        public void LongRandom_IsStrong()
        {
            Assert.That(PasswordStrength.Rate(PasswordStrength.EstimateBits("q7#Vt9!mZ2@xLp4$Rw8&".ToCharArray())),
                Is.EqualTo(StrengthRating.Strong));
        }

        [Test]
        public void SecureStringAndCharArray_Agree()
        {
            const string pw = "q7#Vt9!mZ2@xLp4$Rw8&";
            using (SecureString ss = Secure(pw))
                Assert.That(PasswordStrength.EstimateBits(ss), Is.EqualTo(PasswordStrength.EstimateBits(pw.ToCharArray())));
        }

        // Built from numeric char codes so the source stays pure ASCII (immune to re-encoding).
        private static string NumeroSign() { return "Abc" + (char)0x2116 + "123"; }
        private static string Cyrillic()
        {
            return new string(new[] { (char)0x043F, (char)0x0430, (char)0x0440, (char)0x043E, (char)0x043B, (char)0x044C });
        }

        [TestCase(0)]
        [TestCase(1)]
        public void NonAscii_BothOverloadsAgree(int which)
        {
            string pw = which == 0 ? NumeroSign() : Cyrillic();
            Assert.That(pw.Any(c => c > 0xFF), Is.True, "input must contain a char above U+00FF");
            uint a = PasswordStrength.EstimateBits(pw.ToCharArray());
            using (SecureString ss = Secure(pw))
                Assert.That(PasswordStrength.EstimateBits(ss), Is.EqualTo(a));
        }

        [Test]
        public void PopularDictionary_IsLoaded()
        {
            PasswordStrength.EstimateBits("x1".ToCharArray());
            Assert.That(KeePassLib.Cryptography.PopularPasswords.IsPopularPassword("letmein".ToCharArray()), Is.True);
        }

        [Test]
        public void CommonPasswordWithSuffix_IsNotStrong()
        {
            Assert.That(PasswordStrength.Rate(PasswordStrength.EstimateBits("Password123!".ToCharArray())),
                Is.Not.EqualTo(StrengthRating.Strong));
        }

        [Test]
        public void Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PasswordStrength.EstimateBits((char[])null));
            Assert.Throws<ArgumentNullException>(() => PasswordStrength.EstimateBits((SecureString)null));
        }

        [Test]
        public void Empty_IsZeroAndWeak()
        {
            Assert.That(PasswordStrength.EstimateBits(new char[0]), Is.EqualTo(0u));
            using (SecureString ss = new SecureString())
                Assert.That(PasswordStrength.EstimateBits(ss), Is.EqualTo(0u));
            Assert.That(PasswordStrength.Rate(0), Is.EqualTo(StrengthRating.Weak));
        }

        [Test]
        public void CallerArray_IsNotModified()
        {
            char[] pw = "q7#Vt9!mZ2@xLp4$Rw8&".ToCharArray();
            char[] copy = (char[])pw.Clone();
            PasswordStrength.EstimateBits(pw);
            Assert.That(pw, Is.EqualTo(copy));
        }
    }
}

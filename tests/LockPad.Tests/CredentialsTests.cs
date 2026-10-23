using System;
using System.IO;
using System.Security;
using System.Text;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class CredentialsTests
    {
        private static byte[] U(string s) { return Encoding.UTF8.GetBytes(s); }

        [Test]
        public void SamePassword_Matches()
        {
            using (var a = new Credentials(U("pw1"), null))
            using (var b = new Credentials(U("pw1"), null))
                Assert.That(a.Matches(b), Is.True);
        }

        [Test]
        public void DifferentPassword_DoesNotMatch()
        {
            using (var a = new Credentials(U("pw1"), null))
            using (var b = new Credentials(U("pw2"), null))
                Assert.That(a.Matches(b), Is.False);
        }

        [Test]
        public void NonAsciiPassword_IsUsedVerbatim()
        {
            using (var a = new Credentials(U("Abc\u2116123"), null))
            using (var b = new Credentials(U("Abc\u2116123"), null))
            using (var c = new Credentials(U("Abc?123"), null))
            {
                Assert.That(a.Matches(b), Is.True);
                Assert.That(a.Matches(c), Is.False);
            }
        }

        [Test]
        public void FromSecureString_EqualsUtf8Bytes()
        {
            const string pw = "\u043F\u0430\u0440\u043E\u043B\u044C\u2116";
            var ss = new SecureString();
            foreach (char ch in pw) ss.AppendChar(ch);
            using (var a = Credentials.FromSecureString(ss, null))
            using (var b = new Credentials(U(pw), null))
                Assert.That(a.Matches(b), Is.True);
        }

        [Test]
        public void EmptyPassword_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Credentials(new byte[0], null));
        }

        [Test]
        public void KeyFile_ChangesKey()
        {
            using (var dir = new TempDir())
            {
                string f = dir.File("k.keyx");
                Credentials.CreateKeyFile(f);
                using (var a = new Credentials(U("pw"), f))
                using (var b = new Credentials(U("pw"), null))
                    Assert.That(a.Matches(b), Is.False);
            }
        }

        [Test]
        public void MissingKeyFile_ThrowsIOException()
        {
            using (var dir = new TempDir())
                Assert.Throws(Is.InstanceOf<IOException>(), () => new Credentials(U("pw"), dir.File("nope.keyx")));
        }

        [Test]
        public void CreateKeyFile_RefusesToOverwrite()
        {
            using (var dir = new TempDir())
            {
                string f = dir.File("k.keyx");
                File.WriteAllText(f, "x");
                Assert.Throws<IOException>(() => Credentials.CreateKeyFile(f));
                Assert.That(File.ReadAllText(f), Is.EqualTo("x"));
            }
        }

        [Test]
        public void KeyFileUrl_ThrowsNonLocalPath()
        {
            Assert.Throws<NonLocalPathException>(() => new Credentials(U("pw"), "https://evil.example/k.keyx"));
        }

        [Test]
        public void CreateKeyFileUrl_ThrowsNonLocalPath()
        {
            Assert.Throws<NonLocalPathException>(() => Credentials.CreateKeyFile("https://evil.example/k.keyx"));
        }
    }
}

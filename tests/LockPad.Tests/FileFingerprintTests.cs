using System.IO;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class FileFingerprintTests
    {
        [Test]
        public void SameBytes_SameContent()
        {
            using (var d = new TempDir())
            {
                File.WriteAllText(d.File("a"), "abc");
                File.WriteAllText(d.File("b"), "abc");
                Assert.That(FileFingerprint.Compute(d.File("a")).SameContentAs(FileFingerprint.Compute(d.File("b"))), Is.True);
            }
        }

        [Test]
        public void DifferentBytes_NotSame()
        {
            using (var d = new TempDir())
            {
                File.WriteAllText(d.File("a"), "abc");
                File.WriteAllText(d.File("b"), "abd");
                Assert.That(FileFingerprint.Compute(d.File("a")).SameContentAs(FileFingerprint.Compute(d.File("b"))), Is.False);
            }
        }

        [Test]
        public void Missing_NotExists()
        {
            using (var d = new TempDir())
            {
                var fp = FileFingerprint.Compute(d.File("nope"));
                Assert.That(fp.Exists, Is.False);
                Assert.That(fp.SameContentAs(FileFingerprint.Missing), Is.True);
            }
        }

        [Test]
        public void ReadsFileHeldOpenByWriter()
        {
            using (var d = new TempDir())
            {
                string f = d.File("a");
                File.WriteAllText(f, "abc");
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    Assert.DoesNotThrow(() => FileFingerprint.Compute(f));
                }
            }
        }
    }
}

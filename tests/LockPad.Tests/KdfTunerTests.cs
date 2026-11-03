using System;
using System.Linq;
using LockPad.Core;
using KeePassLib.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class KdfTunerTests
    {
        [Test]
        public void Argon2id_UsesIdVariantAndDefaultsMemoryParallelism()
        {
            KdfParameters p = KdfTuner.Argon2id(100);
            Assert.That(p.KdfUuid, Is.EqualTo(new Argon2Kdf(Argon2Type.ID).Uuid));
            Assert.That(p.GetUInt64(Argon2Kdf.ParamMemory, 0), Is.EqualTo(64UL * 1024 * 1024));
            Assert.That(p.GetUInt32(Argon2Kdf.ParamParallelism, 0), Is.EqualTo(2u));
            Assert.That(p.GetUInt64(Argon2Kdf.ParamIterations, 0), Is.GreaterThanOrEqualTo(2UL));
        }

        [Test]
        public void Argon2id_TinyTarget_StillAtLeastTwoIterationsAndNotWeak()
        {
            KdfParameters p = KdfTuner.Argon2id(1);
            Assert.That(p.GetUInt64(Argon2Kdf.ParamIterations, 0), Is.GreaterThanOrEqualTo(2UL));
            Assert.That(new Argon2Kdf(Argon2Type.ID).AreParametersWeak(p), Is.False);
        }

        [Test]
        public void Argon2id_ZeroTarget_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => KdfTuner.Argon2id(0));
        }

        [Test]
        public void Argon2id_FreshSaltPerCall()
        {
            byte[] a = KdfTuner.Argon2id(1).GetByteArray(Argon2Kdf.ParamSalt);
            byte[] b = KdfTuner.Argon2id(1).GetByteArray(Argon2Kdf.ParamSalt);
            Assert.That(a.Length, Is.EqualTo(32));
            Assert.That(b.Length, Is.EqualTo(32));
            Assert.That(a.Any(x => x != 0), Is.True);
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void DefaultTarget_Is1000()
        {
            Assert.That(KdfTuner.DefaultTargetMilliseconds, Is.EqualTo(1000u));
        }
    }
}

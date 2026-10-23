using KeePassLib.Cryptography.KeyDerivation;

namespace LockPad.Tests
{
    internal static class TestKdf
    {
        public static KdfParameters Fast()
        {
            KdfParameters p = new Argon2Kdf(Argon2Type.ID).GetDefaultParameters();
            p.SetUInt64(Argon2Kdf.ParamIterations, 1);
            p.SetUInt64(Argon2Kdf.ParamMemory, 1024 * 1024);
            return p;
        }
    }
}
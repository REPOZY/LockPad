using System;
using KeePassLib.Cryptography.KeyDerivation;

namespace LockPad.Core
{
    public static class KdfTuner
    {
        public const uint DefaultTargetMilliseconds = 1000;

        private const ulong MinIterations = 2;
        private const ulong MemoryBytes = 64UL * 1024 * 1024;
        private const uint Parallelism = 2;

        /// <summary>
        /// Builds Argon2id parameters tuned to take roughly <paramref name="targetMilliseconds"/>
        /// on this machine. Runs Argon2 on the calling thread for about that long: callers (UI)
        /// must invoke it off the UI thread. Always returns a fresh salt, memory 64 MiB,
        /// parallelism 2 and at least 2 iterations (never "weak" per KeePassLib). Tunes against KeePassLib's managed
        /// Argon2, which is what every later key derivation uses (<see cref="KeePassLibPolicy"/>).
        /// </summary>
        public static KdfParameters Argon2id(uint targetMilliseconds)
        {
            KeePassLibPolicy.Apply();
            if (targetMilliseconds == 0)
                throw new ArgumentOutOfRangeException(nameof(targetMilliseconds), "Target must be greater than zero.");

            var kdf = new Argon2Kdf(Argon2Type.ID);
            KdfParameters p = kdf.GetBestParameters(targetMilliseconds);

            if (p.GetUInt64(Argon2Kdf.ParamIterations, 0) < MinIterations)
                p.SetUInt64(Argon2Kdf.ParamIterations, MinIterations);
            p.SetUInt64(Argon2Kdf.ParamMemory, MemoryBytes);
            p.SetUInt32(Argon2Kdf.ParamParallelism, Parallelism);
            return p;
        }
    }
}

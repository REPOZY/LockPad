using System;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;

namespace LockPad.Core
{
    /// <summary>
    /// The crypto and format settings every notebook write enforces (SEC-M1), not only Create: another client (e.g.
    /// KeePassXC's database settings) can change them in the file, and KeePassLib would otherwise keep writing what it
    /// loaded. Applied to the database right before each write; the reopened temp file is checked against it too.
    /// <list type="bullet">
    /// <item>Cipher AES-256.</item>
    /// <item>KDF Argon2id with memory at least 64 MiB, parallelism at least 2 and at least 2 iterations. Anything below
    /// that floor, or another KDF, is replaced by a fresh KdfTuner result (fresh salt); compliant parameters are kept
    /// as they are. KeePassLib re-randomizes the KDF salt (Argon2) or seed (AES-KDF), the master seed and the
    /// encryption IV on every write (KdbxFile.Write: kdf.Randomize), so keeping the parameters never reuses a salt.</item>
    /// <item>Notes protected in memory, history limited to 10 items per entry (and KeePassLib's default 6 MiB, the
    /// value Create has always used), recycle bin off.</item>
    /// </list>
    /// </summary>
    internal static class SaveFormatPolicy
    {
        internal const ulong MinArgon2MemoryBytes = 64UL * 1024 * 1024;
        internal const ulong MinArgon2Iterations = 2;
        internal const uint MinArgon2Parallelism = 2;
        internal const int HistoryMaxItems = 10;
        internal const long HistoryMaxSizeBytes = 6L * 1024 * 1024;

        private static readonly PwUuid Argon2idUuid = new Argon2Kdf(Argon2Type.ID).Uuid;

        /// <summary>
        /// Test seam only: when true, Argon2id parameters below the floor are accepted (the test suite writes notebooks
        /// with 1 MiB and 1 iteration to stay fast). The cipher, the KDF type and the other settings are still enforced.
        /// Never set by the application.
        /// </summary>
        internal static bool AcceptWeakKdfForTests { get; set; }

        /// <summary>Produces replacement KDF parameters (about 1 s on this machine). Test seam.</summary>
        internal static Func<KdfParameters> FreshKdf { get; set; } =
            () => KdfTuner.Argon2id(KdfTuner.DefaultTargetMilliseconds);

        /// <summary>Re-asserts every setting on <paramref name="db"/>. Runs KdfTuner (seconds) only when the KDF
        /// parameters are below the floor.</summary>
        public static void Apply(PwDatabase db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            db.DataCipherUuid = StandardAesEngine.AesUuid;
            if (!IsStrongKdf(db.KdfParameters))
            {
                KdfParameters fresh = FreshKdf();
                if (!IsStrongKdf(fresh)) throw new InvalidOperationException("The replacement KDF parameters are too weak.");
                db.KdfParameters = fresh;
            }
            db.MemoryProtection.ProtectNotes = true;
            db.HistoryMaxItems = HistoryMaxItems;
            db.HistoryMaxSize = HistoryMaxSizeBytes;
            db.RecycleBinEnabled = false;
        }

        /// <summary>True when a database read back from disk carries the enforced cipher, KDF and limits.</summary>
        public static bool IsSatisfiedBy(PwDatabase db)
        {
            return db != null
                && db.DataCipherUuid.Equals(StandardAesEngine.AesUuid)
                && IsStrongKdf(db.KdfParameters)
                && db.HistoryMaxItems == HistoryMaxItems
                && db.HistoryMaxSize == HistoryMaxSizeBytes
                && !db.RecycleBinEnabled;
        }

        internal static bool IsStrongKdf(KdfParameters p)
        {
            if (p == null || !Argon2idUuid.Equals(p.KdfUuid)) return false;
            if (AcceptWeakKdfForTests) return true;
            return p.GetUInt64(Argon2Kdf.ParamMemory, 0) >= MinArgon2MemoryBytes
                && p.GetUInt64(Argon2Kdf.ParamIterations, 0) >= MinArgon2Iterations
                && p.GetUInt32(Argon2Kdf.ParamParallelism, 0) >= MinArgon2Parallelism;
        }
    }
}

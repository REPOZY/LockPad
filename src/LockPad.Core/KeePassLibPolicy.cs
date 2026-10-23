using KeePassLib.Native;

namespace LockPad.Core
{
    /// <summary>
    /// Process-wide KeePassLib settings LockPad depends on. LockPad never ships KeePassLib's native library
    /// (KeePassLibN.*.dll); with native code allowed, every Argon2 or AES-KDF run would look it up through the default
    /// DLL search order, so a planted copy (next to a notebook, on PATH) would run in-process and receive the raw
    /// composite key. The managed implementations are used instead, and KdfTuner tunes against them.
    /// <para>
    /// Applied by every Core entry point (NotebookStore, KdfTuner, Credentials) and again immediately before each key
    /// derivation, so a caller that re-enabled it cannot reach the native path through Core. Never call NativeLib's
    /// "is the library installed" probe: it forces native code on while it probes (a repository guard test enforces
    /// this by name, so the name is not spelled out here).
    /// </para>
    /// </summary>
    public static class KeePassLibPolicy
    {
        public static void Apply()
        {
            NativeLib.AllowNative = false;
        }
    }
}

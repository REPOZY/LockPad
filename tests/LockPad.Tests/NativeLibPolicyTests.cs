using System.IO;
using System.Text;
using LockPad.Core;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Native;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// SEC-H2: KeePassLib's native library (KeePassLibN.*.dll) is never shipped, so loading it can only pick up a
    /// planted DLL that would receive the composite key. Every Core entry point turns the native path off before any
    /// key derivation can run. Each test first forces it on, as a stray caller could.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class NativeLibPolicyTests
    {
        [TearDown]
        public void TearDown()
        {
            NativeLib.AllowNative = false;
        }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        [Test]
        public void NotebookStore_Construction_DisablesNativeLib()
        {
            NativeLib.AllowNative = true;
            new NotebookStore();
            Assert.That(NativeLib.AllowNative, Is.False);
        }

        [Test]
        public void Credentials_Construction_DisablesNativeLib()
        {
            NativeLib.AllowNative = true;
            using (Pw()) { }
            Assert.That(NativeLib.AllowNative, Is.False);
        }

        [Test]
        public void KdfTuner_DisablesNativeLib()
        {
            NativeLib.AllowNative = true;
            KdfTuner.Argon2id(1);
            Assert.That(NativeLib.AllowNative, Is.False);
        }

        [Test]
        public void NotebookStore_Open_DisablesNativeLibEvenIfReenabledAfterConstruction()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("nb.kdbx");
                var store = new NotebookStore();
                using (store.Create(path, Pw(), TestKdf.Fast())) { }

                NativeLib.AllowNative = true;
                using (store.Open(path, Pw())) { }
                Assert.That(NativeLib.AllowNative, Is.False);
            }
        }

        [Test]
        public void NotebookStore_Save_DisablesNativeLibEvenIfReenabledAfterOpen()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("nb.kdbx");
                var store = new NotebookStore();
                using (store.Create(path, Pw(), TestKdf.Fast())) { }
                using (Notebook nb = store.Open(path, Pw()))
                {
                    nb.AddNote("n");
                    NativeLib.AllowNative = true;
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                    Assert.That(NativeLib.AllowNative, Is.False);
                }
            }
        }

        [Test]
        public void App_HardenNativeLoading_DisablesNativeLibAndClearsDllDirectory()
        {
            NativeLib.AllowNative = true;
            bool dllDirectoryCleared = LockPad.App.App.HardenNativeLoading();
            Assert.That(NativeLib.AllowNative, Is.False);
            Assert.That(dllDirectoryCleared, Is.True, "SetDllDirectory(\"\") failed");
        }

        /// <summary>The common file dialogs must not move the process current directory to the chosen folder
        /// (WPF maps RestoreDirectory to OFN_NOCHANGEDIR / FOS_NOCHANGEDIR).</summary>
        [Test]
        public void DialogService_FileDialogs_DoNotChangeCurrentDirectory()
        {
            var dialogs = new Microsoft.Win32.FileDialog[]
            {
                LockPad.App.Services.DialogService.NewOpenDialog("x|*.x", "t"),
                LockPad.App.Services.DialogService.NewSaveDialog("t"),
                LockPad.App.Services.DialogService.NewKeyFileSaveDialog(),
            };
            foreach (Microsoft.Win32.FileDialog d in dialogs)
                Assert.That(d.RestoreDirectory, Is.True, d.GetType().Name + " " + d.Title);
        }

        [Test]
        public void KdfTuner_ManagedArgon2_StillAtLeastTwoIterationsAnd64MiB()
        {
            KdfParameters p = KdfTuner.Argon2id(100);
            Assert.That(NativeLib.AllowNative, Is.False);
            Assert.That(p.GetUInt64(Argon2Kdf.ParamIterations, 0), Is.GreaterThanOrEqualTo(2UL));
            Assert.That(p.GetUInt64(Argon2Kdf.ParamMemory, 0), Is.EqualTo(64UL * 1024 * 1024));
            Assert.That(p.GetUInt32(Argon2Kdf.ParamParallelism, 0), Is.EqualTo(2u));
        }
    }
}

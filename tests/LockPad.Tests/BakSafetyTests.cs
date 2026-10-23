using System;
using System.IO;
using System.Linq;
using System.Text;
using LockPad.App.ViewModels;
using LockPad.Core;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// .bak safety (red-team M1 and L1 on V7): LockPad never destroys a <c>.bak</c> it cannot prove is its own. A
    /// <c>.bak</c> this notebook did not write is replaced only if it opens with the notebook's credentials and carries
    /// the LockPad marker; anything else is renamed aside to <c>.bak.kept-yyyyMMdd-HHmmss</c> and the user is told.
    /// </summary>
    [TestFixture]
    public class BakSafetyTests
    {
        private const string KeptPattern = @"^nb\.kdbx\.bak\.kept-[0-9]{8}-[0-9]{6}$";

        private TempDir _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
        }

        [TearDown]
        public void TearDown()
        {
            _dir.Dispose(); // also clears read-only attributes
        }

        private string P { get { return _dir.File("nb.kdbx"); } }
        private string Bak { get { return P + ".bak"; } }

        private static Credentials Pw(string password = "pw")
        {
            return new Credentials(Encoding.UTF8.GetBytes(password), null);
        }

        private string[] FileNames()
        {
            return Directory.GetFiles(_dir.Path).Select(Path.GetFileName).ToArray();
        }

        private string[] KeptFiles()
        {
            return FileNames().Where(n => n.IndexOf(".kept-", StringComparison.Ordinal) >= 0).ToArray();
        }

        /// <summary>A real LockPad notebook with one note, under <paramref name="password"/>, at <paramref name="path"/>.</summary>
        private static byte[] WriteNotebook(string path, string password, string noteTitle)
        {
            var store = new NotebookStore();
            using (Notebook nb = store.Create(path, Pw(password), TestKdf.Fast()))
            {
                nb.AddNote(noteTitle);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            File.Delete(path + ".bak");
            return File.ReadAllBytes(path);
        }

        private static void WriteUnmarkedKdbx(string path, string password)
        {
            using (Credentials c = Pw(password))
            {
                var db = new PwDatabase();
                db.New(IOConnectionInfo.FromPath(path), c.CompositeKey);
                db.KdfParameters = TestKdf.Fast();
                var e = new PwEntry(true, true);
                e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "bank"));
                e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "SECRET-PW"));
                db.RootGroup.AddEntry(e, true);
                db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
                db.Close();
            }
        }

        private static string Notice(string keptName)
        {
            return "An existing file \"nb.kdbx.bak\" was not created by this notebook. It was kept as \"" + keptName + "\".";
        }

        /// <summary>Opens nb.kdbx in a fresh session, edits and saves it; returns the result.</summary>
        private SaveResult EditAndSaveInNewSession(NotebookStore store, out Notebook nb)
        {
            nb = store.Open(P, Pw());
            nb.AddNote("edit " + Guid.NewGuid().ToString("N"));
            return store.Save(nb, false);
        }

        public enum Foreign
        {
            OtherPassword,
            NotKdbx,
            SamePasswordWithoutMarker,
            EmptyFile,
        }

        [TestCase(Foreign.OtherPassword)]
        [TestCase(Foreign.NotKdbx)]
        [TestCase(Foreign.SamePasswordWithoutMarker)]
        [TestCase(Foreign.EmptyFile)]
        public void Save_ForeignBak_KeptAside_SaveSucceeds_KeptBytesUnchanged(Foreign kind)
        {
            byte[] main = WriteNotebook(P, "pw", "mine");
            switch (kind)
            {
                case Foreign.OtherPassword: WriteNotebook(_dir.File("copy.kdbx"), "other", "copy"); File.Move(_dir.File("copy.kdbx"), Bak); break;
                case Foreign.NotKdbx: File.WriteAllText(Bak, "user's own manual backup copy"); break;
                case Foreign.SamePasswordWithoutMarker: WriteUnmarkedKdbx(Bak, "pw"); break;
                case Foreign.EmptyFile: File.WriteAllBytes(Bak, new byte[0]); break;
            }
            byte[] foreign = File.ReadAllBytes(Bak);

            var store = new NotebookStore();
            Notebook nb;
            SaveResult r = EditAndSaveInNewSession(store, out nb);
            using (nb)
            {
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                string kept = KeptFiles().Single();
                Assert.That(kept, Does.Match(KeptPattern));
                Assert.That(File.ReadAllBytes(_dir.File(kept)), Is.EqualTo(foreign), "the foreign file is kept byte for byte");
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main), ".bak now holds this notebook's previous version");
                Assert.That(nb.TakeBackupNotice(), Is.EqualTo(Notice(kept)));
                Assert.That(nb.TakeBackupNotice(), Is.Null, "one notice per occurrence");
                Assert.That(NotebookStore.IsBackupCopyName(_dir.File(kept)), Is.False, "the kept name is not a backup name");
                Assert.That(kept, Does.Not.EndWith(".tmp"));
            }
            Assert.That(Directory.GetFiles(_dir.Path, "*.tmp"), Is.Empty);
        }

        [Test]
        public void Save_OwnPreviousVersionFromAnEarlierSession_Replaced_NoKeptFile_NoNotice()
        {
            WriteNotebook(P, "pw", "mine");
            var first = new NotebookStore();
            Notebook a;
            Assert.That(EditAndSaveInNewSession(first, out a).Status, Is.EqualTo(SaveStatus.Saved));
            a.Dispose();
            Assert.That(File.Exists(Bak), Is.True);
            byte[] main = File.ReadAllBytes(P);

            var store = new NotebookStore();
            Notebook nb;
            SaveResult r = EditAndSaveInNewSession(store, out nb);
            using (nb)
            {
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
                Assert.That(nb.TakeBackupNotice(), Is.Null);
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }));
        }

        [Test]
        public void Save_OwnBak_ClassifiedAtMostOncePerSession_ThenReplacedWithoutAKeyDerivation()
        {
            WriteNotebook(P, "pw", "mine");
            Notebook a;
            Assert.That(EditAndSaveInNewSession(new NotebookStore(), out a).Status, Is.EqualTo(SaveStatus.Saved));
            a.Dispose();

            var store = new NotebookStore();
            int classifications = 0;
            store.TestHook = (stage, path) => { if (stage == "classify-backup") classifications++; };
            using (Notebook nb = store.Open(P, Pw()))
            {
                for (int i = 1; i <= 3; i++)
                {
                    nb.AddNote("save " + i);
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "save " + i);
                }
            }
            Assert.That(classifications, Is.EqualTo(1));
            Assert.That(KeptFiles(), Is.Empty);
        }

        [Test]
        public void Save_NewBakWrittenThisSession_NeverClassified()
        {
            var store = new NotebookStore();
            int classifications = 0;
            store.TestHook = (stage, path) => { if (stage == "classify-backup") classifications++; };
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                for (int i = 1; i <= 3; i++)
                {
                    nb.AddNote("save " + i);
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "save " + i);
                }
            }
            Assert.That(classifications, Is.EqualTo(0), "the .bak was written by this notebook object");
        }

        [Test]
        public void Save_OwnBakSwappedForAForeignFileDuringTheSession_KeptAside()
        {
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("one");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                File.Delete(Bak);
                File.WriteAllText(Bak, "dropped in by the user");
                byte[] main = File.ReadAllBytes(P);

                nb.AddNote("two");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

                string kept = KeptFiles().Single();
                Assert.That(File.ReadAllText(_dir.File(kept)), Is.EqualTo("dropped in by the user"));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
            }
        }

        [Test]
        public void Save_BakIsADirectory_SaveFails_MessageNamesIt_NothingDestroyed()
        {
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                byte[] main = File.ReadAllBytes(P);
                Directory.CreateDirectory(Bak);
                File.WriteAllText(Path.Combine(Bak, "inside.txt"), "user data");
                nb.AddNote("unsaved");

                SaveResult r = store.Save(nb, false);

                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Does.StartWith("Could not replace the backup file \"nb.kdbx.bak\": "));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
                Assert.That(File.ReadAllText(Path.Combine(Bak, "inside.txt")), Is.EqualTo("user data"));
            }
            Assert.That(FileNames(), Is.EqualTo(new[] { "nb.kdbx" }));
        }

        [Test]
        public void Save_KeepAsideFails_IoError_MainAndForeignFileUntouched()
        {
            byte[] main = WriteNotebook(P, "pw", "mine");
            File.WriteAllText(Bak, "user's own manual backup copy");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw()))
            {
                nb.AddNote("edit");
                SaveResult r;
                // Readable, but held without delete sharing: it cannot be renamed.
                using (new FileStream(Bak, FileMode.Open, FileAccess.Read, FileShare.Read))
                    r = store.Save(nb, false);

                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Does.StartWith(
                    "An existing file \"nb.kdbx.bak\" was not created by this notebook and could not be moved aside: "));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(nb.TakeBackupNotice(), Is.Null);
            }
            Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
            Assert.That(File.ReadAllText(Bak), Is.EqualTo("user's own manual backup copy"));
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }), "nothing kept, no temp files");
        }

        [Test]
        public void KeptName_Taken_GetsANumberedSuffix()
        {
            WriteNotebook(P, "pw", "mine");
            File.WriteAllText(Bak, "foreign");
            var store = new NotebookStore();
            store.Now = () => new DateTime(2026, 10, 19, 13, 14, 15);
            File.WriteAllText(_dir.File("nb.kdbx.bak.kept-20261019-131415"), "an earlier kept file");
            File.WriteAllText(_dir.File("nb.kdbx.bak.kept-20261019-131415-2"), "another one");

            Notebook nb;
            Assert.That(EditAndSaveInNewSession(store, out nb).Status, Is.EqualTo(SaveStatus.Saved));
            using (nb)
                Assert.That(nb.TakeBackupNotice(), Is.EqualTo(Notice("nb.kdbx.bak.kept-20261019-131415-3")));

            Assert.That(File.ReadAllText(_dir.File("nb.kdbx.bak.kept-20261019-131415")), Is.EqualTo("an earlier kept file"));
            Assert.That(File.ReadAllText(_dir.File("nb.kdbx.bak.kept-20261019-131415-2")), Is.EqualTo("another one"));
            Assert.That(File.ReadAllText(_dir.File("nb.kdbx.bak.kept-20261019-131415-3")), Is.EqualTo("foreign"));
        }

        [Test]
        public void SaveAs_OntoANameWhoseBakBelongsToAnotherNotebookWithTheSamePassword_KeptAside()
        {
            // The other notebook was deleted or moved; its .bak is its only copy. Same password: still not this
            // notebook's history (a notebook saved under a new name has none).
            string other = _dir.File("other.kdbx");
            byte[] otherBytes = WriteNotebook(other, "pw", "the other notebook");
            File.Move(other, other + ".bak");

            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("mine");
                Assert.That(store.SaveAs(nb, other).Status, Is.EqualTo(SaveStatus.Saved));
                string kept = FileNames().Single(n => n.StartsWith("other.kdbx.bak.kept-", StringComparison.Ordinal));
                Assert.That(nb.TakeBackupNotice(), Is.EqualTo("An existing file \"other.kdbx.bak\" was not created by this "
                    + "notebook. It was kept as \"" + kept + "\"."));

                byte[] saved = File.ReadAllBytes(other);
                nb.AddNote("more");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(File.ReadAllBytes(other + ".bak"), Is.EqualTo(saved));
                Assert.That(File.ReadAllBytes(_dir.File(kept)), Is.EqualTo(otherBytes));
            }
        }

        [Test]
        public void Create_OntoANameWithAStaleBak_KeptAside()
        {
            File.WriteAllText(Bak, "stale");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                string kept = KeptFiles().Single();
                Assert.That(kept, Does.Match(KeptPattern));
                Assert.That(File.ReadAllText(_dir.File(kept)), Is.EqualTo("stale"));
                Assert.That(nb.TakeBackupNotice(), Is.EqualTo(Notice(kept)));
                Assert.That(File.Exists(Bak), Is.False);

                byte[] created = File.ReadAllBytes(P);
                nb.AddNote("x");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(created));
                Assert.That(File.ReadAllText(_dir.File(kept)), Is.EqualTo("stale"));
            }
        }

        [Test]
        public void SaveAs_OntoANameWhoseBakIsADirectory_Refused_NothingWritten()
        {
            string other = _dir.File("other.kdbx");
            Directory.CreateDirectory(other + ".bak");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("mine");
                SaveResult r = store.SaveAs(nb, other);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Does.StartWith("Could not replace the backup file \"other.kdbx.bak\": "));
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(P)));
            }
            Assert.That(File.Exists(other), Is.False);
            Assert.That(Directory.Exists(other + ".bak"), Is.True);
        }

        [Test]
        public void Editor_SaveNow_ShowsTheNoticeInTheStatusBar()
        {
            WriteNotebook(P, "pw", "mine");
            File.WriteAllText(Bak, "foreign");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw()))
            {
                var vm = new EditorViewModel(nb, store, new FakeDialogService(), new SettingsStore(_dir.File("settings")),
                    new AppSettings());
                vm.BeginBusy = () => new Nothing();
                vm.SelectedNote = vm.VisibleNotes.Single();
                vm.EditorText = "edited";

                Assert.That(vm.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));

                string kept = KeptFiles().Single();
                Assert.That(vm.StatusText, Is.EqualTo(Notice(kept)));
                vm.EditorText = "typed on";
                Assert.That(vm.StatusText, Is.EqualTo(EditorViewModel.StatusUnsaved), "shown once");
            }
        }

        [Test]
        public void Editor_ShownForANotebookWithAPendingNotice_ShowsIt()
        {
            // E.g. Create kept a stale .bak aside, then the shell shows the editor.
            File.WriteAllText(Bak, "stale");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                var vm = new EditorViewModel(nb, store, new FakeDialogService(), new SettingsStore(_dir.File("settings")),
                    new AppSettings());
                Assert.That(vm.StatusText, Is.EqualTo(Notice(KeptFiles().Single())));
            }
        }

        // ---- B1 follow-up: a .bak with heavier key settings than the notebook is never test-opened ----

        private static KeePassLib.Cryptography.KeyDerivation.KdfParameters Argon2(
            KeePassLib.Cryptography.KeyDerivation.Argon2Type type, ulong memory, ulong iterations, uint lanes)
        {
            var p = new KeePassLib.Cryptography.KeyDerivation.Argon2Kdf(type).GetDefaultParameters();
            p.SetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamMemory, memory);
            p.SetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamIterations, iterations);
            p.SetUInt32(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamParallelism, lanes);
            return p;
        }

        private const ulong MiB = 1024UL * 1024;
        private static readonly KeePassLib.Cryptography.KeyDerivation.Argon2Type Id =
            KeePassLib.Cryptography.KeyDerivation.Argon2Type.ID;

        /// <summary>A previous version of nb.kdbx as far as ownership goes (same password, the marker and nb.kdbx's
        /// notebook id) but with the given KDF, at nb.kdbx.bak.</summary>
        private void WriteSamePasswordBak(KeePassLib.Cryptography.KeyDerivation.KdfParameters kdf)
        {
            string copy = _dir.File("copy.kdbx");
            using (new NotebookStore().Create(copy, Pw(), kdf)) { }
            SetFileNotebookId(copy, FileNotebookId(P));
            File.Move(copy, Bak);
        }

        /// <summary>Saves nb.kdbx in a new session, counting the key derivations run for the .bak check.</summary>
        private SaveResult SaveCountingBackupKdf(out int backupKdfRuns, out Notebook nb)
        {
            var store = new NotebookStore();
            int runs = 0;
            store.TestHook = (stage, path) => { if (stage == "backup-kdf") runs++; };
            nb = store.Open(P, Pw());
            nb.AddNote("edit");
            SaveResult r = store.Save(nb, false);
            backupKdfRuns = runs;
            return r;
        }

        [TestCase(1UL, 1UL, 2u)]  // the same parameters as the notebook
        [TestCase(1UL, 2UL, 2u)]  // twice the iterations is still accepted
        [TestCase(1UL, 1UL, 1u)]  // lighter: fewer lanes
        public void Save_SamePasswordBakWithParametersNoHeavierThanTheNotebook_IsOurs(ulong memoryMiB, ulong iterations, uint lanes)
        {
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { }
            WriteSamePasswordBak(Argon2(Id, memoryMiB * MiB, iterations, lanes));
            byte[] main = File.ReadAllBytes(P);

            int kdfRuns;
            Notebook nb;
            SaveResult r = SaveCountingBackupKdf(out kdfRuns, out nb);
            nb.Dispose();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
            Assert.That(kdfRuns, Is.EqualTo(1), "the .bak was test-opened");
            Assert.That(KeptFiles(), Is.Empty, "classified as ours and replaced");
            Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
        }

        public enum Heavier
        {
            Memory,
            Iterations,
            Lanes,
            AesKdf,
            Argon2dHeavierMemory,
        }

        [TestCase(Heavier.Memory)]
        [TestCase(Heavier.Iterations)]
        [TestCase(Heavier.Lanes)]
        [TestCase(Heavier.AesKdf)]
        [TestCase(Heavier.Argon2dHeavierMemory)]
        public void Save_SamePasswordBakWithHeavierOrOtherKdf_KeptAside_KeyNeverDerived(Heavier kind)
        {
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { }
            KeePassLib.Cryptography.KeyDerivation.KdfParameters kdf;
            if (kind == Heavier.AesKdf)
            {
                kdf = new KeePassLib.Cryptography.KeyDerivation.AesKdf().GetDefaultParameters();
                kdf.SetUInt64(KeePassLib.Cryptography.KeyDerivation.AesKdf.ParamRounds, 1000);
            }
            else
            {
                kdf = kind == Heavier.Memory ? Argon2(Id, 2 * MiB, 1, 2)
                    : kind == Heavier.Iterations ? Argon2(Id, 1 * MiB, 3, 2)
                    : kind == Heavier.Lanes ? Argon2(Id, 1 * MiB, 1, 4)
                    : Argon2(KeePassLib.Cryptography.KeyDerivation.Argon2Type.D, 2 * MiB, 1, 2);
            }
            // Written directly with KeePassLib (as another client could): same password, the LockPad marker, this KDF.
            using (Credentials c = Pw())
            {
                var db = new PwDatabase();
                db.New(IOConnectionInfo.FromPath(Bak), c.CompositeKey);
                db.KdfParameters = kdf;
                db.CustomData.Set(NotebookStore.MarkerKey, "1");
                db.SaveAs(IOConnectionInfo.FromPath(Bak), false, null);
                db.Close();
            }
            byte[] foreign = File.ReadAllBytes(Bak);

            int kdfRuns;
            Notebook nb;
            SaveResult r = SaveCountingBackupKdf(out kdfRuns, out nb);
            using (nb)
            {
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(kdfRuns, Is.EqualTo(0), "no key was derived for the .bak");
                string kept = KeptFiles().Single();
                Assert.That(File.ReadAllBytes(_dir.File(kept)), Is.EqualTo(foreign));
                Assert.That(nb.TakeBackupNotice(), Is.EqualTo(Notice(kept)));
            }
        }

        /// <summary>Inserts an outer-header field (KDBX 4: id byte, Int32 size, data) just before the end-of-header
        /// field. The stored header hash is not updated: KeePassLib checks it only after the key derivation.</summary>
        private static void InsertHeaderField(string path, byte id, byte[] data)
        {
            byte[] file = File.ReadAllBytes(path);
            int pos = 12;
            while (file[pos] != 0) pos += 5 + BitConverter.ToInt32(file, pos + 1);
            byte[] field = new byte[] { id }.Concat(BitConverter.GetBytes(data.Length)).Concat(data).ToArray();
            File.WriteAllBytes(path, file.Take(pos).Concat(field).Concat(file.Skip(pos)).ToArray());
        }

        /// <summary>Header fields KeePassLib also honours in a KDBX 4 file and that change the key derivation: a
        /// second KDF parameters field (the last one wins), and the obsolete KDBX 3 transform seed and rounds fields.
        /// Each switches to settings heavier than the first field's.</summary>
        public enum ExtraKdfField
        {
            SecondKdfParameters,
            ObsoleteTransformRounds,
            ObsoleteTransformSeed,
        }

        private static void InsertExtraKdfField(string path, ExtraKdfField kind)
        {
            if (kind == ExtraKdfField.SecondKdfParameters)
                InsertHeaderField(path, 11, KeePassLib.Cryptography.KeyDerivation.KdfParameters.SerializeExt(
                    Argon2(Id, 1 * MiB, 3, 2)));
            else if (kind == ExtraKdfField.ObsoleteTransformRounds)
                InsertHeaderField(path, 6, BitConverter.GetBytes(1000UL));
            else
                InsertHeaderField(path, 5, new byte[32]);
        }

        [TestCase(ExtraKdfField.SecondKdfParameters)]
        [TestCase(ExtraKdfField.ObsoleteTransformRounds)]
        [TestCase(ExtraKdfField.ObsoleteTransformSeed)]
        public void Save_BakWithAnExtraKdfHeaderField_KeptAside_KeyNeverDerived(ExtraKdfField kind)
        {
            // Full-codebase red team 2026-10-22 (Medium): the first KDF field was checked against the cap, but
            // KeePassLib derives with the last one (or AES-KDF from the obsolete fields), so a planted .bak could run
            // any key derivation, for hours, on the UI thread.
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { }
            WriteSamePasswordBak(Argon2(Id, 1 * MiB, 1, 2));
            InsertExtraKdfField(Bak, kind);
            byte[] planted = File.ReadAllBytes(Bak);

            int kdfRuns;
            Notebook nb;
            SaveResult r = SaveCountingBackupKdf(out kdfRuns, out nb);
            using (nb)
            {
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(kdfRuns, Is.EqualTo(0), "no key was derived for the .bak");
                string kept = KeptFiles().Single();
                Assert.That(File.ReadAllBytes(_dir.File(kept)), Is.EqualTo(planted));
            }
        }

        [TestCase(ExtraKdfField.SecondKdfParameters)]
        [TestCase(ExtraKdfField.ObsoleteTransformRounds)]
        [TestCase(ExtraKdfField.ObsoleteTransformSeed)]
        public void KdfHeaderReader_ExtraKdfHeaderField_Null(ExtraKdfField kind)
        {
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { }
            InsertExtraKdfField(P, kind);
            Assert.That(KdbxHeader.ReadKdfParameters(File.ReadAllBytes(P)), Is.Null);
        }

        /// <summary>Opens nb.kdbx, replaces the file on disk with a marked same-password notebook with nb.kdbx's own
        /// notebook id (this notebook, changed elsewhere) using <paramref name="kdf"/>, then answers Overwrite,
        /// counting the key derivations run to check the file.</summary>
        private SaveResult OverwriteReplacedFile(KeePassLib.Cryptography.KeyDerivation.KdfParameters kdf,
            out int overwriteKdfRuns, out byte[] onDisk)
        {
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { }
            var store = new NotebookStore();
            int runs = 0;
            store.TestHook = (stage, path) => { if (stage == "overwrite-kdf") runs++; };
            using (Notebook nb = store.Open(P, Pw()))
            {
                string copy = _dir.File("other.kdbx");
                using (new NotebookStore().Create(copy, Pw(), kdf)) { }
                SetFileNotebookId(copy, FileNotebookId(P));
                File.Delete(P);
                File.Move(copy, P);
                onDisk = File.ReadAllBytes(P);
                nb.AddNote("edit");
                SaveResult r = store.Save(nb, true);
                overwriteKdfRuns = runs;
                return r;
            }
        }

        [Test]
        public void SaveOverwrite_FileReplacedWithTheSameKdf_CheckedAndOverwritten()
        {
            int runs;
            byte[] replaced;
            SaveResult r = OverwriteReplacedFile(Argon2(Id, 1 * MiB, 1, 2), out runs, out replaced);
            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
            Assert.That(runs, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(P), Is.Not.EqualTo(replaced));
        }

        [Test]
        public void SaveOverwrite_FileReplacedWithAHeavierKdf_RefusedWithoutDerivingAKey()
        {
            // Full check 2026-10-22, L2: Overwrite test-opened the replaced file with whatever KDF its header named,
            // on the UI thread, with the notes on screen.
            int runs;
            byte[] replaced;
            SaveResult r = OverwriteReplacedFile(Argon2(Id, 1 * MiB, 3, 2), out runs, out replaced);
            Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
            Assert.That(r.Message, Does.Contain("could not safely check"));
            Assert.That(runs, Is.EqualTo(0), "no key was derived for the replaced file");
            Assert.That(File.ReadAllBytes(P), Is.EqualTo(replaced), "not overwritten");
        }

        [Test]
        public void SaveOverwrite_FileReplacedWithAnotherSamePasswordNotebook_RefusedAndNeverLost()
        {
            // Full-check red team 2026-10-22 (M1): Overwrite checked only the marker, so a different notebook with the
            // same password moved over this one was replaced, became the .bak, was remembered as this notebook's own
            // .bak and was destroyed by the next save.
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { }
            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw()))
            {
                string other = _dir.File("other.kdbx");
                using (new NotebookStore().Create(other, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { } // its own new id
                File.Delete(P);
                File.Move(other, P);
                byte[] otherBytes = File.ReadAllBytes(P);

                nb.AddNote("edit");
                SaveResult r = store.Save(nb, true);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError), "a different notebook is never overwritten");
                Assert.That(r.Message, Does.Contain("Save As"));
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(otherBytes));
                Assert.That(nb.IsDirty, Is.True);

                store.Save(nb, false);
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(otherBytes), "a later normal save does not touch it either");
            }
        }

        [Test]
        public void SaveAs_GivesTheCopyANewNotebookId_OverwriteOfTheOriginalByTheCopyIsRefused()
        {
            // Re-attack 2026-10-22 (Medium): Save As kept the id, so a Save As copy moved over the original passed the
            // Overwrite id check and the M1 outcome (the replaced notebook destroyed by the next save) was back.
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { }
            string copy = _dir.File("copy.kdbx");
            var store = new NotebookStore();
            using (Notebook forked = store.Open(P, Pw()))
            {
                forked.AddNote("only in the copy");
                Assert.That(store.SaveAs(forked, copy).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(forked.FileNotebookId, Is.EqualTo(FileNotebookId(copy)));
            }
            Assert.That(FileNotebookId(copy), Is.Not.Null.And.Not.EqualTo(FileNotebookId(P)));

            using (Notebook nb = store.Open(P, Pw()))
            {
                File.Delete(P);
                File.Move(copy, P);
                byte[] copyBytes = File.ReadAllBytes(P);
                nb.AddNote("edit");
                Assert.That(store.Save(nb, true).Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(copyBytes));
            }
        }

        [Test]
        public void SaveAs_Fails_TheNotebookKeepsItsId()
        {
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 2))) { }
            string original = FileNotebookId(P);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw()))
            {
                nb.AddNote("edit");
                store.TestHook = (stage, path) => { if (stage == "after-temp-write") throw new IOException("disk full"); };
                Assert.That(store.SaveAs(nb, _dir.File("copy.kdbx")).Status, Is.Not.EqualTo(SaveStatus.Saved));
                store.TestHook = null;
                Assert.That(nb.FileNotebookId, Is.EqualTo(original));
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(FileNotebookId(P), Is.EqualTo(original), "a later save of the original keeps its id");
        }

        /// <summary>Records Debug.Assert failures (KeePassLib asserts are compiled into Debug builds).</summary>
        private sealed class FailRecorder : System.Diagnostics.TraceListener
        {
            public readonly System.Collections.Generic.List<string> Failures = new System.Collections.Generic.List<string>();
            public override void Fail(string message) { Failures.Add(message ?? ""); }
            public override void Fail(string message, string detailMessage) { Failures.Add(message ?? ""); }
            public override void Write(string message) { }
            public override void WriteLine(string message) { }
        }

        [Test]
        public void Save_BakWithAnotherPassword_OwnershipCheck_RaisesNoKeePassLibAssert()
        {
            // Full-check red team 2026-10-22 (L4): loading the checked bytes without a root group made KeePassLib
            // assert on a wrong key; in a Debug build of LockPad.exe that is a modal Abort/Retry/Ignore box.
            WriteNotebook(P, "pw", "mine");
            string theirs = _dir.File("theirs.kdbx");
            WriteNotebook(theirs, "another password", "theirs");
            File.Move(theirs, Bak);
            var recorder = new FailRecorder();
            System.Diagnostics.Trace.Listeners.Add(recorder);
            try
            {
                var store = new NotebookStore();
                using (Notebook nb = store.Open(P, Pw()))
                {
                    nb.AddNote("edit");
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                }
            }
            finally
            {
                System.Diagnostics.Trace.Listeners.Remove(recorder);
            }
            Assert.That(KeptFiles().Length, Is.EqualTo(1), "the foreign .bak was test-opened and kept aside");
            Assert.That(recorder.Failures, Is.Empty);
        }

        [Test]
        public void Save_BakWithOnlyTheOldLabels_IsForeign_KeptAside_NeverReplaced()
        {
            byte[] main = WriteNotebook(P, "pw", "mine");
            string ownId;
            using (Notebook own = new NotebookStore().Open(P, Pw())) ownId = own.FileNotebookId;
            Assert.That(ownId, Is.Not.Null);
            using (Credentials c = Pw())
            {
                var db = new PwDatabase();
                db.New(IOConnectionInfo.FromPath(Bak), c.CompositeKey);
                db.KdfParameters = TestKdf.Fast();
                // The notebook's OWN id under the old labels (review A1): only the label decides that it is foreign.
                db.CustomData.Set("KdbxPad.Notebook", "1");
                db.CustomData.Set("KdbxPad.NotebookId", ownId);
                db.SaveAs(IOConnectionInfo.FromPath(Bak), false, null);
                db.Close();
            }
            byte[] foreign = File.ReadAllBytes(Bak);

            var store = new NotebookStore();
            Notebook nb;
            SaveResult r = EditAndSaveInNewSession(store, out nb);
            using (nb)
            {
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                string kept = KeptFiles().Single();
                Assert.That(File.ReadAllBytes(_dir.File(kept)), Is.EqualTo(foreign), "the old-label file is kept byte for byte");
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main), ".bak now holds this notebook's previous version");
                Assert.That(nb.TakeBackupNotice(), Is.EqualTo(Notice(kept)));
            }
        }

        [Test]
        public void KdfHeaderReader_ReadsTheParametersOfAWrittenNotebook_NullForOtherFiles()
        {
            using (new NotebookStore().Create(P, Pw(), Argon2(Id, 3 * MiB, 2, 2))) { }
            var p = KdbxHeader.ReadKdfParameters(P);
            Assert.That(p, Is.Not.Null);
            Assert.That(p.GetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamMemory, 0), Is.EqualTo(3 * MiB));
            Assert.That(p.GetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamIterations, 0), Is.EqualTo(2UL));

            File.WriteAllText(_dir.File("text.bak"), "not a database");
            Assert.That(KdbxHeader.ReadKdfParameters(_dir.File("text.bak")), Is.Null);
            File.WriteAllBytes(_dir.File("empty.bak"), new byte[0]);
            Assert.That(KdbxHeader.ReadKdfParameters(_dir.File("empty.bak")), Is.Null);
            File.WriteAllBytes(_dir.File("short.bak"), File.ReadAllBytes(P).Take(20).ToArray());
            Assert.That(KdbxHeader.ReadKdfParameters(_dir.File("short.bak")), Is.Null);
        }

        [Test]
        public void Save_PreviousVersionBelowTheFloor_PolicyReplacedTheKdfOnThisSave_StillOurs()
        {
            // Session 1 (the suite's relaxed policy): 1 MiB, 1 iteration, 1 lane, saved so its .bak exists.
            var store1 = new NotebookStore();
            using (Notebook first = store1.Create(P, Pw(), Argon2(Id, 1 * MiB, 1, 1)))
            {
                first.AddNote("one");
                Assert.That(store1.Save(first, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            byte[] main = File.ReadAllBytes(P);

            // Session 2 under the production floor: this save writes fresh 64 MiB / 2-lane parameters, while the .bak
            // still has the old, lighter ones.
            bool accept = SaveFormatPolicy.AcceptWeakKdfForTests;
            var fresh = SaveFormatPolicy.FreshKdf;
            SaveFormatPolicy.AcceptWeakKdfForTests = false;
            SaveFormatPolicy.FreshKdf = () => KdfTuner.Argon2id(1);
            try
            {
                int kdfRuns;
                Notebook nb;
                SaveResult r = SaveCountingBackupKdf(out kdfRuns, out nb);
                nb.Dispose();
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(kdfRuns, Is.EqualTo(1));
            }
            finally
            {
                SaveFormatPolicy.AcceptWeakKdfForTests = accept;
                SaveFormatPolicy.FreshKdf = fresh;
            }
            Assert.That(KeptFiles(), Is.Empty, "the lighter previous version is ours");
            Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
        }

        // ---- B2 (red-team L1): a read-only own .bak ----

        [Test]
        public void Save_ReadOnlyOwnBak_WrittenThisSession_SaveSucceeds_BakHoldsThePreviousVersion()
        {
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("one");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                File.SetAttributes(Bak, FileAttributes.ReadOnly | FileAttributes.Hidden);
                byte[] main = File.ReadAllBytes(P);

                nb.AddNote("two");
                SaveResult r = store.Save(nb, false);

                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
            }
            Assert.That(KeptFiles(), Is.Empty);
        }

        [Test]
        public void Save_ReadOnlyOwnBak_FromAnEarlierSession_SaveSucceeds()
        {
            WriteNotebook(P, "pw", "mine");
            Notebook a;
            Assert.That(EditAndSaveInNewSession(new NotebookStore(), out a).Status, Is.EqualTo(SaveStatus.Saved));
            a.Dispose();
            File.SetAttributes(Bak, FileAttributes.ReadOnly);
            byte[] main = File.ReadAllBytes(P);

            Notebook nb;
            SaveResult r = EditAndSaveInNewSession(new NotebookStore(), out nb);
            nb.Dispose();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
            Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
            Assert.That(KeptFiles(), Is.Empty);
        }

        [Test]
        public void Save_OwnBakCannotBeReplaced_MessageNamesTheBak_MainAndBakUntouched()
        {
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("one");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                byte[] main = File.ReadAllBytes(P);
                byte[] backup = File.ReadAllBytes(Bak);

                nb.AddNote("two");
                SaveResult r;
                using (new FileStream(Bak, FileMode.Open, FileAccess.Read, FileShare.Read))
                    r = store.Save(nb, false);

                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Does.StartWith("Could not replace the backup file \"nb.kdbx.bak\": "));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(backup));
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }));
        }

        [Test]
        public void Save_TargetLocked_MessageDoesNotBlameTheBak()
        {
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("one");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                nb.AddNote("two");
                SaveResult r;
                using (new FileStream(P, FileMode.Open, FileAccess.Read, FileShare.Read))
                    r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Does.Not.Contain("backup file"));
            }
        }

        // ---- Re-attack L2: a locked .bak is retried, never treated as foreign ----

        [TestCase(FileShare.None)]
        [TestCase(FileShare.Write)]
        public void Save_OwnBakWrittenThisSession_LockedAtSaveTime_Fails_NothingRenamed_RetrySucceeds(FileShare share)
        {
            var store = new NotebookStore();
            using (Notebook nb = store.Create(P, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("one");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                byte[] main = File.ReadAllBytes(P);
                byte[] backup = File.ReadAllBytes(Bak);

                nb.AddNote("two");
                SaveResult r;
                using (new FileStream(Bak, FileMode.Open, FileAccess.Read, share))
                    r = store.Save(nb, false);

                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Does.StartWith("Could not replace the backup file \"nb.kdbx.bak\": "));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(nb.TakeBackupNotice(), Is.Null, "nothing was kept aside");
                Assert.That(FileNames(), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }));
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(backup));

                // The lock is gone: the retry replaces the own .bak normally.
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
                Assert.That(nb.TakeBackupNotice(), Is.Null);
            }
            Assert.That(KeptFiles(), Is.Empty);
        }

        [Test]
        public void Save_OwnBakFromAnEarlierSession_LockedAtSaveTime_Fails_NothingRenamed_RetrySucceeds()
        {
            WriteNotebook(P, "pw", "mine");
            Notebook a;
            Assert.That(EditAndSaveInNewSession(new NotebookStore(), out a).Status, Is.EqualTo(SaveStatus.Saved));
            a.Dispose();
            byte[] main = File.ReadAllBytes(P);
            byte[] backup = File.ReadAllBytes(Bak);

            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw()))
            {
                nb.AddNote("edit");
                SaveResult r;
                using (new FileStream(Bak, FileMode.Open, FileAccess.Read, FileShare.None))
                    r = store.Save(nb, false);

                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Does.StartWith("Could not replace the backup file \"nb.kdbx.bak\": "));
                Assert.That(KeptFiles(), Is.Empty);
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(backup));

                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
            }
            Assert.That(KeptFiles(), Is.Empty);
        }

        // ---- Re-attack L1: a per-notebook id tells two same-password notebooks apart ----

        private const string IdPattern = "^[0-9a-f]{32}$";

        /// <summary>The LockPad.NotebookId stored in the file, or null.</summary>
        private static string FileNotebookId(string path)
        {
            using (Credentials c = Pw())
            {
                var db = new PwDatabase();
                db.Open(IOConnectionInfo.FromPath(path), c.CompositeKey, null);
                try { return db.CustomData.Get(NotebookStore.NotebookIdKey); }
                finally { db.Close(); }
            }
        }

        /// <summary>Rewrites the file with KeePassLib, setting (or with null removing) its LockPad.NotebookId; the KDF
        /// parameters are kept.</summary>
        private static void SetFileNotebookId(string path, string id)
        {
            using (Credentials c = Pw())
            {
                var db = new PwDatabase();
                db.Open(IOConnectionInfo.FromPath(path), c.CompositeKey, null);
                try
                {
                    if (id == null) db.CustomData.Remove(NotebookStore.NotebookIdKey);
                    else db.CustomData.Set(NotebookStore.NotebookIdKey, id);
                    db.Save(null);
                }
                finally { db.Close(); }
            }
        }

        [Test]
        public void Create_WritesARandomNotebookId_OpenAndSaveKeepIt()
        {
            using (new NotebookStore().Create(P, Pw(), TestKdf.Fast())) { }
            string id = FileNotebookId(P);
            Assert.That(id, Does.Match(IdPattern));

            string other = _dir.File("other.kdbx");
            using (new NotebookStore().Create(other, Pw(), TestKdf.Fast())) { }
            Assert.That(FileNotebookId(other), Does.Match(IdPattern));
            Assert.That(FileNotebookId(other), Is.Not.EqualTo(id), "random per notebook");

            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw()))
            {
                nb.AddNote("edit");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(FileNotebookId(P), Is.EqualTo(id), "the save preserved it");
        }

        [Test]
        public void OldNotebookWithoutId_GetsOneOnItsNextSuccessfulSave_MarkerStays()
        {
            using (new NotebookStore().Create(P, Pw(), TestKdf.Fast())) { }
            SetFileNotebookId(P, null); // as written by an older build
            Assert.That(FileNotebookId(P), Is.Null);

            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw()))
            {
                Assert.That(nb.IsReadOnly, Is.False, "still a marked notebook");
                nb.AddNote("edit");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(FileNotebookId(P), Does.Match(IdPattern));
            using (Credentials c = Pw())
            {
                var db = new PwDatabase();
                db.Open(IOConnectionInfo.FromPath(P), c.CompositeKey, null);
                try { Assert.That(db.CustomData.Get(NotebookStore.MarkerKey), Is.EqualTo("1")); }
                finally { db.Close(); }
            }
        }

        [Test]
        public void Save_BakIsAnotherNotebookWithTheSamePasswordAndADifferentId_KeptAside()
        {
            // Re-attack PoC SamePasswordAndMarker_DifferentNotebook.
            WriteNotebook(P, "pw", "mine");
            string other = _dir.File("other.kdbx");
            byte[] otherBytes = WriteNotebook(other, "pw", "SECRET-OTHER");
            File.Copy(other, Bak);
            string otherId = FileNotebookId(other);

            var store = new NotebookStore();
            Notebook nb;
            SaveResult r = EditAndSaveInNewSession(store, out nb);
            using (nb)
            {
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                string kept = KeptFiles().Single();
                Assert.That(File.ReadAllBytes(_dir.File(kept)), Is.EqualTo(otherBytes));
                string notice = nb.TakeBackupNotice();
                Assert.That(notice, Is.EqualTo(Notice(kept)));
                Assert.That(notice, Does.Not.Contain(otherId), "the id never appears in messages");
            }
        }

        [Test]
        public void Save_BakWithTheSameId_FromAnEarlierCopyOfThisNotebook_Replaced()
        {
            WriteNotebook(P, "pw", "mine");
            File.Copy(P, Bak); // an earlier version of this very notebook: same id
            byte[] main = File.ReadAllBytes(P);

            Notebook nb;
            SaveResult r = EditAndSaveInNewSession(new NotebookStore(), out nb);
            nb.Dispose();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
            Assert.That(KeptFiles(), Is.Empty);
            Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
        }

        [TestCase(false, true)]  // an old .bak without an id, a notebook with one
        [TestCase(true, false)]  // a .bak with an id, an old notebook without one
        public void Save_IdMissingOnEitherSide_FallsBackToKeyAndMarker_Ours(bool bakHasId, bool notebookHasId)
        {
            WriteNotebook(P, "pw", "mine");
            string other = _dir.File("other.kdbx");
            WriteNotebook(other, "pw", "same password, marked"); // a different id, if it keeps one
            if (!bakHasId) SetFileNotebookId(other, null);
            if (!notebookHasId) SetFileNotebookId(P, null);
            File.Move(other, Bak);
            byte[] main = File.ReadAllBytes(P);

            Notebook nb;
            SaveResult r = EditAndSaveInNewSession(new NotebookStore(), out nb);
            nb.Dispose();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
            Assert.That(KeptFiles(), Is.Empty, "key + marker, no id to compare: ours (as before the id existed)");
            Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(main));
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}

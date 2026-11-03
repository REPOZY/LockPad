using System;
using System.IO;
using System.Linq;
using System.Text;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// UX round 3, V7 (user decision 2026-10-19): every save keeps exactly one backup, <c>notebook.bak</c>, holding the
    /// version before that save. Older <c>.bak1</c> to <c>.bak3</c> files and rotation leftovers are never touched.
    /// </summary>
    [TestFixture]
    public class SingleBackupTests
    {
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

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        private string[] FileNames()
        {
            return Directory.GetFiles(_dir.Path).Select(Path.GetFileName).ToArray();
        }

        [Test]
        public void BackupPath_IsTheNotebookPathPlusBak()
        {
            Assert.That(NotebookStore.BackupPath(@"C:\x\n.kdbx"), Is.EqualTo(@"C:\x\n.kdbx.bak"));
        }

        [Test]
        public void Create_NewFile_NoBackup()
        {
            string p = _dir.File("n.kdbx");
            using (new NotebookStore().Create(p, Pw(), TestKdf.Fast())) { }
            Assert.That(FileNames(), Is.EqualTo(new[] { "n.kdbx" }));
        }

        [Test]
        public void SaveAs_NewFile_NoBackupForTheNewFile()
        {
            string p = _dir.File("n.kdbx");
            string q = _dir.File("copy.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("edit");
                Assert.That(store.SaveAs(nb, q).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "n.kdbx", "copy.kdbx" }));
        }

        [Test]
        public void Saves_TheBakAlwaysHoldsTheVersionBeforeTheLastSave_OnlyOneBackupFile()
        {
            string p = _dir.File("n.kdbx");
            string bak = p + ".bak";
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                byte[] first = File.ReadAllBytes(p);
                Assert.That(File.Exists(bak), Is.False, "the first version has no backup");

                string id = nb.AddNote("t").Id;
                nb.UpdateNote(id, "t", "second");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                byte[] second = File.ReadAllBytes(p);
                Assert.That(File.ReadAllBytes(bak), Is.EqualTo(first), "second save: .bak is the first version");

                nb.UpdateNote(id, "t", "third");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(File.ReadAllBytes(bak), Is.EqualTo(second), "third save: .bak is the second version");

                nb.UpdateNote(id, "t", "fourth");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "n.kdbx", "n.kdbx.bak" }), "no .bak1 to .bak3, no temp files");
            var reader = new NotebookStore();
            using (Notebook b = reader.Open(bak, Pw()))
                Assert.That(b.Notes.Single().Text, Is.EqualTo("third"));
            using (Notebook m = reader.Open(p, Pw()))
                Assert.That(m.Notes.Single().Text, Is.EqualTo("fourth"));
        }

        [Test]
        public void Save_ExternalChangeOverwritten_TheReplacedVersionIsTheBak()
        {
            string p = _dir.File("n.kdbx");
            string other = _dir.File("other.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                // The same notebook, changed elsewhere (a different notebook is refused: full-check red team M1).
                File.Copy(p, other);
                using (Notebook o = store.Open(other, Pw()))
                {
                    o.AddNote("theirs");
                    Assert.That(store.Save(o, false).Status, Is.EqualTo(SaveStatus.Saved));
                }
                byte[] external = File.ReadAllBytes(other);
                nb.AddNote("ours");
                File.WriteAllBytes(p, external);
                Assert.That(store.Save(nb, true).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(File.ReadAllBytes(p + ".bak"), Is.EqualTo(external));
            }
        }

        public enum Failure
        {
            VerifyMismatch,
            IoErrorBeforeReplace,
            TargetChangedBeforeReplace,
            BakLockedWithoutDeleteSharing,
            TargetLockedWithoutDeleteSharing,
        }

        [TestCase(Failure.VerifyMismatch)]
        [TestCase(Failure.IoErrorBeforeReplace)]
        [TestCase(Failure.TargetChangedBeforeReplace)]
        [TestCase(Failure.BakLockedWithoutDeleteSharing)]
        [TestCase(Failure.TargetLockedWithoutDeleteSharing)]
        public void Save_Fails_MainAndBakByteIdentical_NothingLeftOver(Failure failure)
        {
            string p = _dir.File("n.kdbx");
            string bak = p + ".bak";
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                string id = nb.AddNote("t").Id;
                nb.UpdateNote(id, "t", "saved");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                byte[] main = File.ReadAllBytes(p);
                byte[] backup = File.ReadAllBytes(bak);

                nb.UpdateNote(id, "t", "unsaved");
                FileStream held = null;
                try
                {
                    if (failure == Failure.TargetChangedBeforeReplace)
                    {
                        // Another program changes the file during the KDF runs: it is never replaced (the second
                        // attempt is refused up front, as the file differs from what was loaded).
                        main = main.Concat(new byte[] { 0x78 }).ToArray();
                    }
                    bool changed = false;
                    store.TestHook = (stage, temp) =>
                    {
                        if (failure == Failure.VerifyMismatch && stage == "after-temp-write")
                            File.WriteAllBytes(temp, new byte[] { 1, 2, 3 });
                        if (failure == Failure.IoErrorBeforeReplace && stage == "before-replace")
                            throw new IOException("simulated disk failure");
                        if (failure == Failure.TargetChangedBeforeReplace && stage == "before-replace" && !changed)
                        {
                            changed = true;
                            File.AppendAllText(p, "x");
                        }
                    };
                    if (failure == Failure.BakLockedWithoutDeleteSharing)
                        held = new FileStream(bak, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (failure == Failure.TargetLockedWithoutDeleteSharing)
                        held = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);

                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        SaveResult r = store.Save(nb, false);
                        Assert.That(r.Status, Is.Not.EqualTo(SaveStatus.Saved), "attempt " + attempt);
                        Assert.That(nb.IsDirty, Is.True);
                        Assert.That(File.ReadAllBytes(p), Is.EqualTo(main), "main, attempt " + attempt);
                        Assert.That(File.ReadAllBytes(bak), Is.EqualTo(backup), ".bak, attempt " + attempt);
                    }
                }
                finally
                {
                    if (held != null) held.Dispose();
                }
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "n.kdbx", "n.kdbx.bak" }), "no temp files left");
        }

        [Test]
        public void Save_BakIsADirectory_Fails_MainUntouched_DirectoryKept()
        {
            string p = _dir.File("n.kdbx");
            string bak = p + ".bak";
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                byte[] main = File.ReadAllBytes(p);
                Directory.CreateDirectory(bak);
                nb.AddNote("unsaved");
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(File.ReadAllBytes(p), Is.EqualTo(main));
                Assert.That(Directory.Exists(bak), Is.True);
            }
            Assert.That(FileNames(), Is.EqualTo(new[] { "n.kdbx" }), "no temp files left");
        }

        [Test]
        public void Save_IdenticalContent_BakUntouched()
        {
            string p = _dir.File("n.kdbx");
            string bak = p + ".bak";
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                string id = nb.AddNote("t").Id;
                nb.UpdateNote(id, "t", "v1");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                nb.UpdateNote(id, "t", "v2");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                byte[] backup = File.ReadAllBytes(bak);
                DateTime backupTime = File.GetLastWriteTimeUtc(bak);
                byte[] main = File.ReadAllBytes(p);

                nb.UpdateNote(id, "t", "typed");
                nb.UpdateNote(id, "t", "v2"); // and undone: dirty, but equal to the file
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

                Assert.That(File.ReadAllBytes(bak), Is.EqualTo(backup), "the version before the last real save is kept");
                Assert.That(File.GetLastWriteTimeUtc(bak), Is.EqualTo(backupTime));
                Assert.That(File.ReadAllBytes(p), Is.EqualTo(main));
            }
        }

        [Test]
        public void Saves_OlderBak1ToBak3AndRotationLeftovers_NeverTouched()
        {
            string p = _dir.File("n.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                string[] old =
                {
                    p + ".bak1", p + ".bak2", p + ".bak3", p + ".bak3.kdbxpad-rotation-0badf00d.old"
                };
                for (int i = 0; i < old.Length; i++) File.WriteAllText(old[i], "older LockPad " + i);
                DateTime stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                foreach (string f in old) File.SetLastWriteTimeUtc(f, stamp);

                string id = nb.AddNote("t").Id;
                for (int i = 1; i <= 3; i++)
                {
                    nb.UpdateNote(id, "t", "v" + i);
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "save " + i);
                }

                for (int i = 0; i < old.Length; i++)
                {
                    Assert.That(File.ReadAllText(old[i]), Is.EqualTo("older LockPad " + i), Path.GetFileName(old[i]));
                    Assert.That(File.GetLastWriteTimeUtc(old[i]), Is.EqualTo(stamp), Path.GetFileName(old[i]));
                }
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[]
            {
                "n.kdbx", "n.kdbx.bak", "n.kdbx.bak1", "n.kdbx.bak2", "n.kdbx.bak3",
                "n.kdbx.bak3.kdbxpad-rotation-0badf00d.old"
            }));
        }

        [Test]
        public void Bak_OpensReadOnly_AsABackupCopy_SaveRefused()
        {
            string p = _dir.File("n.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("first");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            byte[] backup = File.ReadAllBytes(p + ".bak");
            var reader = new NotebookStore();
            using (Notebook b = reader.Open(p + ".bak", Pw()))
            {
                Assert.That(b.IsReadOnly, Is.True);
                Assert.That(b.ReadOnlyReason, Is.EqualTo(ReadOnlyReason.BackupCopy));
                Assert.That(reader.Save(b, false).Status, Is.EqualTo(SaveStatus.ReadOnly));
                Assert.That(reader.Save(b, true).Status, Is.EqualTo(SaveStatus.ReadOnly));
            }
            Assert.That(File.ReadAllBytes(p + ".bak"), Is.EqualTo(backup));
        }

        [TestCase("n.kdbx.bak")]
        [TestCase("N.KDBX.BAK")]
        [TestCase("n.kdbx.Bak.")] // GetFullPath trims the trailing dot: the file would be n.kdbx.Bak
        public void SaveAs_BakName_Refused_NothingWritten(string name)
        {
            string p = _dir.File("n.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("work");
                SaveResult r = store.SaveAs(nb, _dir.File(name));
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("This name is reserved for backup copies. Choose another name."));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(p)));
            }
            Assert.That(FileNames(), Is.EqualTo(new[] { "n.kdbx" }), "the next save of n.kdbx cannot overwrite a copy");
        }

        [Test]
        public void Create_BakName_Refused_NothingWritten()
        {
            Credentials c = Pw();
            var ex = Assert.Throws<IOException>(() => new NotebookStore().Create(_dir.File("n.kdbx.bak"), c, TestKdf.Fast()));
            c.Dispose();
            Assert.That(ex.Message, Is.EqualTo("This name is reserved for backup copies. Choose another name."));
            Assert.That(FileNames(), Is.Empty);
        }

        [TestCase("n.kdbx.bak", true)]
        [TestCase("n.kdbx.BAK", true)]
        [TestCase("n.kdbx.bak1", true)]
        [TestCase("n.kdbx.bak12", true)]
        [TestCase("x.bak", true)]
        [TestCase(".bak", false)]  // nothing before the suffix
        [TestCase("n.kdbx.bakx", false)]
        [TestCase("n.bak.kdbx", false)]
        [TestCase("n.kdbx.backup", false)]
        [TestCase("n.kdbxbak", false)]
        public void IsBackupCopyName_BakWithOptionalAsciiDigits(string name, bool expected)
        {
            Assert.That(NotebookStore.IsBackupCopyName(_dir.File(name)), Is.EqualTo(expected));
        }

        [Test]
        public void IsBackupCopyName_NonAsciiDigit_NotABackupName()
        {
            Assert.That(NotebookStore.IsBackupCopyName(_dir.File("n.kdbx.bak" + (char)0x0661)), Is.False);
        }

        // ---- File.Replace failing half-way (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2) ----

        [Test]
        public void Save_ReplaceFailsHalfWay_NewCopyKept_MessageNamesBothFiles()
        {
            string p = _dir.File("n.kdbx");
            string bak = p + ".bak";
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("first");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved)); // an own, older .bak exists
                byte[] previous = File.ReadAllBytes(p);
                nb.RemoveNote(nb.Notes.Single().Id);
                nb.AddNote("new work");
                string keptTemp = null;
                // What Win32 ReplaceFile leaves on ERROR_UNABLE_TO_MOVE_REPLACEMENT_2: the target was renamed to the
                // backup name (replacing the old backup), the new copy is still under its temp name.
                store.ReplaceFile = (source, destination, backup) =>
                {
                    keptTemp = source;
                    File.Delete(backup);
                    File.Move(destination, backup);
                    throw new IOException("Unable to move the replacement file to the file to be replaced.");
                };

                SaveResult r = store.Save(nb, false);

                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("Unable to move the replacement file to the file to be replaced."
                    + " The new copy was kept at " + keptTemp + " and the previous version at " + bak + "."));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(File.Exists(p), Is.False);
                Assert.That(File.ReadAllBytes(bak), Is.EqualTo(previous));
                Assert.That(File.Exists(keptTemp), Is.True, "the verified new copy is not deleted");
                store.ReplaceFile = null;
            }
            using (Notebook kept = new NotebookStore().Open(Directory.GetFiles(_dir.Path, "*.tmp").Single(), Pw()))
                Assert.That(kept.Notes.Single().Title, Is.EqualTo("new work"));
        }

        [Test]
        public void Save_ReplaceFailsWithFilesUnchanged_TempDeleted_PlainMessage()
        {
            string p = _dir.File("n.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("first");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved)); // an own, older .bak exists
                byte[] main = File.ReadAllBytes(p);
                byte[] backup = File.ReadAllBytes(p + ".bak");
                nb.AddNote("new work");
                // ERROR_UNABLE_TO_REMOVE_REPLACED / ERROR_UNABLE_TO_MOVE_REPLACEMENT: every file keeps its name.
                store.ReplaceFile = (source, destination, b) => { throw new IOException("Unable to remove."); };
                SaveResult r = store.Save(nb, false);
                store.ReplaceFile = null;

                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("Unable to remove."));
                Assert.That(File.ReadAllBytes(p), Is.EqualTo(main));
                Assert.That(File.ReadAllBytes(p + ".bak"), Is.EqualTo(backup));
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "n.kdbx", "n.kdbx.bak" }));
        }
    }
}

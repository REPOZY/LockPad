using System;
using System.IO;
using System.Linq;
using System.Text;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// Change master password (user decisions 2026-10-19): the current password is required, the key file is kept,
    /// and the notebook is written twice so both the file and its <c>.bak</c> open only with the new password.
    /// </summary>
    [TestFixture]
    public class ChangeMasterKeyTests
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
            _dir.Dispose();
        }

        private string P { get { return _dir.File("nb.kdbx"); } }
        private string Bak { get { return P + ".bak"; } }

        private static Credentials Pw(string password, string keyFile = null)
        {
            return new Credentials(Encoding.UTF8.GetBytes(password), keyFile);
        }

        private string[] FileNames()
        {
            return Directory.GetFiles(_dir.Path).Select(Path.GetFileName).ToArray();
        }

        private string[] KeptFiles()
        {
            return FileNames().Where(n => n.IndexOf(".kept-", StringComparison.Ordinal) >= 0).ToArray();
        }

        /// <summary>True when <paramref name="path"/> opens with the password (and key file).</summary>
        private static bool Opens(string path, string password, string keyFile = null)
        {
            try
            {
                using (new NotebookStore().Open(path, Pw(password, keyFile))) return true;
            }
            catch (WrongCredentialsException)
            {
                return false;
            }
        }

        private static string[] NoteTexts(string path, string password, string keyFile = null)
        {
            using (Notebook nb = new NotebookStore().Open(path, Pw(password, keyFile)))
                return nb.Notes.Select(n => n.Title + "=" + n.Text).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        /// <summary>A notebook under "old" with one saved note (so a .bak exists, written by this object).</summary>
        private static Notebook CreateSaved(NotebookStore store, string path, Credentials credentials)
        {
            Notebook nb = store.Create(path, credentials, TestKdf.Fast());
            string id = nb.AddNote("t").Id;
            nb.UpdateNote(id, "t", "saved");
            Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            return nb;
        }

        private static void AssertKeyIsDisposed(Credentials c)
        {
            Assert.Throws<ObjectDisposedException>(() => { var k = c.CompositeKey; GC.KeepAlive(k); });
        }

        [Test]
        public void Change_Success_FileAndBakOpenOnlyWithTheNewPassword_IdKept_KeyChangeDateUpdated()
        {
            var store = new NotebookStore();
            Credentials original = Pw("old");
            using (Notebook nb = CreateSaved(store, P, original))
            {
                Assert.That(File.Exists(Bak), Is.True);
                string id = nb.FileNotebookId;
                Assert.That(id, Is.Not.Null);
                nb.Database.MasterKeyChanged = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime before = DateTime.UtcNow.AddSeconds(-2);

                using (Credentials current = Pw("old"))
                {
                    Credentials replacement = Pw("new");
                    ChangeKeyResult r = store.ChangeMasterKey(nb, current, replacement);
                    Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), r.Message);
                    Assert.That(nb.IsDirty, Is.False);
                    Assert.That(nb.CredentialsMatch(Pw("new")), Is.True, "lock/unlock uses the new key");
                    Assert.That(nb.CredentialsMatch(Pw("old")), Is.False);
                    Assert.That(current.CompositeKey, Is.Not.Null, "the caller's current credentials are not disposed");
                    AssertKeyIsDisposed(original);
                }
                Assert.That(nb.FileNotebookId, Is.EqualTo(id));

                Assert.That(Opens(P, "new"), Is.True);
                Assert.That(Opens(P, "old"), Is.False);
                Assert.That(Opens(Bak, "new"), Is.True);
                Assert.That(Opens(Bak, "old"), Is.False);
                Assert.That(NoteTexts(Bak, "new"), Is.EqualTo(new[] { "t=saved" }), ".bak holds the current notes");
                Assert.That(NoteTexts(P, "new"), Is.EqualTo(new[] { "t=saved" }));
                using (Notebook reopened = new NotebookStore().Open(P, Pw("new")))
                {
                    Assert.That(reopened.FileNotebookId, Is.EqualTo(id), "the notebook id is unchanged");
                    Assert.That(reopened.Database.MasterKeyChanged, Is.GreaterThanOrEqualTo(before));
                }
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }), "no kept or temp files");
        }

        [Test]
        public void Change_WithKeyFile_NewPasswordPlusTheSameKeyFileOpensIt_WithoutTheKeyFileItDoesNot()
        {
            string key = _dir.File("key.keyx");
            Credentials.CreateKeyFile(key);
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old", key)))
            using (Credentials current = Pw("old", key))
            {
                ChangeKeyResult r = store.ChangeMasterKey(nb, current, Pw("new", key));
                Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), r.Message);
            }
            Assert.That(Opens(P, "new", key), Is.True);
            Assert.That(Opens(P, "new"), Is.False);
            Assert.That(Opens(P, "old", key), Is.False);
            Assert.That(Opens(Bak, "new", key), Is.True);
            Assert.That(Opens(Bak, "old", key), Is.False);
        }

        [Test]
        public void Change_WrongCurrentPassword_KeyFileMissing_Refused()
        {
            string key = _dir.File("key.keyx");
            Credentials.CreateKeyFile(key);
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old", key)))
            using (Credentials current = Pw("old"))
            using (Credentials replacement = Pw("new", key))
            {
                ChangeKeyResult r = store.ChangeMasterKey(nb, current, replacement);
                Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.WrongCurrentPassword));
                Assert.That(nb.CredentialsMatch(Pw("old", key)), Is.True);
            }
        }

        public enum Refusal
        {
            WrongCurrent,
            SameAsCurrent,
            ExternalChange,
        }

        [TestCase(Refusal.WrongCurrent, ChangeKeyStatus.WrongCurrentPassword)]
        [TestCase(Refusal.SameAsCurrent, ChangeKeyStatus.SameAsCurrent)]
        [TestCase(Refusal.ExternalChange, ChangeKeyStatus.ExternalChangeDetected)]
        public void Change_Refused_NothingWritten_KeyUnchanged(Refusal refusal, ChangeKeyStatus expected)
        {
            var store = new NotebookStore();
            Credentials original = Pw("old");
            using (Notebook nb = CreateSaved(store, P, original))
            {
                string noteId = nb.Notes.Single().Id;
                nb.UpdateNote(noteId, "t", "unsaved");
                if (refusal == Refusal.ExternalChange) File.AppendAllText(P, "x");
                byte[] main = File.ReadAllBytes(P);
                byte[] backup = File.ReadAllBytes(Bak);
                DateTime mainTime = File.GetLastWriteTimeUtc(P);
                DateTime backupTime = File.GetLastWriteTimeUtc(Bak);
                DateTime keyChanged = nb.Database.MasterKeyChanged;
                int writes = 0;
                store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };

                using (Credentials current = Pw(refusal == Refusal.WrongCurrent ? "wrong" : "old"))
                using (Credentials replacement = Pw(refusal == Refusal.SameAsCurrent ? "old" : "new"))
                {
                    ChangeKeyResult r = store.ChangeMasterKey(nb, current, replacement);
                    Assert.That(r.Status, Is.EqualTo(expected));
                    if (refusal == Refusal.ExternalChange)
                        Assert.That(r.Message, Is.EqualTo("The file was changed by another program since it was opened."));
                    Assert.That(replacement.CompositeKey, Is.Not.Null, "the caller keeps its replacement on refusal");
                }

                Assert.That(writes, Is.EqualTo(0));
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(backup));
                Assert.That(File.GetLastWriteTimeUtc(P), Is.EqualTo(mainTime));
                Assert.That(File.GetLastWriteTimeUtc(Bak), Is.EqualTo(backupTime));
                Assert.That(nb.CredentialsMatch(Pw("old")), Is.True, "the in-memory key is unchanged");
                Assert.That(nb.Database.MasterKeyChanged, Is.EqualTo(keyChanged));
                Assert.That(original.CompositeKey, Is.Not.Null, "the notebook's credentials are not disposed");
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }));
        }

        [Test]
        public void Change_ReadOnlyNotebook_Refused()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old"))) { }
            byte[] backup = File.ReadAllBytes(Bak);
            using (Notebook ro = store.Open(Bak, Pw("old")))
            using (Credentials current = Pw("old"))
            using (Credentials replacement = Pw("new"))
            {
                Assert.That(ro.IsReadOnly, Is.True);
                ChangeKeyResult r = store.ChangeMasterKey(ro, current, replacement);
                Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.ReadOnly));
                Assert.That(ro.CredentialsMatch(Pw("old")), Is.True);
            }
            Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(backup));
        }

        [Test]
        public void Change_FirstWriteFails_OldKeyRestored_OldPasswordStillOpens_LaterSaveWorks()
        {
            var store = new NotebookStore();
            Credentials original = Pw("old");
            using (Notebook nb = CreateSaved(store, P, original))
            {
                string noteId = nb.Notes.Single().Id;
                nb.UpdateNote(noteId, "t", "unsaved");
                byte[] main = File.ReadAllBytes(P);
                byte[] backup = File.ReadAllBytes(Bak);
                DateTime keyChanged = nb.Database.MasterKeyChanged;
                store.TestHook = (stage, path) =>
                {
                    if (stage == "after-temp-write") throw new IOException("simulated disk failure");
                };

                Credentials replacement = Pw("new");
                using (Credentials current = Pw("old"))
                {
                    ChangeKeyResult r = store.ChangeMasterKey(nb, current, replacement);
                    Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Failed));
                    Assert.That(r.Message, Does.Contain("simulated disk failure"));
                }
                Assert.That(replacement.CompositeKey, Is.Not.Null, "the caller keeps its replacement on failure");
                replacement.Dispose();
                Assert.That(nb.CredentialsMatch(Pw("old")), Is.True, "the old key is restored");
                Assert.That(nb.Database.MasterKeyChanged, Is.EqualTo(keyChanged));
                Assert.That(original.CompositeKey, Is.Not.Null);
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(backup));
                Assert.That(Opens(P, "old"), Is.True);
                Assert.That(Opens(P, "new"), Is.False);

                store.TestHook = null;
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "a later save uses the old key");
                Assert.That(NoteTexts(P, "old"), Is.EqualTo(new[] { "t=unsaved" }));
            }
            Assert.That(Directory.GetFiles(_dir.Path, "*.tmp"), Is.Empty);
        }

        [Test]
        public void Change_FirstReplaceFailsHalfway_FailedWhileReplacing_TheReasonSaysTheKeptCopyUsesTheNewPassword()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                // ERROR_UNABLE_TO_MOVE_REPLACEMENT_2: the original was renamed to the .bak, the new copy stayed at temp.
                store.ReplaceFile = (temp, target, backup) =>
                {
                    File.Delete(backup);
                    File.Move(target, backup);
                    throw new IOException("Unable to move the replacement file to the file to be replaced.");
                };
                Credentials replacement = Pw("new");
                using (Credentials current = Pw("old"))
                {
                    ChangeKeyResult r = store.ChangeMasterKey(nb, current, replacement);
                    Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.FailedWhileReplacing));
                    Assert.That(r.Message, Does.Contain("The new copy was kept at "));
                    Assert.That(r.Message, Does.EndWith(" That new copy opens only with the new password."));
                }
                replacement.Dispose();
                Assert.That(nb.CredentialsMatch(Pw("old")), Is.True);
            }
            string kept = Directory.GetFiles(_dir.Path, "*.tmp").Single();
            Assert.That(Opens(kept, "new"), Is.True);
            Assert.That(Opens(Bak, "old"), Is.True, "the previous version");
        }

        [Test]
        public void Change_ReplaceFailsWithEveryFileInPlace_PlainFailed()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                byte[] main = File.ReadAllBytes(P);
                // ERROR_UNABLE_TO_REMOVE_REPLACED: ReplaceFile changed nothing; the temp copy is deleted.
                store.ReplaceFile = (temp, target, backup) => { throw new IOException("Unable to remove the file to be replaced."); };
                Credentials replacement = Pw("new");
                using (Credentials current = Pw("old"))
                    Assert.That(store.ChangeMasterKey(nb, current, replacement).Status, Is.EqualTo(ChangeKeyStatus.Failed));
                replacement.Dispose();
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
            }
            Assert.That(Directory.GetFiles(_dir.Path, "*.tmp"), Is.Empty);
        }

        [Test]
        public void Change_BakHeldOpenByAnotherProgram_Failed_OldKeyKept_NothingChanged()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                byte[] main = File.ReadAllBytes(P);
                byte[] backup = File.ReadAllBytes(Bak);
                Credentials replacement = Pw("new");
                using (new FileStream(Bak, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Credentials current = Pw("old"))
                {
                    ChangeKeyResult r = store.ChangeMasterKey(nb, current, replacement);
                    Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Failed));
                    Assert.That(r.Message, Does.Contain("nb.kdbx.bak"));
                }
                replacement.Dispose();
                Assert.That(nb.CredentialsMatch(Pw("old")), Is.True);
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(backup));
            }
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }));
        }

        [Test]
        public void Change_SecondWriteFails_ChangedButBackupNotRelocked_MainNew_BakOld()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                int writes = 0;
                store.TestHook = (stage, path) =>
                {
                    if (stage == "after-temp-write") writes++;
                    if (stage == "before-replace" && writes == 2) throw new IOException("simulated second failure");
                };
                using (Credentials current = Pw("old"))
                {
                    ChangeKeyResult r = store.ChangeMasterKey(nb, current, Pw("new"));
                    Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
                    Assert.That(r.Message, Does.Contain("simulated second failure"));
                }
                Assert.That(writes, Is.EqualTo(2));
                Assert.That(nb.CredentialsMatch(Pw("new")), Is.True, "the main file uses the new key, so the notebook does");
                Assert.That(nb.IsDirty, Is.True, "the .bak re-lock is pending (fix round 2, X3)");
                Assert.That(nb.NeedsWrite, Is.True);
                Assert.That(nb.PreviousCredentials, Is.Not.Null);
                Assert.That(nb.PreviousCredentials.Matches(Pw("old")), Is.True, "the old key is kept to prove the .bak");

                store.TestHook = null;
                nb.AddNote("later");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "the next save works with the new key");
            }
            Assert.That(Opens(P, "new"), Is.True);
            Assert.That(KeptFiles(), Is.Empty);
        }

        [Test]
        public void Change_SecondWriteFails_BakStillOpensWithTheOldPassword()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                int writes = 0;
                store.TestHook = (stage, path) =>
                {
                    if (stage == "after-temp-write") writes++;
                    if (stage == "before-replace" && writes == 2) throw new IOException("simulated second failure");
                };
                using (Credentials current = Pw("old"))
                    Assert.That(store.ChangeMasterKey(nb, current, Pw("new")).Status,
                        Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
            }
            Assert.That(Opens(P, "new"), Is.True);
            Assert.That(Opens(P, "old"), Is.False);
            Assert.That(Opens(Bak, "old"), Is.True);
            Assert.That(Opens(Bak, "new"), Is.False);
        }

        [Test]
        public void Change_OwnBakFromAnEarlierSession_IsRelocked_NotKeptAside()
        {
            using (Notebook first = CreateSaved(new NotebookStore(), P, Pw("old"))) { }
            Assert.That(File.Exists(Bak), Is.True);

            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw("old")))
            using (Credentials current = Pw("old"))
            {
                ChangeKeyResult r = store.ChangeMasterKey(nb, current, Pw("new"));
                Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), r.Message);
                Assert.That(nb.TakeBackupNotice(), Is.Null);
            }
            Assert.That(KeptFiles(), Is.Empty);
            Assert.That(FileNames(), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }));
            Assert.That(Opens(Bak, "new"), Is.True);
            Assert.That(Opens(Bak, "old"), Is.False);
        }

        [Test]
        public void Change_ForeignBak_KeptAsideAsToday_ChangeSucceeds()
        {
            string other = _dir.File("other.kdbx");
            var otherStore = new NotebookStore();
            using (Notebook o = otherStore.Create(other, Pw("other"), TestKdf.Fast())) { }
            using (Notebook mine = new NotebookStore().Create(P, Pw("old"), TestKdf.Fast())) { }
            File.Move(other, Bak);
            byte[] foreign = File.ReadAllBytes(Bak);

            var store = new NotebookStore();
            using (Notebook nb = store.Open(P, Pw("old")))
            using (Credentials current = Pw("old"))
            {
                ChangeKeyResult r = store.ChangeMasterKey(nb, current, Pw("new"));
                Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), r.Message);
                Assert.That(nb.TakeBackupNotice(), Does.StartWith("An existing file \"nb.kdbx.bak\" was not created by this notebook."));
            }
            string kept = KeptFiles().Single();
            Assert.That(File.ReadAllBytes(_dir.File(kept)), Is.EqualTo(foreign), "the foreign file is kept byte for byte");
            Assert.That(Opens(P, "new"), Is.True);
            Assert.That(Opens(Bak, "new"), Is.True);
            Assert.That(Opens(Bak, "old"), Is.False);
        }

        [Test]
        public void Change_DirtyNotes_AreSavedInTheFirstWrite()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                nb.AddNote("pending");
                Assert.That(nb.IsDirty, Is.True);
                using (Credentials current = Pw("old"))
                    Assert.That(store.ChangeMasterKey(nb, current, Pw("new")).Status, Is.EqualTo(ChangeKeyStatus.Changed));
                Assert.That(nb.IsDirty, Is.False);
            }
            Assert.That(NoteTexts(P, "new"), Is.EqualTo(new[] { "pending=", "t=saved" }));
            Assert.That(NoteTexts(Bak, "new"), Is.EqualTo(new[] { "pending=", "t=saved" }));
        }

        [Test]
        public void Change_CleanNotebook_BothWritesHappen()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                Assert.That(nb.IsDirty, Is.False);
                int writes = 0;
                store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };
                using (Credentials current = Pw("old"))
                    Assert.That(store.ChangeMasterKey(nb, current, Pw("new")).Status, Is.EqualTo(ChangeKeyStatus.Changed));
                Assert.That(writes, Is.EqualTo(2), "the second write is forced, never skipped as identical");
            }
        }

        [Test]
        public void AfterChange_EditAndSaveWork_IdenticalContentStillWritesNothing()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                using (Credentials current = Pw("old"))
                    Assert.That(store.ChangeMasterKey(nb, current, Pw("new")).Status, Is.EqualTo(ChangeKeyStatus.Changed));
                byte[] relocked = File.ReadAllBytes(P);

                string id = nb.Notes.Single().Id;
                nb.UpdateNote(id, "t", "after");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(NoteTexts(P, "new"), Is.EqualTo(new[] { "t=after" }));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(relocked), "the .bak is the version before this save");
                Assert.That(KeptFiles(), Is.Empty, "the re-locked .bak is recognized as own");

                byte[] main = File.ReadAllBytes(P);
                byte[] backup = File.ReadAllBytes(Bak);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "clean: nothing to write");
                nb.UpdateNote(id, "t", "typed");
                nb.UpdateNote(id, "t", "after");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "identical content");
                Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
                Assert.That(File.ReadAllBytes(Bak), Is.EqualTo(backup));
            }
        }

        [Test]
        public void AfterChange_ReopenedInANewSession_SaveRecognizesTheRelockedBakAsOwn()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            using (Credentials current = Pw("old"))
                Assert.That(store.ChangeMasterKey(nb, current, Pw("new")).Status, Is.EqualTo(ChangeKeyStatus.Changed));

            var later = new NotebookStore();
            using (Notebook nb = later.Open(P, Pw("new")))
            {
                nb.AddNote("next session");
                Assert.That(later.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(nb.TakeBackupNotice(), Is.Null);
            }
            Assert.That(KeptFiles(), Is.Empty);
        }

        // ---- fix round 2: the .bak re-lock is retried until it succeeds (X3; red team H1, M2; review B3, B4) ----

        /// <summary>Every file in the folder (temp and kept copies included) that opens with <paramref name="password"/>.</summary>
        private string[] FilesOpeningWith(string password)
        {
            return Directory.GetFiles(_dir.Path)
                .Where(f => { try { return Opens(f, password); } catch (Exception) { return false; } })
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        /// <summary>The second write fails before its swap (e.g. antivirus holds the fresh .bak).</summary>
        private static void FailSecondWrite(NotebookStore store)
        {
            int writes = 0;
            store.TestHook = (stage, path) =>
            {
                if (stage == "after-temp-write") writes++;
                if (stage == "before-replace" && writes == 2) throw new IOException("simulated second failure");
            };
        }

        private static void ChangeWithSecondWriteFailing(NotebookStore store, Notebook nb, string from, string to)
        {
            FailSecondWrite(store);
            using (Credentials current = Pw(from))
                Assert.That(store.ChangeMasterKey(nb, current, Pw(to)).Status,
                    Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
            store.TestHook = null;
        }

        [Test]
        public void Success_ThePreviousKeyIsReleased_NoWritePending()
        {
            var store = new NotebookStore();
            Credentials original = Pw("old");
            using (Notebook nb = CreateSaved(store, P, original))
            {
                using (Credentials current = Pw("old"))
                    Assert.That(store.ChangeMasterKey(nb, current, Pw("new")).Status, Is.EqualTo(ChangeKeyStatus.Changed));
                Assert.That(nb.PreviousCredentials, Is.Null);
                Assert.That(nb.NeedsWrite, Is.False);
                Assert.That(nb.IsDirty, Is.False);
                AssertKeyIsDisposed(original);
            }
        }

        /// <summary>Red team RT1a: after ChangedButBackupNotRelocked, Ctrl+S (no edits) re-locks the .bak.</summary>
        [Test]
        public void AfterBackupNotRelocked_CtrlS_WritesAndRelocksTheBak_ThenReleasesThePreviousKey()
        {
            var store = new NotebookStore();
            Credentials original = Pw("old");
            using (Notebook nb = CreateSaved(store, P, original))
            {
                ChangeWithSecondWriteFailing(store, nb, "old", "new");
                Assert.That(Opens(Bak, "old"), Is.True, "not re-locked yet");
                int writes = 0;
                store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };

                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

                Assert.That(writes, Is.EqualTo(1), "identical notes, but the re-lock write is not skipped");
                Assert.That(nb.IsDirty, Is.False);
                Assert.That(nb.NeedsWrite, Is.False);
                Assert.That(nb.PreviousCredentials, Is.Null, "released after the re-lock");
                AssertKeyIsDisposed(original);
            }
            Assert.That(Opens(Bak, "old"), Is.False);
            Assert.That(Opens(Bak, "new"), Is.True);
            Assert.That(FilesOpeningWith("old"), Is.Empty);
            Assert.That(KeptFiles(), Is.Empty);
        }

        [Test]
        public void AfterBackupNotRelocked_TheSaveProvesTheBakWithThePreviousKey_WhenItsMemoryIsGone()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                ChangeWithSecondWriteFailing(store, nb, "old", "new");
                nb.OwnBackupPath = null; // e.g. its fingerprint could not be read right after the first swap
                nb.OwnBackupFingerprint = null;

                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(nb.TakeBackupNotice(), Is.Null);
            }
            Assert.That(KeptFiles(), Is.Empty, "the old-key .bak is proven, not kept aside");
            Assert.That(FilesOpeningWith("old"), Is.Empty);
        }

        [Test]
        public void AfterBackupNotRelocked_ASecondChange_LeavesNoFileWithEitherOldPassword()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("a")))
            {
                ChangeWithSecondWriteFailing(store, nb, "a", "b");
                using (Credentials current = Pw("b"))
                    Assert.That(store.ChangeMasterKey(nb, current, Pw("c")).Status, Is.EqualTo(ChangeKeyStatus.Changed));
                Assert.That(nb.PreviousCredentials, Is.Null);
            }
            Assert.That(FilesOpeningWith("a"), Is.Empty);
            Assert.That(FilesOpeningWith("b"), Is.Empty);
            Assert.That(FilesOpeningWith("c"), Is.EquivalentTo(new[] { "nb.kdbx", "nb.kdbx.bak" }));
        }

        [Test]
        public void AfterBackupNotRelocked_Dispose_ReleasesThePreviousKey()
        {
            var store = new NotebookStore();
            Credentials original = Pw("old");
            Notebook nb = CreateSaved(store, P, original);
            ChangeWithSecondWriteFailing(store, nb, "old", "new");
            nb.Dispose();
            AssertKeyIsDisposed(original);
        }

        /// <summary>Accepted residual (fix round 2, X3): if LockPad is killed before the re-lock save, the next session
        /// cannot prove the old-key .bak and keeps it aside, still opening with the old password. The notice keeps its
        /// wording (a contained neutral wording was not possible: the next session cannot tell this file from a
        /// foreign one).</summary>
        [Test]
        public void AfterBackupNotRelocked_KilledBeforeTheSave_NextSessionKeepsTheOldKeyBakAside_Residual()
        {
            var store = new NotebookStore();
            Notebook nb = CreateSaved(store, P, Pw("old"));
            ChangeWithSecondWriteFailing(store, nb, "old", "new");
            nb.Dispose(); // LockPad killed: no lock, no close, no save

            var later = new NotebookStore();
            string notice;
            using (Notebook next = later.Open(P, Pw("new")))
            {
                next.AddNote("next session");
                Assert.That(later.Save(next, false).Status, Is.EqualTo(SaveStatus.Saved));
                notice = next.TakeBackupNotice();
            }
            string kept = KeptFiles().Single();
            Assert.That(FilesOpeningWith("old"), Is.EqualTo(new[] { kept }));
            Assert.That(notice, Does.Contain("was not created by this notebook"));
        }

        /// <summary>Red team RT5: the fresh old-key .bak is held without sharing right after the first swap (its
        /// fingerprint cannot be read), and released before the second write looks at it.</summary>
        [Test]
        public void FreshBakBrieflyHeldBetweenTheWrites_ProvenWithThePreviousKey_Changed_NoOldPasswordCopy()
        {
            var store = new NotebookStore();
            ChangeKeyResult r;
            string notice;
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                FileStream scanner = null;
                int replaces = 0, writes = 0;
                store.ReplaceFile = (temp, target, backup) =>
                {
                    File.Replace(temp, target, backup, true);
                    replaces++;
                    if (replaces == 1) scanner = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.None);
                };
                store.TestHook = (stage, path) =>
                {
                    if (stage == "after-temp-write" && ++writes == 2 && scanner != null)
                    {
                        scanner.Dispose();
                        scanner = null;
                    }
                };
                using (Credentials current = Pw("old"))
                    r = store.ChangeMasterKey(nb, current, Pw("new"));
                notice = nb.TakeBackupNotice();
            }
            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), r.Message);
            Assert.That(notice, Is.Null);
            Assert.That(KeptFiles(), Is.Empty);
            Assert.That(FilesOpeningWith("old"), Is.Empty);
        }

        // ---- fix round 2: a half-finished swap in the SECOND write (X4; red team M1; review B2) ----

        /// <summary>Red team RT2 (Core part).</summary>
        [Test]
        public void SecondReplaceFailsHalfway_ChangedButReplaceIncomplete_EveryFileNewPassword_WritePending()
        {
            var store = new NotebookStore();
            Credentials original = Pw("old");
            using (Notebook nb = CreateSaved(store, P, original))
            {
                int replaces = 0;
                store.ReplaceFile = (temp, target, backup) =>
                {
                    replaces++;
                    if (replaces == 1) { File.Replace(temp, target, backup, true); return; }
                    File.Delete(backup);
                    File.Move(target, backup);
                    throw new IOException("Unable to move the replacement file to the file to be replaced.");
                };
                ChangeKeyResult r;
                using (Credentials current = Pw("old"))
                    r = store.ChangeMasterKey(nb, current, Pw("new"));

                Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.ChangedButReplaceIncomplete));
                Assert.That(r.Message, Does.Contain("The new copy was kept at "));
                Assert.That(File.Exists(P), Is.False, "the notebook file is missing");
                Assert.That(nb.CredentialsMatch(Pw("new")), Is.True);
                Assert.That(nb.IsDirty, Is.True, "a lock or close must not drop the notes in memory");
                Assert.That(nb.PreviousCredentials, Is.Null, "no file uses the old key any more");
                AssertKeyIsDisposed(original);

                store.ReplaceFile = null;
                Assert.That(store.Save(nb, false).Status, Is.Not.EqualTo(SaveStatus.Saved), "the file is missing");
                Assert.That(nb.IsDirty, Is.True, "still pending");
            }
            Assert.That(FilesOpeningWith("old"), Is.Empty);
            string keptCopy = Directory.GetFiles(_dir.Path, "*.tmp").Single();
            Assert.That(Opens(keptCopy, "new"), Is.True);
            Assert.That(Opens(Bak, "new"), Is.True);
        }

        [Test]
        public void AfterReplaceIncomplete_SaveAs_ClearsThePendingWrite()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                int replaces = 0;
                store.ReplaceFile = (temp, target, backup) =>
                {
                    replaces++;
                    if (replaces == 1) { File.Replace(temp, target, backup, true); return; }
                    File.Delete(backup);
                    File.Move(target, backup);
                    throw new IOException("Unable to move the replacement file to the file to be replaced.");
                };
                using (Credentials current = Pw("old"))
                    Assert.That(store.ChangeMasterKey(nb, current, Pw("new")).Status,
                        Is.EqualTo(ChangeKeyStatus.ChangedButReplaceIncomplete));
                store.ReplaceFile = null;

                Assert.That(store.SaveAs(nb, _dir.File("rescued.kdbx")).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(nb.IsDirty, Is.False);
            }
            Assert.That(NoteTexts(_dir.File("rescued.kdbx"), "new"), Is.EqualTo(new[] { "t=saved" }));
        }

        // ---- fix round 3: Save As while the .bak re-lock is pending reports the old .bak (Y1; red team RM1) ----

        [Test]
        public void AfterBackupNotRelocked_SaveAs_ReportsTheOldBak_TouchesNothingAtTheOldLocation()
        {
            var store = new NotebookStore();
            SaveResult r;
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                ChangeWithSecondWriteFailing(store, nb, "old", "new");
                r = store.SaveAs(nb, _dir.File("moved.kdbx"));
                Assert.That(nb.PreviousCredentials, Is.Null, "the pending re-lock is cleared");
                Assert.That(nb.IsDirty, Is.False);
            }
            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(r.UnrelockedBackupPath, Is.EqualTo(Bak));
            Assert.That(Opens(Bak, "old"), Is.True, "not deleted or rewritten");
            Assert.That(Opens(P, "new"), Is.True);
        }

        [Test]
        public void SaveAs_NoPendingRelock_ReportsNoOldBak()
        {
            var store = new NotebookStore();
            SaveResult r;
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                nb.AddNote("more");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(File.Exists(Bak), Is.True);
                r = store.SaveAs(nb, _dir.File("moved.kdbx"));
            }
            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(r.UnrelockedBackupPath, Is.Null);
            Assert.That(SaveResult.Ok().UnrelockedBackupPath, Is.Null);
        }

        [Test]
        public void AfterBackupNotRelocked_OldBakGone_SaveAs_ReportsNoOldBak()
        {
            var store = new NotebookStore();
            SaveResult r;
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                ChangeWithSecondWriteFailing(store, nb, "old", "new");
                File.Delete(Bak);
                r = store.SaveAs(nb, _dir.File("moved.kdbx"));
            }
            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(r.UnrelockedBackupPath, Is.Null);
        }

        /// <summary>The re-lock save's swap stopped half-way: the new-key file took the .bak name, so the old-key .bak
        /// is gone. A later Save As must not claim the .bak still opens with the old password.</summary>
        [Test]
        public void AfterBackupNotRelocked_ReLockSaveStopsHalfway_OldKeyBakGone_SaveAsReportsNoOldBak()
        {
            var store = new NotebookStore();
            SaveResult r;
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            {
                ChangeWithSecondWriteFailing(store, nb, "old", "new");
                store.ReplaceFile = (temp, target, backup) =>
                {
                    File.Delete(backup);
                    File.Move(target, backup);
                    throw new IOException("Unable to move the replacement file to the file to be replaced.");
                };
                SaveResult failed = store.Save(nb, false);
                Assert.That(failed.Status, Is.Not.EqualTo(SaveStatus.Saved));
                Assert.That(failed.Message, Does.Contain("The new copy was kept at "));
                store.ReplaceFile = null;
                Assert.That(nb.PreviousCredentials, Is.Null, "no file uses the old key any more");
                Assert.That(nb.IsDirty, Is.True, "the notebook file is missing: a write is still pending");

                r = store.SaveAs(nb, _dir.File("moved.kdbx"));
            }
            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(r.UnrelockedBackupPath, Is.Null);
            Assert.That(FilesOpeningWith("old"), Is.Empty);
            Assert.That(Opens(Bak, "new"), Is.True);
        }

        // ---- fix round 2: test gaps (review B7) ----

        [Test]
        public void FirstWriteFinalRecheckSeesAnExternalChange_OldKeyRestored()
        {
            var store = new NotebookStore();
            Credentials original = Pw("old");
            using (Notebook nb = CreateSaved(store, P, original))
            {
                DateTime keyChanged = nb.Database.MasterKeyChanged;
                bool changed = false;
                store.TestHook = (stage, path) =>
                {
                    if (stage == "before-replace" && !changed)
                    {
                        changed = true;
                        File.AppendAllText(P, "x"); // another program writes during the key derivations
                    }
                };
                Credentials replacement = Pw("new");
                using (Credentials current = Pw("old"))
                {
                    ChangeKeyResult r = store.ChangeMasterKey(nb, current, replacement);
                    Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.ExternalChangeDetected));
                }
                replacement.Dispose();
                Assert.That(nb.CredentialsMatch(Pw("old")), Is.True, "the old key is restored");
                Assert.That(nb.Database.MasterKeyChanged, Is.EqualTo(keyChanged));
                Assert.That(nb.PreviousCredentials, Is.Null);
                Assert.That(nb.NeedsWrite, Is.False);
                Assert.That(original.CompositeKey, Is.Not.Null);
            }
            Assert.That(Opens(Bak, "old"), Is.True, "the .bak is untouched");
        }

        private static KeePassLib.Cryptography.KeyDerivation.KdfParameters Argon2(ulong memory, ulong iterations, uint lanes)
        {
            var p = new KeePassLib.Cryptography.KeyDerivation.Argon2Kdf(
                KeePassLib.Cryptography.KeyDerivation.Argon2Type.ID).GetDefaultParameters();
            p.SetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamMemory, memory);
            p.SetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamIterations, iterations);
            p.SetUInt32(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamParallelism, lanes);
            return p;
        }

        /// <summary>The notebook's KDF is below the floor and lighter than its own .bak's (lowered by another client):
        /// the change still ends with the .bak proven and re-locked, not kept aside (an outcome test: since fix round 3,
        /// Y3, the proof may come from the pre-check or from the writes' previous-key fallback).</summary>
        [Test]
        public void OwnBakUnderABelowFloorKdf_IsRelocked_NotKeptAside()
        {
            // Session 1 (relaxed suite policy): the .bak gets 3 MiB / 2 iterations / 2 lanes, the file 1 MiB / 1 / 1.
            var first = new NotebookStore();
            using (Notebook nb = first.Create(P, Pw("old"), Argon2(3UL * 1024 * 1024, 2, 2)))
            {
                nb.AddNote("one");
                Assert.That(first.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                nb.Database.KdfParameters = Argon2(1024 * 1024, 1, 1);
                nb.AddNote("two");
                Assert.That(first.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }

            bool accept = SaveFormatPolicy.AcceptWeakKdfForTests;
            var fresh = SaveFormatPolicy.FreshKdf;
            SaveFormatPolicy.AcceptWeakKdfForTests = false;
            SaveFormatPolicy.FreshKdf = () => KdfTuner.Argon2id(1);
            try
            {
                var store = new NotebookStore();
                using (Notebook nb = store.Open(P, Pw("old")))
                using (Credentials current = Pw("old"))
                {
                    ChangeKeyResult r = store.ChangeMasterKey(nb, current, Pw("new"));
                    Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), r.Message);
                    Assert.That(nb.TakeBackupNotice(), Is.Null);
                }
            }
            finally
            {
                SaveFormatPolicy.AcceptWeakKdfForTests = accept;
                SaveFormatPolicy.FreshKdf = fresh;
            }
            Assert.That(KeptFiles(), Is.Empty);
            Assert.That(FilesOpeningWith("old"), Is.Empty);
        }

        [Test]
        public void Change_NullArguments_Throw()
        {
            var store = new NotebookStore();
            using (Notebook nb = CreateSaved(store, P, Pw("old")))
            using (Credentials c = Pw("old"))
            {
                Assert.Throws<ArgumentNullException>(() => store.ChangeMasterKey(null, c, c));
                Assert.Throws<ArgumentNullException>(() => store.ChangeMasterKey(nb, null, c));
                Assert.Throws<ArgumentNullException>(() => store.ChangeMasterKey(nb, c, null));
            }
        }
    }
}

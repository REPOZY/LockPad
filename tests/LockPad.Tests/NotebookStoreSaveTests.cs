using System;
using System.IO;
using System.Linq;
using System.Text;
using LockPad.Core;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class NotebookStoreSaveTests
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

        private static Credentials Pw(string password = "pw", string keyFile = null)
        {
            return new Credentials(Encoding.UTF8.GetBytes(password), keyFile);
        }

        private string CreateNotebook(string name, string password = "pw", string keyFile = null)
        {
            string p = _dir.File(name);
            using (new NotebookStore().Create(p, Pw(password, keyFile), TestKdf.Fast())) { }
            return p;
        }

        private static PwDatabase RawOpen(string path, string password = "pw")
        {
            using (var c = Pw(password))
            {
                var db = new PwDatabase();
                db.Open(IOConnectionInfo.FromPath(path), c.CompositeKey, null);
                return db;
            }
        }

        private static PwGroup NotesGroup(PwDatabase db)
        {
            return db.RootGroup.Groups.First(g => g.Name == "Notes");
        }

        private string[] TempFiles()
        {
            return Directory.GetFiles(_dir.Path, "*.tmp");
        }

        private static void BuildUnmarkedKdbx(string p)
        {
            using (var c = Pw())
            {
                var db = new PwDatabase();
                db.New(IOConnectionInfo.FromPath(p), c.CompositeKey);
                db.KdfParameters = TestKdf.Fast();
                var e = new PwEntry(true, true);
                e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "bank"));
                e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "SECRET-PW"));
                e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "n"));
                db.RootGroup.AddEntry(e, true);
                db.SaveAs(IOConnectionInfo.FromPath(p), false, null);
                db.Close();
            }
        }

        /// <summary>Opens <paramref name="p"/>, adds one note with the given content and saves it.</summary>
        private static string SeedNote(NotebookStore store, string p, string title, string text)
        {
            using (Notebook nb = store.Open(p, Pw()))
            {
                string id = nb.AddNote(title).Id;
                nb.UpdateNote(id, title, text);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                return id;
            }
        }

        [Test]
        public void Save_RoundTripsContent()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            const string text = "line1\r\nline2\n\tTab \uD83D\uDE00 \u2116 \u202E RTL";
            string id1, id2;
            using (Notebook nb = store.Open(p, Pw()))
            {
                id1 = nb.AddNote("First \u2116 \uD83D\uDE00").Id;
                nb.UpdateNote(id1, "First \u2116 \uD83D\uDE00", text);
                id2 = nb.AddNote("Second").Id;
                nb.UpdateNote(id2, "Second", "plain");
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(nb.IsDirty, Is.False);
                Assert.That(nb.LoadedFingerprint.SameContentAs(FileFingerprint.Compute(p)), Is.True);
            }
            using (Notebook nb = store.Open(p, Pw()))
            {
                Assert.That(nb.Notes.Count, Is.EqualTo(2));
                Assert.That(nb.FindNote(id1).Title, Is.EqualTo("First \u2116 \uD83D\uDE00"));
                Assert.That(string.Equals(nb.FindNote(id1).Text, text, StringComparison.Ordinal), Is.True);
                Assert.That(nb.FindNote(id2).Title, Is.EqualTo("Second"));
                Assert.That(nb.FindNote(id2).Text, Is.EqualTo("plain"));
            }
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_ReadOnlyNotebook_ReturnsReadOnly()
        {
            string p = _dir.File("keepass.kdbx");
            BuildUnmarkedKdbx(p);
            byte[] before = File.ReadAllBytes(p);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                Assert.That(nb.IsReadOnly, Is.True);
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.ReadOnly));
                Assert.That(r.Message, Is.EqualTo("This notebook is read-only."));
                Assert.That(store.Save(nb, true).Status, Is.EqualTo(SaveStatus.ReadOnly));
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
            Assert.That(File.Exists(NotebookStore.BackupPath(p)), Is.False);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_KeepsOneBackup_ThePreviousVersion()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                string id = nb.AddNote("t").Id;
                for (int i = 1; i <= 4; i++)
                {
                    nb.UpdateNote(id, "t", "save " + i);
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "save " + i);
                }
            }
            Assert.That(Directory.GetFiles(_dir.Path).Select(Path.GetFileName), Is.EquivalentTo(new[] { "a.kdbx", "a.kdbx.bak" }));
            using (Notebook bak = store.Open(NotebookStore.BackupPath(p), Pw()))
                Assert.That(bak.Notes.Single().Text, Is.EqualTo("save 3"));
            using (Notebook nb = store.Open(p, Pw()))
                Assert.That(nb.Notes.Single().Text, Is.EqualTo("save 4"));
        }

        [Test]
        public void Save_UpdatedNote_HistoryCappedAtTen()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            string id = SeedNote(store, p, "t", "v0");
            using (Notebook nb = store.Open(p, Pw()))
            {
                for (int i = 1; i <= 12; i++)
                {
                    nb.UpdateNote(id, "t", "v" + i);
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "cycle " + i);
                }
            }
            PwDatabase db = RawOpen(p);
            try
            {
                PwEntry e = NotesGroup(db).Entries.Single();
                Assert.That(e.Uuid.ToHexString(), Is.EqualTo(id));
                Assert.That(e.History.UCount, Is.EqualTo(10u));
                Assert.That(e.Strings.ReadSafe(PwDefs.NotesField), Is.EqualTo("v12"));
                // Newest backup holds the previous value; the oldest two (v0, v1) were dropped.
                Assert.That(e.History.GetAt(9).Strings.ReadSafe(PwDefs.NotesField), Is.EqualTo("v11"));
                Assert.That(e.History.GetAt(0).Strings.ReadSafe(PwDefs.NotesField), Is.EqualTo("v2"));
            }
            finally { db.Close(); }
        }

        [Test]
        public void Save_UnchangedNote_NoHistoryEntry()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            string id = SeedNote(store, p, "keep", "same");
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("other");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            PwDatabase db = RawOpen(p);
            try
            {
                PwEntry e = NotesGroup(db).Entries.First(x => x.Uuid.ToHexString() == id);
                Assert.That(e.History.UCount, Is.EqualTo(0u));
            }
            finally { db.Close(); }
        }

        [Test]
        public void Save_RemovedNote_GoneAfterReopen()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            string keep, gone;
            using (Notebook nb = store.Open(p, Pw()))
            {
                keep = nb.AddNote("keep").Id;
                gone = nb.AddNote("gone").Id;
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                nb.RemoveNote(gone);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                Assert.That(nb.IsDirty, Is.False);
            }
            using (Notebook nb = store.Open(p, Pw()))
                Assert.That(nb.Notes.Select(n => n.Id), Is.EqualTo(new[] { keep }));
            PwDatabase db = RawOpen(p);
            try
            {
                Assert.That(db.DeletedObjects.Count(d => d.Uuid.ToHexString() == gone), Is.EqualTo(1));
            }
            finally { db.Close(); }
        }

        [Test]
        public void Save_AddedThenRemovedUnsavedNote_NoTrace()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            string temp;
            using (Notebook nb = store.Open(p, Pw()))
            {
                temp = nb.AddNote("never saved").Id;
                nb.RemoveNote(temp);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            PwDatabase db = RawOpen(p);
            try
            {
                Assert.That(NotesGroup(db).Entries.UCount, Is.EqualTo(0u));
                Assert.That(db.DeletedObjects.Any(d => d.Uuid.ToHexString() == temp), Is.False);
            }
            finally { db.Close(); }
        }

        [Test]
        public void Save_ExternalChange_Detected_NoWrite()
        {
            string p = CreateNotebook("a.kdbx");
            string other = CreateNotebook("other.kdbx");
            SeedNote(new NotebookStore(), other, "external", "theirs");
            byte[] external = File.ReadAllBytes(other);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                File.WriteAllBytes(p, external);
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
                Assert.That(r.Message, Is.EqualTo("The file was changed by another program since it was opened."));
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(external));
            Assert.That(File.Exists(NotebookStore.BackupPath(p)), Is.False);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_ExternalChange_Overwrite_Saves()
        {
            string p = CreateNotebook("a.kdbx");
            // The same notebook, changed elsewhere (a different notebook is refused: full-check red team M1).
            string other = _dir.File("other.kdbx");
            File.Copy(p, other);
            SeedNote(new NotebookStore(), other, "external", "theirs");
            byte[] external = File.ReadAllBytes(other);
            var store = new NotebookStore();
            string id;
            using (Notebook nb = store.Open(p, Pw()))
            {
                id = nb.AddNote("ours").Id;
                nb.UpdateNote(id, "ours", "our text");
                File.WriteAllBytes(p, external);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
                SaveResult r = store.Save(nb, true);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(nb.IsDirty, Is.False);
                Assert.That(nb.LoadedFingerprint.SameContentAs(FileFingerprint.Compute(p)), Is.True);
            }
            using (Notebook nb = store.Open(p, Pw()))
            {
                Assert.That(nb.Notes.Single().Id, Is.EqualTo(id));
                Assert.That(nb.Notes.Single().Text, Is.EqualTo("our text"));
            }
            // The overwritten external version is kept as the .bak.
            Assert.That(File.ReadAllBytes(NotebookStore.BackupPath(p)), Is.EqualTo(external));
        }

        [Test]
        public void Save_ExternalChange_Overwrite_UnmarkedFile_RefusedFileUntouched()
        {
            // Global constraint: LockPad never writes to a file that lacks the marker, even when the user chose Overwrite.
            string p = CreateNotebook("a.kdbx");
            string foreign = _dir.File("keepass.kdbx");
            BuildUnmarkedKdbx(foreign);
            byte[] external = File.ReadAllBytes(foreign);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                File.WriteAllBytes(p, external);
                SaveResult r = store.Save(nb, true);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo(
                    "The file on disk is no longer a LockPad notebook that opens with this password, so it was not overwritten. Use Save As to keep your changes."));
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(external));
            Assert.That(File.Exists(NotebookStore.BackupPath(p)), Is.False);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_ExternalChange_Overwrite_GarbageFile_RefusedFileUntouched()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                File.WriteAllText(p, "garbage");
                Assert.That(store.Save(nb, true).Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(File.ReadAllText(p), Is.EqualTo("garbage"));
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_ExternalChange_Overwrite_DifferentPassword_RefusedFileUntouched()
        {
            string p = CreateNotebook("a.kdbx");
            string other = CreateNotebook("other.kdbx", "different");
            byte[] external = File.ReadAllBytes(other);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                File.WriteAllBytes(p, external);
                Assert.That(store.Save(nb, true).Status, Is.EqualTo(SaveStatus.IoError));
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(external));
        }

        [Test]
        public void Save_TargetDeleted_IoError_NotRecreated([Values(false, true)] bool overwrite)
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                File.Delete(p);
                SaveResult r = store.Save(nb, overwrite);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("File not found."));
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(File.Exists(p), Is.False);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_TargetLockedDuringCheck_IoError_NoWrite()
        {
            string p = CreateNotebook("a.kdbx");
            byte[] before = File.ReadAllBytes(p);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                using (new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    SaveResult r = store.Save(nb, false);
                    Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                    Assert.That(r.Message, Is.Not.Empty);
                }
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
            Assert.That(File.Exists(NotebookStore.BackupPath(p)), Is.False);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_NoLoadedFingerprint_ExternalChangeDetected()
        {
            string p = CreateNotebook("a.kdbx");
            byte[] before = File.ReadAllBytes(p);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                nb.LoadedFingerprint = null;
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
        }

        [Test]
        public void Save_FailAfterTempWrite_OriginalIdentical()
        {
            string p = CreateNotebook("a.kdbx");
            byte[] before = File.ReadAllBytes(p);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "after-temp-write") throw new IOException("simulated disk failure");
                };
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("simulated disk failure"));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(nb.Notes.Single().Title, Is.EqualTo("ours"));
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_NonIoExceptionAfterTempWrite_IoError_OriginalIdentical()
        {
            string p = CreateNotebook("a.kdbx");
            byte[] before = File.ReadAllBytes(p);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "after-temp-write") throw new InvalidOperationException("boom");
                };
                SaveResult r = null;
                Assert.DoesNotThrow(() => r = store.Save(nb, false));
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("boom"), "the store never prefixes; the UI owns the wording");
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(nb.Notes.Single().Title, Is.EqualTo("ours"));
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_NonIoExceptionWithEmptyMessage_IoErrorWithTypeName()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "after-temp-write") throw new EmptyMessageException();
                };
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("EmptyMessageException"));
            }
        }

        private sealed class EmptyMessageException : Exception
        {
            public override string Message { get { return ""; } }
        }

        [Test]
        public void Save_CleanNotebook_NoWrite_BackupsAndTargetUntouched()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                string id = nb.AddNote("t").Id;
                for (int i = 1; i <= 3; i++)
                {
                    nb.UpdateNote(id, "t", "v" + i);
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                }
                Assert.That(nb.IsDirty, Is.False);

                byte[] target = File.ReadAllBytes(p);
                DateTime targetTime = File.GetLastWriteTimeUtc(p);
                byte[] bak = File.ReadAllBytes(NotebookStore.BackupPath(p));
                DateTime bakTime = File.GetLastWriteTimeUtc(NotebookStore.BackupPath(p));

                bool hookCalled = false;
                store.TestHook = (stage, temp) => hookCalled = true;
                for (int i = 0; i < 3; i++)
                {
                    SaveResult r = store.Save(nb, false);
                    Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), "clean save " + (i + 1));
                }

                Assert.That(hookCalled, Is.False, "no temp file may be written for a clean save");
                Assert.That(File.ReadAllBytes(p), Is.EqualTo(target));
                Assert.That(File.GetLastWriteTimeUtc(p), Is.EqualTo(targetTime));
                Assert.That(File.ReadAllBytes(NotebookStore.BackupPath(p)), Is.EqualTo(bak), ".bak");
                Assert.That(File.GetLastWriteTimeUtc(NotebookStore.BackupPath(p)), Is.EqualTo(bakTime));
                Assert.That(Directory.GetFiles(_dir.Path).Select(Path.GetFileName), Is.EquivalentTo(new[] { "a.kdbx", "a.kdbx.bak" }));
                Assert.That(nb.IsDirty, Is.False);
            }
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_CleanNotebook_ExternallyReplaced_ExternalChangeDetected()
        {
            string p = CreateNotebook("a.kdbx");
            string other = CreateNotebook("other.kdbx");
            SeedNote(new NotebookStore(), other, "external", "theirs");
            byte[] external = File.ReadAllBytes(other);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                File.WriteAllBytes(p, external);
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
                Assert.That(r.Message, Is.EqualTo("The file was changed by another program since it was opened."));
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(external));
            Assert.That(File.Exists(NotebookStore.BackupPath(p)), Is.False);
        }

        [Test]
        public void Save_CleanNotebook_TargetDeleted_FileNotFound()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                File.Delete(p);
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("File not found."));
            }
            Assert.That(File.Exists(p), Is.False);
        }

        [Test]
        public void Save_CleanNotebook_Overwrite_Writes()
        {
            string p = CreateNotebook("a.kdbx");
            byte[] before = File.ReadAllBytes(p);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                Assert.That(nb.IsDirty, Is.False);
                Assert.That(store.Save(nb, true).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(File.ReadAllBytes(NotebookStore.BackupPath(p)), Is.EqualTo(before), "an explicit Overwrite writes");
        }

        [Test]
        public void SaveAs_CleanNotebook_WritesNewFile()
        {
            string p = CreateNotebook("a.kdbx");
            SeedNote(new NotebookStore(), p, "t", "body");
            string q = _dir.File("b.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                Assert.That(nb.IsDirty, Is.False);
                SaveResult r = store.SaveAs(nb, q);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(q)));
            }
            Assert.That(File.Exists(q), Is.True);
            using (Notebook nb = store.Open(q, Pw()))
                Assert.That(nb.Notes.Single().Text, Is.EqualTo("body"));
        }

        /// <summary>Opens <paramref name="p"/>, performs 3 real saves so the .bak exists, and returns the open notebook.</summary>
        private static Notebook OpenWithBackup(NotebookStore store, string p, out string id)
        {
            Notebook nb = store.Open(p, Pw());
            id = nb.AddNote("t").Id;
            for (int i = 1; i <= 3; i++)
            {
                nb.UpdateNote(id, "t", "v" + i);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            return nb;
        }

        private static byte[] ReadBackup(string p)
        {
            return File.ReadAllBytes(NotebookStore.BackupPath(p));
        }

        private static void AssertBackupUnchanged(string p, byte[] bak)
        {
            Assert.That(File.ReadAllBytes(NotebookStore.BackupPath(p)), Is.EqualTo(bak), ".bak");
            Assert.That(File.Exists(p + ".bak1"), Is.False, "no numbered backup");
        }

        [Test]
        public void Save_TargetChangedDuringSave_ExternalChangeDetected_NothingReplaced()
        {
            string p = CreateNotebook("a.kdbx");
            string other = CreateNotebook("other.kdbx");
            SeedNote(new NotebookStore(), other, "external", "theirs");
            byte[] external = File.ReadAllBytes(other);
            var store = new NotebookStore();
            string id;
            using (Notebook nb = OpenWithBackup(store, p, out id))
            {
                byte[] bak = ReadBackup(p);
                nb.UpdateNote(id, "t", "ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "before-replace") File.WriteAllBytes(p, external);
                };
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
                Assert.That(r.Message, Is.EqualTo("The file was changed by another program since it was opened."));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(File.ReadAllBytes(p), Is.EqualTo(external));
                AssertBackupUnchanged(p, bak);
            }
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_TargetDeletedDuringSave_FileNotFound_NotRecreated([Values(false, true)] bool overwrite)
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            string id;
            using (Notebook nb = OpenWithBackup(store, p, out id))
            {
                byte[] bak = ReadBackup(p);
                nb.UpdateNote(id, "t", "ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "before-replace") File.Delete(p);
                };
                SaveResult r = store.Save(nb, overwrite);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("File not found."));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(File.Exists(p), Is.False);
                AssertBackupUnchanged(p, bak);
            }
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_Overwrite_UnmarkedFileSwappedInDuringSave_NotReplaced()
        {
            string p = CreateNotebook("a.kdbx");
            string foreign = _dir.File("keepass.kdbx");
            BuildUnmarkedKdbx(foreign);
            byte[] planted = File.ReadAllBytes(foreign);
            var store = new NotebookStore();
            string id;
            using (Notebook nb = OpenWithBackup(store, p, out id))
            {
                byte[] bak = ReadBackup(p);
                nb.UpdateNote(id, "t", "ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "before-replace") File.WriteAllBytes(p, planted);
                };
                SaveResult r = store.Save(nb, true);
                Assert.That(r.Status, Is.Not.EqualTo(SaveStatus.Saved));
                Assert.That(r.Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(File.ReadAllBytes(p), Is.EqualTo(planted));
                AssertBackupUnchanged(p, bak);
            }
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void Save_TargetLockedAtFinalCheck_IoError_NothingReplaced()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            string id;
            FileStream held = null;
            try
            {
                using (Notebook nb = OpenWithBackup(store, p, out id))
                {
                    byte[] before = File.ReadAllBytes(p);
                    byte[] bak = ReadBackup(p);
                    nb.UpdateNote(id, "t", "ours");
                    store.TestHook = (stage, temp) =>
                    {
                        if (stage == "before-replace") held = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    };
                    SaveResult r = store.Save(nb, false);
                    Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                    Assert.That(nb.IsDirty, Is.True);
                    held.Dispose();
                    held = null;
                    Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
                    AssertBackupUnchanged(p, bak);
                }
                Assert.That(TempFiles(), Is.Empty);
            }
            finally
            {
                if (held != null) held.Dispose();
            }
        }

        [Test]
        public void Save_VerifyMismatch_OriginalIdentical()
        {
            string p = CreateNotebook("a.kdbx");
            byte[] before = File.ReadAllBytes(p);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "after-temp-write") FlipByte(temp, 200);
                };
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.VerifyFailed));
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
            Assert.That(TempFiles(), Is.Empty);
        }

        private static void FlipByte(string path, int offset)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                fs.Position = offset;
                int b = fs.ReadByte();
                fs.Position = offset;
                fs.WriteByte((byte)(b ^ 0xFF));
            }
        }

        [Test]
        public void Save_RetryAfterVerifyFailed_HistoryAndDeletedObjectAddedOnce()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            string keep, gone;
            using (Notebook nb = store.Open(p, Pw()))
            {
                keep = nb.AddNote("keep").Id;
                gone = nb.AddNote("gone").Id;
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            uint historyBefore;
            PwDatabase db0 = RawOpen(p);
            try { historyBefore = NotesGroup(db0).Entries.First(e => e.Uuid.ToHexString() == keep).History.UCount; }
            finally { db0.Close(); }

            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.UpdateNote(keep, "keep", "edited");
                nb.RemoveNote(gone);
                bool first = true;
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "after-temp-write" && first) { first = false; FlipByte(temp, 200); }
                };
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.VerifyFailed));
                Assert.That(nb.IsDirty, Is.True);
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
            }

            PwDatabase db = RawOpen(p);
            try
            {
                PwEntry e = NotesGroup(db).Entries.Single();
                Assert.That(e.Uuid.ToHexString(), Is.EqualTo(keep));
                Assert.That(e.History.UCount, Is.EqualTo(historyBefore + 1));
                Assert.That(e.Strings.ReadSafe(PwDefs.NotesField), Is.EqualTo("edited"));
                Assert.That(db.DeletedObjects.Count(d => d.Uuid.ToHexString() == gone), Is.EqualTo(1));
            }
            finally { db.Close(); }
        }

        [Test]
        public void Save_PreservesOtherEntryFields()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            string id = SeedNote(store, p, "t", "v0");
            PwDatabase raw = RawOpen(p);
            try
            {
                PwEntry e = NotesGroup(raw).Entries.Single();
                e.Strings.Set("Custom", new ProtectedString(false, "custom value"));
                e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "user"));
                e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.invalid/"));
                raw.SaveAs(IOConnectionInfo.FromPath(p), false, null);
            }
            finally { raw.Close(); }

            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.UpdateNote(id, "t2", "v1");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            PwDatabase db = RawOpen(p);
            try
            {
                PwEntry e = NotesGroup(db).Entries.Single();
                Assert.That(e.Strings.ReadSafe("Custom"), Is.EqualTo("custom value"));
                Assert.That(e.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("user"));
                Assert.That(e.Strings.ReadSafe(PwDefs.UrlField), Is.EqualTo("https://example.invalid/"));
                Assert.That(e.Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo("t2"));
                Assert.That(e.Strings.ReadSafe(PwDefs.NotesField), Is.EqualTo("v1"));
                Assert.That(e.Strings.Get(PwDefs.NotesField).IsProtected, Is.True);
            }
            finally { db.Close(); }
        }

        [Test]
        public void Save_ReadOnlyFileAttribute_ReturnsIoError_OriginalUntouched()
        {
            string p = CreateNotebook("a.kdbx");
            byte[] before = File.ReadAllBytes(p);
            var store = new NotebookStore();
            try
            {
                using (Notebook nb = store.Open(p, Pw()))
                {
                    nb.AddNote("ours");
                    File.SetAttributes(p, FileAttributes.ReadOnly);
                    SaveResult r = store.Save(nb, false);
                    Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                    Assert.That(nb.IsDirty, Is.True);
                }
                Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
                Assert.That(TempFiles(), Is.Empty);
            }
            finally
            {
                if (File.Exists(p)) File.SetAttributes(p, FileAttributes.Normal);
            }
        }

        [Test]
        public void Save_LargeNote5MB_RoundTrips()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            var sb = new StringBuilder(5000000);
            for (int i = 0; i < 5000000; i++) sb.Append((char)('a' + (i % 26)));
            string big = sb.ToString();
            string edited = big.Substring(0, 2500000) + "EDIT" + big.Substring(2500004);
            string id;
            using (Notebook nb = store.Open(p, Pw()))
            {
                id = nb.AddNote("big").Id;
                nb.UpdateNote(id, "big", big);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                nb.UpdateNote(id, "big", edited);
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            using (Notebook nb = store.Open(p, Pw()))
            {
                string read = nb.FindNote(id).Text;
                Assert.That(read.Length, Is.EqualTo(5000000));
                Assert.That(string.Equals(read, edited, StringComparison.Ordinal), Is.True);
            }
        }

        [Test]
        public void Save_StaleTempFilePresent_StillSaves()
        {
            string p = CreateNotebook("a.kdbx");
            string stale = p + ".DEADBEEF.tmp";
            File.WriteAllText(stale, "junk");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(File.ReadAllText(stale), Is.EqualTo("junk"));
            Assert.That(TempFiles(), Is.EqualTo(new[] { stale }));
            using (Notebook nb = store.Open(p, Pw()))
                Assert.That(nb.Notes.Single().Title, Is.EqualTo("ours"));
        }

        [Test]
        public void Save_KeyFileDeletedAfterOpen_StillSaves()
        {
            string key = _dir.File("k.keyx");
            Credentials.CreateKeyFile(key);
            string p = CreateNotebook("a.kdbx", "pw", key);
            byte[] keyBytes = File.ReadAllBytes(key);
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw("pw", key)))
            {
                File.Delete(key);
                nb.AddNote("ours");
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
            }
            File.WriteAllBytes(key, keyBytes);
            using (Notebook nb = store.Open(p, Pw("pw", key)))
                Assert.That(nb.Notes.Single().Title, Is.EqualTo("ours"));
        }

        [Test]
        public void Save_DisposedNotebook_ThrowsObjectDisposed()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            Notebook nb = store.Open(p, Pw());
            nb.Dispose();
            Assert.Throws<ObjectDisposedException>(() => store.Save(nb, false));
            Assert.Throws<ObjectDisposedException>(() => store.SaveAs(nb, _dir.File("b.kdbx")));
        }

        [Test]
        public void Save_NullNotebook_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new NotebookStore().Save(null, false));
            Assert.Throws<ArgumentNullException>(() => new NotebookStore().SaveAs(null, _dir.File("b.kdbx")));
        }

        [Test]
        public void SaveAs_NewPath_MovesNotebook_OriginalUnchanged()
        {
            string p = CreateNotebook("a.kdbx");
            byte[] before = File.ReadAllBytes(p);
            string q = _dir.File("b.kdbx");
            var store = new NotebookStore();
            string id;
            using (Notebook nb = store.Open(p, Pw()))
            {
                id = nb.AddNote("moved").Id;
                nb.UpdateNote(id, "moved", "body");
                SaveResult r = store.SaveAs(nb, q);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(q)));
                Assert.That(nb.IsDirty, Is.False);
                Assert.That(nb.LoadedFingerprint.SameContentAs(FileFingerprint.Compute(q)), Is.True);

                // Subsequent saves go to the new path.
                nb.UpdateNote(id, "moved", "body 2");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(File.ReadAllBytes(p), Is.EqualTo(before));
            Assert.That(File.Exists(NotebookStore.BackupPath(p)), Is.False);
            using (Notebook nb = store.Open(q, Pw()))
                Assert.That(nb.FindNote(id).Text, Is.EqualTo("body 2"));
            using (Notebook nb = store.Open(p, Pw()))
                Assert.That(nb.Notes, Is.Empty);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void SaveAs_ExistingTarget_ReturnsIoError()
        {
            string p = CreateNotebook("a.kdbx");
            string q = _dir.File("b.kdbx");
            File.WriteAllText(q, "existing");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                SaveResult r = store.SaveAs(nb, q);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("File already exists."));
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(p)));
                Assert.That(nb.IsDirty, Is.True);

                // Its own current path also exists: never replaced through Save As.
                Assert.That(store.SaveAs(nb, p).Message, Is.EqualTo("File already exists."));
            }
            Assert.That(File.ReadAllText(q), Is.EqualTo("existing"));
            Assert.That(File.Exists(NotebookStore.BackupPath(q)), Is.False);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void SaveAs_TargetAppearsBeforeCommit_IoError_PlantedFileIntact()
        {
            string p = CreateNotebook("a.kdbx");
            string q = _dir.File("b.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "before-replace") File.WriteAllText(q, "planted");
                };
                SaveResult r = store.SaveAs(nb, q);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("File already exists."));
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(p)));
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(File.ReadAllText(q), Is.EqualTo("planted"));
            Assert.That(File.Exists(NotebookStore.BackupPath(q)), Is.False);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void SaveAs_ReadOnlyNotebook_ReturnsReadOnly()
        {
            string p = _dir.File("keepass.kdbx");
            BuildUnmarkedKdbx(p);
            string q = _dir.File("copy.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                SaveResult r = store.SaveAs(nb, q);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.ReadOnly));
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(p)));
            }
            Assert.That(File.Exists(q), Is.False);
        }

        [Test]
        public void SaveAs_FailureAfterTempWrite_FilePathUnchanged_NoFile()
        {
            string p = CreateNotebook("a.kdbx");
            string q = _dir.File("b.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                store.TestHook = (stage, temp) =>
                {
                    if (stage == "after-temp-write") throw new InvalidOperationException("boom");
                };
                SaveResult r = store.SaveAs(nb, q);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(p)));
                Assert.That(nb.IsDirty, Is.True);
            }
            Assert.That(File.Exists(q), Is.False);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void SaveAs_UrlPath_IoError()
        {
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("ours");
                SaveResult r = store.SaveAs(nb, "https://example.invalid/a.kdbx");
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo("Only local file paths are allowed."));
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(p)));
            }
        }

        // ---- Full check 2026-10-22, L6: a path too long for the .tmp and .bak.kept names is refused ----

        private const string PathTooLongMessage =
            "This path is too long for LockPad's backup and temporary files. Choose a shorter folder or file name.";

        /// <summary>A full path (as LocalPath expands it) of exactly <paramref name="length"/> characters in the test
        /// folder.</summary>
        private string PathOfLength(int length)
        {
            string prefix = Path.GetFullPath(Path.Combine(_dir.Path, "n"));
            int pad = length - prefix.Length - ".kdbx".Length;
            Assert.That(pad, Is.GreaterThan(0));
            return prefix + new string('x', pad) + ".kdbx";
        }

        [Test]
        public void Create_PathLongerThanTheLimit_Refused_NothingWritten()
        {
            string p = PathOfLength(NotebookStore.MaxNotebookPathLength + 1);
            var ex = Assert.Throws<IOException>(() => new NotebookStore().Create(p, Pw(), TestKdf.Fast()));
            Assert.That(ex.Message, Is.EqualTo(PathTooLongMessage));
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty);
        }

        [Test]
        public void Create_PathAtTheLimit_SavesTwice()
        {
            string p = PathOfLength(NotebookStore.MaxNotebookPathLength);
            var store = new NotebookStore();
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("a");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                nb.AddNote("b");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Assert.That(File.Exists(p + ".bak"), Is.True);
        }

        [Test]
        public void SaveAs_PathLongerThanTheLimit_Refused_StaysDirty()
        {
            string p = CreateNotebook("nb.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("work");
                SaveResult r = store.SaveAs(nb, PathOfLength(NotebookStore.MaxNotebookPathLength + 1));
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo(PathTooLongMessage));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(p)));
            }
            Assert.That(Directory.GetFiles(_dir.Path).Select(Path.GetFileName), Is.EqualTo(new[] { "nb.kdbx" }));
        }

        // ---- Fix round 2, B (L3): backup-copy names are reserved ----

        private const string BackupNameMessage = "This name is reserved for backup copies. Choose another name.";

        [TestCase("nb.kdbx.bak")]
        [TestCase("nb.kdbx.bak3")]
        [TestCase("X.BAK12")]
        [TestCase("nb.kdbx.bak3.")] // GetFullPath trims the trailing dot: the file would be nb.kdbx.bak3
        [TestCase("nb.kdbx.bak ")] // GetFullPath trims the trailing space: the file would be nb.kdbx.bak
        public void SaveAs_BackupCopyName_Refused_NothingWritten_StaysDirty(string name)
        {
            string p = CreateNotebook("nb.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Open(p, Pw()))
            {
                nb.AddNote("work");
                SaveResult r = store.SaveAs(nb, _dir.File(name));
                Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
                Assert.That(r.Message, Is.EqualTo(BackupNameMessage));
                Assert.That(nb.IsDirty, Is.True);
                Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(p)));
            }
            Assert.That(Directory.GetFiles(_dir.Path).Select(Path.GetFileName), Is.EqualTo(new[] { "nb.kdbx" }));
        }

        [Test]
        public void SaveAs_FreeBackupNameOfMain_Refused_SoTheNextSaveCannotReplaceTheCopy()
        {
            // Re-attack PoC L3 (R3), for the single backup (V7): nb.kdbx has never been saved over, so nb.kdbx.bak is
            // free; a copy saved as nb.kdbx.bak would be replaced by the next save of nb.kdbx. The Save As is refused
            // and the work stays in the notebook to be saved elsewhere.
            string main = CreateNotebook("nb.kdbx");
            var store = new NotebookStore();
            Assert.That(File.Exists(main + ".bak"), Is.False);

            using (Notebook nb = store.Open(main, Pw()))
            {
                string id = nb.AddNote("saved-as-copy").Id;
                nb.UpdateNote(id, "saved-as-copy", "latest work");
                Assert.That(store.SaveAs(nb, main + ".bak").Message, Is.EqualTo(BackupNameMessage));
                Assert.That(File.Exists(main + ".bak"), Is.False);

                string elsewhere = _dir.File("copy.kdbx");
                Assert.That(store.SaveAs(nb, elsewhere).Status, Is.EqualTo(SaveStatus.Saved), "another name works");
            }
            using (Notebook copy = store.Open(_dir.File("copy.kdbx"), Pw()))
                Assert.That(copy.Notes.Any(n => n.Title == "saved-as-copy"), Is.True);
        }

        [TestCase("nb.kdbx.bak1")]
        [TestCase("nb.kdbx.bak")]
        public void Create_BackupCopyName_Refused_NothingWritten(string name)
        {
            string p = _dir.File(name);
            Credentials c = Pw();
            var ex = Assert.Throws<IOException>(() => new NotebookStore().Create(p, c, TestKdf.Fast()));
            c.Dispose();
            Assert.That(ex.Message, Is.EqualTo(BackupNameMessage));
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty);
        }

        // ---- SEC-M1: the crypto and format settings are enforced on every save ----

        private const ulong MiB = 1024UL * 1024;

        public enum Weakening
        {
            ChaCha20Cipher,
            Argon2dKdf,
            AesKdf,
            LowMemory,
            OneIteration,
            OneLane,
            UnlimitedHistory,
            RecycleBinOn,
        }

        /// <summary>
        /// Runs with the production KDF floor (the suite otherwise accepts TestKdf.Fast notebooks, see AssemblySetup).
        /// Fresh parameters come from KdfTuner with a 1 ms target: 64 MiB, parallelism 2, 2 iterations.
        /// </summary>
        private sealed class ProductionFormatPolicy : IDisposable
        {
            private readonly bool _accept;
            private readonly Func<KeePassLib.Cryptography.KeyDerivation.KdfParameters> _fresh;
            public int FreshCalls;

            public ProductionFormatPolicy()
            {
                _accept = SaveFormatPolicy.AcceptWeakKdfForTests;
                _fresh = SaveFormatPolicy.FreshKdf;
                SaveFormatPolicy.AcceptWeakKdfForTests = false;
                SaveFormatPolicy.FreshKdf = () => { FreshCalls++; return KdfTuner.Argon2id(1); };
            }

            public void Dispose()
            {
                SaveFormatPolicy.AcceptWeakKdfForTests = _accept;
                SaveFormatPolicy.FreshKdf = _fresh;
            }
        }

        private static KeePassLib.Cryptography.KeyDerivation.KdfParameters Argon2(
            KeePassLib.Cryptography.KeyDerivation.Argon2Type type, ulong memory, ulong iterations, uint lanes)
        {
            var p = new KeePassLib.Cryptography.KeyDerivation.Argon2Kdf(type).GetDefaultParameters();
            p.SetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamMemory, memory);
            p.SetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamIterations, iterations);
            p.SetUInt32(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamParallelism, lanes);
            return p;
        }

        /// <summary>
        /// Changes the database settings of a LockPad notebook through KeePassLib, as another client could. Every
        /// setting other than the weakened one is compliant (a compliant 64 MiB / 2 / 2 Argon2id KDF unless the KDF is
        /// what is weakened).
        /// </summary>
        private static void Weaken(string path, Weakening w)
        {
            PwDatabase db = RawOpen(path);
            try
            {
                var id = KeePassLib.Cryptography.KeyDerivation.Argon2Type.ID;
                db.KdfParameters = Argon2(id, 64 * MiB, 2, 2);
                switch (w)
                {
                    case Weakening.ChaCha20Cipher:
                        db.DataCipherUuid = new KeePassLib.Cryptography.Cipher.ChaCha20Engine().CipherUuid;
                        break;
                    case Weakening.Argon2dKdf:
                        db.KdfParameters = Argon2(KeePassLib.Cryptography.KeyDerivation.Argon2Type.D, 64 * MiB, 2, 2);
                        break;
                    case Weakening.AesKdf:
                        var aes = new KeePassLib.Cryptography.KeyDerivation.AesKdf().GetDefaultParameters();
                        aes.SetUInt64(KeePassLib.Cryptography.KeyDerivation.AesKdf.ParamRounds, 1000);
                        db.KdfParameters = aes;
                        break;
                    case Weakening.LowMemory:
                        db.KdfParameters = Argon2(id, 16 * MiB, 2, 2);
                        break;
                    case Weakening.OneIteration:
                        db.KdfParameters = Argon2(id, 64 * MiB, 1, 2);
                        break;
                    case Weakening.OneLane:
                        db.KdfParameters = Argon2(id, 64 * MiB, 2, 1);
                        break;
                    case Weakening.UnlimitedHistory:
                        db.HistoryMaxItems = -1;
                        db.HistoryMaxSize = -1;
                        break;
                    case Weakening.RecycleBinOn:
                        db.RecycleBinEnabled = true;
                        break;
                }
                db.Save(null);
            }
            finally
            {
                db.Close();
            }
        }

        private static void AssertCompliant(PwDatabase db)
        {
            Assert.That(db.DataCipherUuid, Is.EqualTo(KeePassLib.Cryptography.Cipher.StandardAesEngine.AesUuid), "AES-256");
            var p = db.KdfParameters;
            Assert.That(p.KdfUuid, Is.EqualTo(new KeePassLib.Cryptography.KeyDerivation.Argon2Kdf(
                KeePassLib.Cryptography.KeyDerivation.Argon2Type.ID).Uuid), "Argon2id");
            Assert.That(p.GetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamMemory, 0), Is.GreaterThanOrEqualTo(64 * MiB));
            Assert.That(p.GetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamIterations, 0), Is.GreaterThanOrEqualTo(2UL));
            Assert.That(p.GetUInt32(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamParallelism, 0), Is.EqualTo(2u));
            Assert.That(db.HistoryMaxItems, Is.EqualTo(10));
            Assert.That(db.HistoryMaxSize, Is.EqualTo(6L * 1024 * 1024));
            Assert.That(db.RecycleBinEnabled, Is.False);
        }

        [TestCase(Weakening.ChaCha20Cipher)]
        [TestCase(Weakening.Argon2dKdf)]
        [TestCase(Weakening.AesKdf)]
        [TestCase(Weakening.LowMemory)]
        [TestCase(Weakening.OneIteration)]
        [TestCase(Weakening.OneLane)]
        [TestCase(Weakening.UnlimitedHistory)]
        [TestCase(Weakening.RecycleBinOn)]
        public void Save_WeakenedByAnotherClient_RestoresAesArgon2idAndHistoryLimit(Weakening w)
        {
            // Created and weakened under the suite's relaxed policy (fast); opened and saved by LockPad under the
            // production floor.
            string p = CreateNotebook("weak.kdbx");
            Weaken(p, w);
            using (new ProductionFormatPolicy())
            {
                var store = new NotebookStore();
                using (Notebook nb = store.Open(p, Pw()))
                {
                    Assert.That(nb.IsReadOnly, Is.False, "still a marked LockPad notebook");
                    nb.AddNote("edit");
                    SaveResult r = store.Save(nb, false);
                    Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
                }

                PwDatabase db = RawOpen(p);
                try { AssertCompliant(db); }
                finally { db.Close(); }
            }
        }

        [Test]
        public void Save_CompliantFile_KeepsExactKdfParameters_ExceptTheSaltKeePassLibRandomizes()
        {
            string p = CreateNotebook("ok.kdbx");
            // A compliant but different choice: 3 iterations, 64 MiB, 2 lanes.
            byte[] saltBefore;
            PwDatabase raw = RawOpen(p);
            try
            {
                raw.KdfParameters = Argon2(KeePassLib.Cryptography.KeyDerivation.Argon2Type.ID, 64 * MiB, 3, 2);
                raw.Save(null); // randomizes the salt in place: this is the salt now in the file
                saltBefore = raw.KdfParameters.GetByteArray(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamSalt);
            }
            finally { raw.Close(); }

            using (var policy = new ProductionFormatPolicy())
            {
                int freshBefore = policy.FreshCalls;
                var store = new NotebookStore();
                using (Notebook nb = store.Open(p, Pw()))
                {
                    nb.AddNote("edit");
                    Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
                }

                Assert.That(policy.FreshCalls, Is.EqualTo(freshBefore), "no re-tuning for a compliant file");
                var after = ReadKdf(p);
                Assert.That(after.GetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamIterations, 0), Is.EqualTo(3UL));
                Assert.That(after.GetUInt64(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamMemory, 0), Is.EqualTo(64 * MiB));
                Assert.That(after.GetUInt32(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamParallelism, 0), Is.EqualTo(2u));
                Assert.That(after.GetByteArray(KeePassLib.Cryptography.KeyDerivation.Argon2Kdf.ParamSalt), Is.Not.EqualTo(saltBefore),
                    "KeePassLib re-randomizes the salt on every write (KdbxFile.Write: kdf.Randomize)");
            }
        }

        private static KeePassLib.Cryptography.KeyDerivation.KdfParameters ReadKdf(string path)
        {
            PwDatabase db = RawOpen(path);
            try { return db.KdfParameters; }
            finally { db.Close(); }
        }

        [Test]
        public void Create_WithWeakKdf_WritesFreshCompliantParameters()
        {
            using (new ProductionFormatPolicy())
            {
                string p = CreateNotebook("new.kdbx"); // CreateNotebook passes TestKdf.Fast (1 MiB, 1 iteration)
                PwDatabase db = RawOpen(p);
                try { AssertCompliant(db); }
                finally { db.Close(); }
            }
        }
    }
}

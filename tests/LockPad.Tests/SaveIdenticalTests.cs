using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// UX round 3, V5: a save whose notes (ids, titles, texts, the set of notes) equal the last loaded or saved file
    /// writes nothing and leaves the .bak alone, so typing then undoing never replaces the real older version in the
    /// backup (V7: one .bak). The comparison is against the PwDatabase already in memory, and only while that database still
    /// equals the file (a failed save has already applied the changes to it).
    /// </summary>
    [TestFixture]
    public class SaveIdenticalTests
    {
        private TempDir _dir;
        private NotebookStore _store;
        private readonly List<Notebook> _notebooks = new List<Notebook>();

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
            _store = new NotebookStore();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _notebooks) nb.Dispose();
            _notebooks.Clear();
            if (File.Exists(Path)) File.SetAttributes(Path, FileAttributes.Normal);
            _dir.Dispose();
        }

        private string Path { get { return _dir.File("nb.kdbx"); } }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        /// <summary>Creates nb.kdbx with notes A ("a") and B ("b"), saved, then opens it fresh.</summary>
        private Notebook Opened()
        {
            using (Notebook nb = _store.Create(Path, Pw(), TestKdf.Fast()))
            {
                Note a = nb.AddNote("A");
                nb.UpdateNote(a.Id, "A", "a");
                Note b = nb.AddNote("B");
                nb.UpdateNote(b.Id, "B", "b");
                Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Notebook opened = _store.Open(Path, Pw());
            _notebooks.Add(opened);
            return opened;
        }

        /// <summary>Names, bytes and write times of the notebook and every file next to it.</summary>
        private Dictionary<string, string> Snapshot()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(System.IO.Path.GetDirectoryName(Path)))
                d[System.IO.Path.GetFileName(f)] = Convert.ToBase64String(File.ReadAllBytes(f)) + "|"
                    + File.GetLastWriteTimeUtc(f).Ticks;
            return d;
        }

        private Notebook Reopen()
        {
            Notebook nb = new NotebookStore().Open(Path, Pw());
            _notebooks.Add(nb);
            return nb;
        }

        private void AssertSavesNothing(Notebook nb)
        {
            Assert.That(nb.IsDirty, Is.True, "the edit marked it dirty");
            Dictionary<string, string> before = Snapshot();

            SaveResult r = _store.Save(nb, false);

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(nb.IsDirty, Is.False, "marked clean");
            Assert.That(nb.RemovedIds, Is.Empty);
            Assert.That(Snapshot(), Is.EqualTo(before), "no file written, the .bak untouched");
        }

        [Test]
        public void TypeThenRevert_WritesNothing()
        {
            Notebook nb = Opened();
            string id = nb.Notes[0].Id;
            nb.UpdateNote(id, "A", "ax");
            nb.UpdateNote(id, "A", "a");

            AssertSavesNothing(nb);
        }

        [Test]
        public void NoteAddedThenDeleted_WritesNothing()
        {
            Notebook nb = Opened();
            Note added = nb.AddNote("temp");
            nb.RemoveNote(added.Id);

            AssertSavesNothing(nb);
        }

        [Test]
        public void RenamedBackToTheOldTitle_WritesNothing()
        {
            Notebook nb = Opened();
            string id = nb.Notes[1].Id;
            nb.UpdateNote(id, "Renamed", "b");
            nb.UpdateNote(id, "B", "b");

            AssertSavesNothing(nb);
        }

        [Test]
        public void DeletedThenRestored_WritesNothing()
        {
            Notebook nb = Opened();
            Note a = nb.Notes[0];
            nb.RemoveNote(a.Id);
            nb.RestoreNote(a, 0);

            AssertSavesNothing(nb);
        }

        [Test]
        public void AfterASkippedSave_ARealChangeStillSaves()
        {
            Notebook nb = Opened();
            string id = nb.Notes[0].Id;
            nb.UpdateNote(id, "A", "ax");
            nb.UpdateNote(id, "A", "a");
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

            nb.UpdateNote(id, "A", "changed");
            Dictionary<string, string> before = Snapshot();
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

            Assert.That(Snapshot(), Is.Not.EqualTo(before));
            Assert.That(Reopen().FindNote(id).Text, Is.EqualTo("changed"));
        }

        [TestCase("text")]
        [TestCase("title")]
        [TestCase("added")]
        [TestCase("removed")]
        public void ARealChange_Saves(string change)
        {
            Notebook nb = Opened();
            string id = nb.Notes[0].Id;
            if (change == "text") nb.UpdateNote(id, "A", "a2");
            if (change == "title") nb.UpdateNote(id, "A2", "a");
            if (change == "added") nb.AddNote("C");
            if (change == "removed") nb.RemoveNote(id);
            Dictionary<string, string> before = Snapshot();

            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

            Assert.That(Snapshot(), Is.Not.EqualTo(before), "written");
            Notebook reopened = Reopen();
            Assert.That(reopened.Notes.Select(n => n.Title + "=" + n.Text).OrderBy(s => s),
                Is.EqualTo(nb.Notes.Select(n => n.Title + "=" + n.Text).OrderBy(s => s)));
        }

        [Test]
        public void AfterAFailedSave_TheSameChangeIsStillWritten()
        {
            // The failed attempt applied the edit to the in-memory database: comparing against it would wrongly
            // report "identical" and lose the edit.
            Notebook nb = Opened();
            string id = nb.Notes[0].Id;
            nb.UpdateNote(id, "A", "edited");
            File.SetAttributes(Path, FileAttributes.ReadOnly);
            Assert.That(_store.Save(nb, false).Status, Is.Not.EqualTo(SaveStatus.Saved));
            File.SetAttributes(Path, FileAttributes.Normal);

            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

            Assert.That(Reopen().FindNote(id).Text, Is.EqualTo("edited"));
        }

        [Test]
        public void AfterAFailedSave_RevertingStillWritesAndEndsClean()
        {
            Notebook nb = Opened();
            string id = nb.Notes[0].Id;
            nb.UpdateNote(id, "A", "edited");
            File.SetAttributes(Path, FileAttributes.ReadOnly);
            Assert.That(_store.Save(nb, false).Status, Is.Not.EqualTo(SaveStatus.Saved));
            File.SetAttributes(Path, FileAttributes.Normal);
            nb.UpdateNote(id, "A", "a");

            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

            Assert.That(nb.IsDirty, Is.False);
            Assert.That(Reopen().FindNote(id).Text, Is.EqualTo("a"));
        }

        [Test]
        public void ExternalChange_IsStillDetectedFirst()
        {
            Notebook nb = Opened();
            string id = nb.Notes[0].Id;
            nb.UpdateNote(id, "A", "ax");
            nb.UpdateNote(id, "A", "a");
            var other = new NotebookStore();
            using (Notebook second = other.Open(Path, Pw()))
            {
                second.AddNote("from elsewhere");
                Assert.That(other.Save(second, false).Status, Is.EqualTo(SaveStatus.Saved));
            }

            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
            Assert.That(nb.IsDirty, Is.True);
        }

        [Test]
        public void ExplicitOverwrite_AlwaysWrites()
        {
            Notebook nb = Opened();
            string id = nb.Notes[0].Id;
            nb.UpdateNote(id, "A", "ax");
            nb.UpdateNote(id, "A", "a");
            Dictionary<string, string> before = Snapshot();

            Assert.That(_store.Save(nb, true).Status, Is.EqualTo(SaveStatus.Saved));

            Assert.That(Snapshot(), Is.Not.EqualTo(before));
        }
    }
}

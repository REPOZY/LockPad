using System.Linq;
using System.Text;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>C11 (review M11): round trips the review found untested.</summary>
    [TestFixture]
    public class NotebookRoundTripTests
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

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        [Test]
        public void DuplicateTitles_SurviveSaveAndReopen_AsDistinctNotes()
        {
            string p = _dir.File("nb.kdbx");
            var store = new NotebookStore();
            string idA, idB;
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                Note a = nb.AddNote("Wallet");
                Note b = nb.AddNote("Wallet");
                idA = a.Id;
                idB = b.Id;
                nb.UpdateNote(idA, "Wallet", "seed one");
                nb.UpdateNote(idB, "Wallet", "seed two");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }

            using (Notebook nb = store.Open(p, Pw()))
            {
                Assert.That(nb.Notes.Count, Is.EqualTo(2));
                Assert.That(nb.Notes.Select(n => n.Title), Is.EqualTo(new[] { "Wallet", "Wallet" }));
                Assert.That(nb.FindNote(idA).Text, Is.EqualTo("seed one"));
                Assert.That(nb.FindNote(idB).Text, Is.EqualTo("seed two"));

                // Editing one of them changes only that one.
                nb.UpdateNote(idB, "Wallet", "seed two, edited");
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            using (Notebook nb = store.Open(p, Pw()))
            {
                Assert.That(nb.FindNote(idA).Text, Is.EqualTo("seed one"));
                Assert.That(nb.FindNote(idB).Text, Is.EqualTo("seed two, edited"));
            }
        }

        /// <summary>Review Focus 2: about 5 MB of text, larger than KeePassLib's 6 MiB history limit allows to keep in
        /// history, saved four times with edits; every reopen is exact.</summary>
        [Test]
        public void FiveMegabyteNote_FourSavesWithEdits_EachReopenExact()
        {
            const int Length = 5 * 1024 * 1024;
            var sb = new StringBuilder(Length);
            string line = "abandon ability able about above absent absorb abstract absurd abuse access accident 0123456789";
            for (int i = 0; sb.Length < Length; i++) sb.Append(line).Append(' ').Append(i).Append('\n');
            sb.Length = Length;
            string text = sb.ToString();

            string p = _dir.File("big.kdbx");
            var store = new NotebookStore();
            string id;
            using (Notebook nb = store.Create(p, Pw(), TestKdf.Fast()))
            {
                id = nb.AddNote("big").Id;
                Assert.That(store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }

            for (int version = 1; version <= 4; version++)
            {
                // A different edit per version: start, middle, end, and a whole-line replacement.
                char[] chars = text.ToCharArray();
                int at = version == 1 ? 0 : version == 2 ? Length / 2 : version == 3 ? Length - 1 : Length / 3;
                chars[at] = (char)('A' + version);
                text = new string(chars);
                if (version == 4) text = text.Replace("absurd abuse", "ABSURD ABUSE");

                using (Notebook nb = store.Open(p, Pw()))
                {
                    nb.UpdateNote(id, "big", text);
                    SaveResult r = store.Save(nb, false);
                    Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), "version " + version + ": " + r.Message);
                }
                using (Notebook nb = store.Open(p, Pw()))
                {
                    Note n = nb.Notes.Single();
                    Assert.That(n.Text.Length, Is.EqualTo(text.Length), "version " + version);
                    Assert.That(string.Equals(n.Text, text, System.StringComparison.Ordinal), Is.True,
                        "version " + version + " reopened exactly");
                }
            }
        }

        [Test]
        public void Open_WithADifferentValidKeyFile_IsWrongCredentials()
        {
            string rightKey = _dir.File("right.keyx");
            string otherKey = _dir.File("other.keyx");
            Credentials.CreateKeyFile(rightKey);
            Credentials.CreateKeyFile(otherKey);
            string p = _dir.File("nb.kdbx");
            var store = new NotebookStore();
            using (store.Create(p, new Credentials(Encoding.UTF8.GetBytes("pw"), rightKey), TestKdf.Fast())) { }

            var wrong = new Credentials(Encoding.UTF8.GetBytes("pw"), otherKey);
            var ex = Assert.Throws<WrongCredentialsException>(() => store.Open(p, wrong));
            Assert.That(ex.Message, Is.EqualTo("Wrong password or key file."));
            wrong.Dispose();

            using (Notebook nb = store.Open(p, new Credentials(Encoding.UTF8.GetBytes("pw"), rightKey)))
                Assert.That(nb.IsReadOnly, Is.False, "the right key file still opens it");
        }
    }
}

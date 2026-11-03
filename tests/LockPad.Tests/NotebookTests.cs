using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class NotebookTests
    {
        private static Notebook Fresh(bool readOnly = false, params Note[] notes)
        {
            return new Notebook("x.kdbx", readOnly, notes);
        }

        private static Note N(string id, string title, string text)
        {
            return Note.Create(id, title, text, DateTime.UtcNow);
        }

        [Test]
        public void AddNote_MarksDirty_AssignsUniqueHexIds()
        {
            var nb = Fresh();
            var a = nb.AddNote("a");
            var b = nb.AddNote("b");
            Assert.That(a.Id, Is.Not.EqualTo(b.Id));
            Assert.That(Regex.IsMatch(a.Id, "^[0-9A-F]{32}$"), Is.True);
            Assert.That(Regex.IsMatch(b.Id, "^[0-9A-F]{32}$"), Is.True);
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(nb.Notes.Select(n => n.Id), Is.EqualTo(new[] { a.Id, b.Id }));
        }

        [Test]
        public void AddNote_EmptyTitle_Allowed()
        {
            Assert.That(Fresh().AddNote("").Title, Is.EqualTo(""));
        }

        [Test]
        public void Dispose_DropsTheNoteReferences_AndTheSavedIds()
        {
            // Fix round 1, X9 (defence in depth): a disposed notebook keeps no note text reachable.
            var nb = Fresh(false, N("A1", "a", "secret a"), N("B2", "b", "secret b"));
            nb.RemoveNote("B2");
            IReadOnlyList<Note> held = nb.Notes; // a view of the same list, as the editor holds it
            Assert.That(nb.IsInSavedFile("A1"), Is.True);

            nb.Dispose();

            Assert.That(nb.Notes, Is.Empty);
            Assert.That(held, Is.Empty, "views handed out earlier are emptied too");
            Assert.That(nb.FindNote("A1"), Is.Null);
            Assert.That(nb.IsInSavedFile("A1"), Is.False);
            Assert.DoesNotThrow(nb.Dispose, "twice is harmless");
        }

        [Test]
        public void UpdateNote_SameValues_NotDirty()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            nb.UpdateNote("A", "t", "x");
            Assert.That(nb.IsDirty, Is.False);
        }

        [Test]
        public void UpdateNote_Changed_DirtyAndEventOnce()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            int count = 0;
            nb.DirtyChanged += (s, e) => count++;
            nb.UpdateNote("A", "t", "y");
            nb.UpdateNote("A", "t", "z");
            Assert.That(count, Is.EqualTo(1));
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(nb.FindNote("A").Text, Is.EqualTo("z"));
            Assert.That(nb.Notes[0].Text, Is.EqualTo("z"));
        }

        [Test]
        public void UpdateNote_NullBecomesEmpty()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            nb.UpdateNote("A", null, null);
            Assert.That(nb.FindNote("A").Title, Is.EqualTo(""));
            Assert.That(nb.FindNote("A").Text, Is.EqualTo(""));
        }

        [Test]
        public void UpdateNote_UnknownId_Throws()
        {
            Assert.Throws<KeyNotFoundException>(() => Fresh().UpdateNote("nope", "a", "b"));
        }

        [Test]
        public void FindNote_Absent_ReturnsNull()
        {
            Assert.That(Fresh().FindNote("nope"), Is.Null);
        }

        [Test]
        public void RemoveNote_RemovesAndRecordsId()
        {
            var nb = Fresh(false, N("A", "t", "x"), N("B", "u", "y"));
            nb.RemoveNote("A");
            Assert.That(nb.Notes.Count, Is.EqualTo(1));
            Assert.That(nb.RemovedIds, Does.Contain("A"));
            Assert.That(nb.IsDirty, Is.True);
        }

        [Test]
        public void RemoveNote_UnknownId_Throws()
        {
            Assert.Throws<KeyNotFoundException>(() => Fresh().RemoveNote("nope"));
        }

        [Test]
        public void ReadOnly_MutationsThrow()
        {
            var nb = Fresh(true, N("A", "t", "x"));
            Assert.That(nb.IsReadOnly, Is.True);
            Assert.Throws<InvalidOperationException>(() => nb.AddNote("a"));
            Assert.Throws<InvalidOperationException>(() => nb.UpdateNote("A", "a", "b"));
            Assert.Throws<InvalidOperationException>(() => nb.RemoveNote("A"));
        }

        [Test]
        public void AddNote_TitleSanitizedToXmlSafe()
        {
            Assert.That(Fresh().AddNote("a\u0001b").Title, Is.EqualTo("ab"));
        }

        [Test]
        public void UpdateNote_TitleLoneSurrogateDropped()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            nb.UpdateNote("A", "\uD800x", "x");
            Assert.That(nb.FindNote("A").Title, Is.EqualTo("x"));
        }

        [Test]
        public void UpdateNote_ChangeOnlyBySanitization_NotDirty()
        {
            var nb = Fresh(false, N("A", "ab", "x"));
            nb.UpdateNote("A", "a\u0001b\uFFFF", "x");
            Assert.That(nb.IsDirty, Is.False);
            Assert.That(nb.FindNote("A").Title, Is.EqualTo("ab"));
        }

        // Inputs without lone surrogates: StrUtil.SafeXmlString Debug.Asserts on those (KeePassLib is built in Debug here).
        [TestCase("")]
        [TestCase("plain title")]
        [TestCase("a\u0001b\u0008c\u000Bd\u001Fe")]
        [TestCase("tab\tnl\ncr\r")]
        [TestCase("\uFFFE\uFFFFz\uFFFD\uE000\uD7FF")]
        [TestCase("pair \uD83D\uDE00 ok")]
        public void XmlSafe_MatchesKeePassLibSafeXmlString(string s)
        {
            Assert.That(XmlSafeText.StripXmlInvalid(s), Is.EqualTo(KeePassLib.Utility.StrUtil.SafeXmlString(s)));
        }

        [Test]
        public void TitleRule_SingleLine_WhitespaceOnlyBecomesEmpty()
        {
            Assert.That(XmlSafeText.Sanitize(" "), Is.EqualTo(""));
            Assert.That(XmlSafeText.Sanitize("   "), Is.EqualTo(""));
            Assert.That(XmlSafeText.Sanitize("\t"), Is.EqualTo(""));
            Assert.That(XmlSafeText.Sanitize("\r\n"), Is.EqualTo(""));
            Assert.That(XmlSafeText.Sanitize(" \u0001 "), Is.EqualTo(""));
            Assert.That(XmlSafeText.Sanitize("a\r\nb"), Is.EqualTo("a b"));
            Assert.That(XmlSafeText.Sanitize("a\rb"), Is.EqualTo("a b"));
            Assert.That(XmlSafeText.Sanitize("a\nb"), Is.EqualTo("a b"));
            Assert.That(XmlSafeText.Sanitize("a\tb"), Is.EqualTo("a b"));
            Assert.That(XmlSafeText.Sanitize("a\r\n\r\nb"), Is.EqualTo("a  b"));
            Assert.That(XmlSafeText.Sanitize("  lead"), Is.EqualTo("  lead"));
            Assert.That(XmlSafeText.Sanitize("trail  "), Is.EqualTo("trail  "));
            Assert.That(XmlSafeText.Sanitize("\tx\n"), Is.EqualTo(" x "));
        }

        [Test]
        public void UpdateNote_TitleChangeOnlyByWhitespaceRule_NotDirty()
        {
            var nb = Fresh(false, N("A", "a b", "x"));
            nb.UpdateNote("A", "a\r\nb", "x");
            Assert.That(nb.IsDirty, Is.False);
        }

        [Test]        public void XmlSafe_DropsLoneSurrogates()
        {
            // Not [TestCase]: attribute strings are stored as UTF-8, which would mangle lone surrogates.
            Assert.That(XmlSafeText.Sanitize("\uD800"), Is.EqualTo(""));
            Assert.That(XmlSafeText.Sanitize("a\uD800b"), Is.EqualTo("ab"));
            Assert.That(XmlSafeText.Sanitize("a\uDC00b"), Is.EqualTo("ab"));
            Assert.That(XmlSafeText.Sanitize("\uDC00\uD800"), Is.EqualTo(""));
            Assert.That(XmlSafeText.Sanitize("\uD800\uD83D\uDE00"), Is.EqualTo("\uD83D\uDE00"));
            Assert.That(XmlSafeText.Sanitize(null), Is.EqualTo(""));
        }

        [Test]
        public void UpdateNote_TextLoneSurrogateBecomesReplacementChar_ControlCharsKept()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            nb.UpdateNote("A", "t", "a\u0001\uD800b\uDC00");
            Assert.That(nb.FindNote("A").Text, Is.EqualTo("a\u0001\uFFFDb\uFFFD"));
        }

        [Test]
        public void UpdateNote_TextChangeOnlyByNormalization_NotDirty()
        {
            var nb = Fresh(false, N("A", "t", "a\uFFFD"));
            nb.UpdateNote("A", "t", "a\uD800");
            Assert.That(nb.IsDirty, Is.False);
        }

        [Test]
        public void StorableText_MatchesUtf8RoundTrip()
        {
            // The rule mirrors what UTF-8 storage does (KeePassLib StrUtil.Utf8 replaces invalid chars with U+FFFD).
            foreach (string s in new[] { "", "plain", "a\u0001b", "\uD800", "x\uDC00y", "\uDC00\uD800", "\uD800\uD83D\uDE00z", "\uD83D\uDE00" })
                Assert.That(XmlSafeText.NormalizeText(s), Is.EqualTo(KeePassLib.Utility.StrUtil.Utf8.GetString(KeePassLib.Utility.StrUtil.Utf8.GetBytes(s))), s);
        }
        [Test]
        public void MarkClean_ResetsDirtyAndRemovedIds()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            int count = 0;
            nb.DirtyChanged += (s, e) => count++;
            nb.RemoveNote("A");
            nb.MarkClean();
            Assert.That(nb.IsDirty, Is.False);
            Assert.That(nb.RemovedIds, Is.Empty);
            Assert.That(count, Is.EqualTo(2));
        }

        // ---- UX round 2, U3: undo delete ----

        [Test]
        public void RestoreNote_ReinsertsTheSameNoteAtItsIndex_Dirty_NoLongerRemoved()
        {
            var nb = Fresh(false, N("A", "t", "x"), N("B", "u", "y"), N("C", "v", "z"));
            Note b = nb.FindNote("B");
            nb.RemoveNote("B");
            nb.MarkClean();
            nb.RemoveNote("A");
            Note a = Note.Create("A", "t", "x", DateTime.UtcNow);

            nb.RestoreNote(b, 1);

            Assert.That(nb.Notes.Select(n => n.Id), Is.EqualTo(new[] { "C", "B" }), "index 1 is now the end");
            Assert.That(nb.FindNote("B").Title, Is.EqualTo("u"));
            Assert.That(nb.FindNote("B").Text, Is.EqualTo("y"));
            Assert.That(nb.IsDirty, Is.True);

            nb.RestoreNote(a, 0);
            Assert.That(nb.Notes.Select(n => n.Id), Is.EqualTo(new[] { "A", "C", "B" }));
            Assert.That(nb.RemovedIds, Does.Not.Contain("A"), "a restored note is no longer pending removal");
        }

        [Test]
        public void RestoreNote_IndexOutOfRange_IsClamped()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            nb.RestoreNote(N("B", "u", "y"), 99);
            nb.RestoreNote(N("C", "v", "z"), -3);
            Assert.That(nb.Notes.Select(n => n.Id), Is.EqualTo(new[] { "C", "A", "B" }));
        }

        [Test]
        public void RestoreNote_IdAlreadyPresent_Throws_ReadOnlyThrows_NullThrows()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            Assert.Throws<InvalidOperationException>(() => nb.RestoreNote(N("A", "t2", "x2"), 0));
            Assert.Throws<ArgumentNullException>(() => nb.RestoreNote(null, 0));
            Assert.That(nb.IsDirty, Is.False);
            Assert.Throws<InvalidOperationException>(() => Fresh(true).RestoreNote(N("B", "u", "y"), 0));
        }

        [Test]
        public void IsInSavedFile_LoadedNotesYes_NewNotesNo_UntilMarkClean()
        {
            var nb = Fresh(false, N("A", "t", "x"));
            Note added = nb.AddNote("new");

            Assert.That(nb.IsInSavedFile("A"), Is.True, "loaded from the file");
            Assert.That(nb.IsInSavedFile(added.Id), Is.False, "created this session");

            nb.RemoveNote("A");
            Assert.That(nb.IsInSavedFile("A"), Is.True, "the deletion is not saved yet");

            nb.MarkClean(); // a successful save
            Assert.That(nb.IsInSavedFile(added.Id), Is.True);
            Assert.That(nb.IsInSavedFile("A"), Is.False, "no longer in the saved file");
            Assert.That(nb.IsInSavedFile(null), Is.False);
        }
    }
}

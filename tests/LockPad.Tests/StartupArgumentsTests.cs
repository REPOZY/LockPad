using LockPad.App;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// Bug fix 2026-10-21: "Open with" starts LockPad.exe with the double-clicked file as its first argument. Only the
    /// first argument counts; surrounding quotes and whitespace are trimmed. The result is still untrusted: the unlock
    /// page validates it like the remembered path.
    /// </summary>
    [TestFixture]
    public class StartupArgumentsTests
    {
        [Test]
        public void NoArguments_Null()
        {
            Assert.That(StartupArguments.NotebookPathFrom(null), Is.Null);
            Assert.That(StartupArguments.NotebookPathFrom(new string[0]), Is.Null);
        }

        [Test]
        public void QuotedPathWithSpaces_IsTrimmed()
        {
            Assert.That(StartupArguments.NotebookPathFrom(new[] { "  \"E:\\a b\\n.kdbx\"  " }), Is.EqualTo(@"E:\a b\n.kdbx"));
            Assert.That(StartupArguments.NotebookPathFrom(new[] { @"E:\backup\notes.kdbx" }), Is.EqualTo(@"E:\backup\notes.kdbx"));
        }

        [Test]
        public void TwoArguments_TheFirst()
        {
            Assert.That(StartupArguments.NotebookPathFrom(new[] { @"E:\first.kdbx", @"E:\second.kdbx" }),
                Is.EqualTo(@"E:\first.kdbx"));
        }

        [Test]
        public void EmptyArgument_Null()
        {
            Assert.That(StartupArguments.NotebookPathFrom(new[] { "" }), Is.Null);
            Assert.That(StartupArguments.NotebookPathFrom(new string[] { null }), Is.Null);
            Assert.That(StartupArguments.NotebookPathFrom(new[] { "   " }), Is.Null);
            Assert.That(StartupArguments.NotebookPathFrom(new[] { " \"\" " }), Is.Null, "only quotes");
        }

        [Test]
        public void EmptyFirstArgument_Null_ExtrasIgnored()
        {
            Assert.That(StartupArguments.NotebookPathFrom(new[] { "", @"E:\second.kdbx" }), Is.Null);
        }
    }
}

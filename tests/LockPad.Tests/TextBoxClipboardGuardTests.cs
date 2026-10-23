using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LockPad.App.Services;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// The guard's class handlers are process-wide and permanent; Register swaps the services they use, so each test
    /// registers its own fakes. Never touches the real clipboard: a copy that bypassed the guard would raise
    /// DataObject.Copying on the TextBox, which these tests detect.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class TextBoxClipboardGuardTests
    {
        private FakeClipboardService _clipboard;
        private FakeDialogService _dialogs;

        [SetUp]
        public void SetUp()
        {
            _clipboard = new FakeClipboardService();
            _dialogs = new FakeDialogService();
            TextBoxClipboardGuard.Register(_clipboard, _dialogs);
        }

        private static TextBox Box(string text, int start, int length, bool readOnly = false)
        {
            var tb = new TextBox { Text = text, IsReadOnly = readOnly };
            tb.Select(start, length);
            return tb;
        }

        /// <summary>Counts WPF's own copy path: it raises DataObject.Copying before it writes the clipboard.</summary>
        private static Func<int> CountDefaultCopies(TextBox tb)
        {
            int count = 0;
            DataObject.AddCopyingHandler(tb, (s, e) => count++);
            return () => count;
        }

        [Test]
        public void Copy_RoutesSelectionToProtectedClipboard_DefaultCopyNeverRuns()
        {
            TextBox tb = Box("abc secret def", 4, 6);
            Func<int> defaultCopies = CountDefaultCopies(tb);

            Assert.That(ApplicationCommands.Copy.CanExecute(null, tb), Is.True);
            ApplicationCommands.Copy.Execute(null, tb);

            Assert.That(_clipboard.Copied, Is.EqualTo("secret"));
            Assert.That(_clipboard.CopyCount, Is.EqualTo(1));
            Assert.That(defaultCopies(), Is.EqualTo(0));
            Assert.That(tb.Text, Is.EqualTo("abc secret def"));
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void Copy_ReadOnlyTextBox_StillProtected()
        {
            TextBox tb = Box("abc secret def", 4, 6, readOnly: true);
            Func<int> defaultCopies = CountDefaultCopies(tb);

            ApplicationCommands.Copy.Execute(null, tb);

            Assert.That(_clipboard.Copied, Is.EqualTo("secret"));
            Assert.That(defaultCopies(), Is.EqualTo(0));
        }

        [Test]
        public void Copy_EmptySelection_IsNoOp()
        {
            TextBox tb = Box("abc", 1, 0);
            _clipboard.ThrowOnCopy = true; // proves the clipboard is not even called
            Func<int> defaultCopies = CountDefaultCopies(tb);

            Assert.That(ApplicationCommands.Copy.CanExecute(null, tb), Is.False);
            Assert.That(ApplicationCommands.Cut.CanExecute(null, tb), Is.False);
            ApplicationCommands.Copy.Execute(null, tb);
            ApplicationCommands.Cut.Execute(null, tb);

            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(defaultCopies(), Is.EqualTo(0));
            Assert.That(tb.Text, Is.EqualTo("abc"));
        }

        [Test]
        public void Cut_CopiesAndRemovesSelection()
        {
            TextBox tb = Box("abc secret def", 4, 6);
            Func<int> defaultCopies = CountDefaultCopies(tb);

            Assert.That(ApplicationCommands.Cut.CanExecute(null, tb), Is.True);
            ApplicationCommands.Cut.Execute(null, tb);

            Assert.That(_clipboard.Copied, Is.EqualTo("secret"));
            Assert.That(tb.Text, Is.EqualTo("abc  def"));
            Assert.That(defaultCopies(), Is.EqualTo(0));
        }

        [Test]
        public void Cut_ReadOnly_CopiesButDeletesNothing()
        {
            TextBox tb = Box("abc secret def", 4, 6, readOnly: true);

            Assert.That(ApplicationCommands.Cut.CanExecute(null, tb), Is.True);
            ApplicationCommands.Cut.Execute(null, tb);

            Assert.That(_clipboard.Copied, Is.EqualTo("secret"));
            Assert.That(tb.Text, Is.EqualTo("abc secret def"));
        }

        [Test]
        public void Copy_Failure_ShowsCopyFailed_WithoutTheText()
        {
            TextBox tb = Box("abc secret def", 4, 6);
            _clipboard.ThrowOnCopy = true;
            Func<int> defaultCopies = CountDefaultCopies(tb);

            Assert.DoesNotThrow(() => ApplicationCommands.Copy.Execute(null, tb));

            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Copy failed"));
            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo("The text could not be copied. Try again."));
            Assert.That(_dialogs.LastErrorMessage, Does.Not.Contain("secret"));
            Assert.That(defaultCopies(), Is.EqualTo(0), "no fallback to the unprotected copy");
        }

        [Test]
        public void Cut_Failure_KeepsText()
        {
            TextBox tb = Box("abc secret def", 4, 6);
            _clipboard.ThrowOnCopy = true;

            ApplicationCommands.Cut.Execute(null, tb);

            Assert.That(tb.Text, Is.EqualTo("abc secret def"));
            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Copy failed"));
        }

        [Test]
        public void DragOutCopy_IsCancelled()
        {
            TextBox tb = Box("abc secret def", 4, 6);
            var args = new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "secret"), true);

            tb.RaiseEvent(args);

            Assert.That(args.CommandCancelled, Is.True);
        }

        [Test]
        public void NonDragCopyingEvent_IsCancelled()
        {
            // Any WPF-internal copy (e.g. TextBox.Copy() called from code) would bypass the guarded commands; the
            // guard cancels it too, so only CopyProtected ever writes TextBox text to the clipboard.
            TextBox tb = Box("abc secret def", 4, 6);
            var args = new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "secret"), false);

            tb.RaiseEvent(args);

            Assert.That(args.CommandCancelled, Is.True);
        }

        [Test]
        public void ProgrammaticCopyAndCut_AreCancelled_AndDetectorSeesThem()
        {
            // TextBox.Copy()/Cut() bypass the commands and use WPF's own copy, which raises Copying first. This also
            // proves CountDefaultCopies detects that path, so the "never runs" assertions above are meaningful.
            TextBox tb = Box("abc plain def", 4, 5);
            Func<int> defaultCopies = CountDefaultCopies(tb);

            tb.Copy();
            tb.Cut();

            Assert.That(defaultCopies(), Is.EqualTo(2));
            Assert.That(tb.Text, Is.EqualTo("abc plain def"), "a cancelled cut deletes nothing");
            Assert.That(_clipboard.Copied, Is.Null);
        }

        [Test]
        public void Register_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => TextBoxClipboardGuard.Register(null, _dialogs));
            Assert.Throws<ArgumentNullException>(() => TextBoxClipboardGuard.Register(_clipboard, null));
        }
    }
}

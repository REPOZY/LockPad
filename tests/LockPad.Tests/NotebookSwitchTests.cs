using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LockPad.App;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// 2026-10-21: File > New notebook / Open notebook from inside LockPad (switching closes the current notebook like
    /// closing the window: save, or ask), the complete File menu, and the notebook's file name in the window title.
    /// </summary>
    [TestFixture]
    public class NotebookSwitchTests
    {
        private static readonly string Ellipsis = ((char)0x2026).ToString();

        private TempDir _dir;
        private FakeDialogService _dialogs;
        private FakeClipboardService _clipboard;
        private SettingsStore _settingsStore;
        private AppSettings _settings;
        private NotebookStore _store;
        private FakeClock _clock;
        private DateTime _wall;
        private List<Notebook> _notebooks;

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
            _clipboard = new FakeClipboardService();
            _settingsStore = new SettingsStore(_dir.File("settings"));
            _settings = new AppSettings();
            _store = new NotebookStore();
            _clock = new FakeClock();
            _wall = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
            _notebooks = new List<Notebook>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _notebooks) nb.Dispose();
            _dir.Dispose();
        }

        // ---- helpers ----

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        private string NotebookPath { get { return _dir.File("nb.kdbx"); } }

        /// <summary>A second notebook on disk ("other.kdbx", password "pw", one note "Other").</summary>
        private string OtherNotebook()
        {
            string p = _dir.File("other.kdbx");
            using (Notebook nb = new NotebookStore().Create(p, Pw(), TestKdf.Fast()))
            {
                nb.AddNote("Other");
                Assert.That(new NotebookStore().Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            return p;
        }

        private ShellViewModel NewShell()
        {
            var shell = new ShellViewModel(_store, _settingsStore, _settings, _dialogs, _clipboard, _clock, TestKdf.Fast);
            shell.WallClock = () => _wall;
            return shell;
        }

        private void Advance(TimeSpan t)
        {
            _clock.Advance(t);
            _wall = _wall + t;
        }

        private Notebook NewNotebook()
        {
            using (new NotebookStore().Create(NotebookPath, Pw(), TestKdf.Fast())) { }
            Notebook nb = _store.Open(NotebookPath, Pw());
            _notebooks.Add(nb);
            foreach (string title in new[] { "Ledger", "Bank" })
            {
                Note n = nb.AddNote(title);
                nb.UpdateNote(n.Id, title, "original " + title);
            }
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            return nb;
        }

        private ShellViewModel OpenShell(out Notebook nb)
        {
            ShellViewModel shell = NewShell();
            nb = NewNotebook();
            shell.ShowEditor(nb);
            Quiet(shell);
            return shell;
        }

        private static void Quiet(ShellViewModel shell)
        {
            if (shell.Editor != null) shell.Editor.BeginBusy = () => new NoopDisposable();
        }

        private static string Edit(ShellViewModel shell, string text)
        {
            EditorViewModel e = shell.Editor;
            e.SelectedNote = e.VisibleNotes.First(i => i.DisplayTitle == "Ledger");
            e.EditorText = text;
            return e.SelectedNote.Id;
        }

        private static string NoteText(string path, string title)
        {
            using (Notebook nb = new NotebookStore().Open(path, Pw()))
                return nb.Notes.Single(n => n.Title == title).Text;
        }

        private static void ChangeExternally(string path)
        {
            var other = new NotebookStore();
            using (Notebook nb = other.Open(path, Pw()))
            {
                nb.AddNote("from elsewhere");
                Assert.That(other.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
        }

        private void MakeFileReadOnly() { File.SetAttributes(NotebookPath, FileAttributes.ReadOnly); }

        private static void AssertDisposed(NotebookStore store, Notebook nb)
        {
            Assert.Throws<ObjectDisposedException>(() => store.Save(nb, false));
        }

        private ShellViewModel LockedWithHeldNotebook(out Notebook nb)
        {
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited while away");
            MakeFileReadOnly();
            shell.Lock();
            Assert.That(((UnlockViewModel)shell.CurrentView).HasHeldNotebook, Is.True);
            return shell;
        }

        private static async Task Unlock(ShellViewModel shell, string password)
        {
            var unlock = (UnlockViewModel)shell.CurrentView;
            using (SecureString ss = UnlockViewModelTests.Secure(password)) await unlock.UnlockAsync(ss);
            Quiet(shell);
        }

        /// <summary>The editor is shown, intact and still owns its (dirty) notebook.</summary>
        private void AssertStillInEditor(ShellViewModel shell, EditorViewModel editor, Notebook nb, string id, string text)
        {
            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(shell.CurrentView, Is.SameAs(editor));
            Assert.That(nb.FindNote(id).Text, Is.EqualTo(text), "notes intact");
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(editor.SelectedNote, Is.Not.Null, "the view was not emptied");
            Assert.That(editor.EditorText, Is.EqualTo(text));
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }

        // ---- Open notebook from the editor ----

        [Test]
        public void Open_FromEditor_PickerCancelled_NothingChanges()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.True);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath" }), "the picker first, nothing else");
            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(shell.CurrentView, Is.SameAs(editor));
            Assert.That(_clipboard.ClearCount, Is.EqualTo(0));
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "still open");
        }

        [Test]
        public void Open_FromEditor_Clean_ShowsUnlockWithChosenPath_DisposesAndClearsClipboard()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string other = OtherNotebook();
            byte[] before = File.ReadAllBytes(NotebookPath);
            _dialogs.OpenPaths.Enqueue(other);
            _clipboard.OwnsClipboardContent = true;

            shell.OpenNotebookCommand.Execute(null);

            var unlock = shell.CurrentView as UnlockViewModel;
            Assert.That(unlock, Is.Not.Null, "the plain unlock page");
            Assert.That(unlock.HasHeldNotebook, Is.False);
            Assert.That(unlock.NotebookPath, Is.EqualTo(Path.GetFullPath(other)));
            Assert.That(unlock.ErrorMessage, Is.Null);
            Assert.That(shell.Editor, Is.Null);
            AssertDisposed(_store, nb);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False, "LockPad's clipboard content was cleared");
            Assert.That(editor.EditorText, Is.EqualTo(""), "the editor was emptied");
            Assert.That(editor.VisibleNotes, Is.Empty);
            Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before), "nothing to save");
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath" }));
            Assert.That(shell.LockCountdownText, Is.EqualTo(""));
        }

        [Test]
        public void Open_FromEditor_Dirty_SavesThenShowsUnlockPage()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"), "saved first");
            Assert.That(((UnlockViewModel)shell.CurrentView).NotebookPath, Is.EqualTo(Path.GetFullPath(other)));
            AssertDisposed(_store, nb);
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath" }), "a successful save asks nothing");
        }

        [Test]
        public void Open_FromEditor_SaveFails_Cancel_StaysInEditor_NotesIntact()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string id = Edit(shell, "edited");
            MakeFileReadOnly();
            _dialogs.OpenPaths.Enqueue(OtherNotebook());
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.Cancel);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath", "AskCloseAfterSaveFailure" }));
            Assert.That(_dialogs.LastCloseChoiceTitle, Is.EqualTo("Could not save"));
            AssertStillInEditor(shell, editor, nb, id, "edited");
            Assert.That(_clipboard.ClearCount, Is.EqualTo(0));
        }

        [Test]
        public void Open_FromEditor_SaveFails_CloseWithoutSavingDeclined_StaysInEditor()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string id = Edit(shell, "edited");
            MakeFileReadOnly();
            _dialogs.OpenPaths.Enqueue(OtherNotebook());
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.CloseWithoutSaving);
            _dialogs.Confirms.Enqueue(false);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(_dialogs.Calls,
                Is.EqualTo(new[] { "PickOpenNotebookPath", "AskCloseAfterSaveFailure", "ConfirmDefaultNo" }));
            AssertStillInEditor(shell, editor, nb, id, "edited");
        }

        [Test]
        public void Open_FromEditor_SaveFails_CloseWithoutSavingConfirmed_Switches()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            MakeFileReadOnly();
            byte[] before = File.ReadAllBytes(NotebookPath);
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.CloseWithoutSaving);
            _dialogs.Confirms.Enqueue(true);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(_dialogs.Calls,
                Is.EqualTo(new[] { "PickOpenNotebookPath", "AskCloseAfterSaveFailure", "ConfirmDefaultNo" }));
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo("Discard all unsaved changes? This cannot be undone."));
            Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before), "nothing written");
            AssertDisposed(_store, nb);
            Assert.That(((UnlockViewModel)shell.CurrentView).NotebookPath, Is.EqualTo(Path.GetFullPath(other)));
        }

        [Test]
        public void Open_FromEditor_SaveFails_SaveAsSucceeds_Switches()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            MakeFileReadOnly();
            string other = OtherNotebook();
            string copy = _dir.File("copy.kdbx");
            _dialogs.OpenPaths.Enqueue(other);
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.SaveAs);
            _dialogs.SaveAsPaths.Enqueue(copy);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(NoteText(copy, "Ledger"), Is.EqualTo("edited"));
            AssertDisposed(_store, nb);
            Assert.That(((UnlockViewModel)shell.CurrentView).NotebookPath, Is.EqualTo(Path.GetFullPath(other)));
        }

        [Test]
        public void Open_FromEditor_ExternalChange_Cancel_StaysInEditor()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            ChangeExternally(NotebookPath);
            string id = Edit(shell, "edited");
            _dialogs.OpenPaths.Enqueue(OtherNotebook());
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.Cancel);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath", "AskExternalChange" }),
                "the user is present: the question is asked; Cancel needs no further dialog");
            AssertStillInEditor(shell, editor, nb, id, "edited");
        }

        [Test]
        public void Open_FromEditor_ObscuredWhilePickerOpen_NoSwitch_ThePendingLockProceeds()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);
            _dialogs.OnPickOpen = () =>
            {
                // The idle timeout expires while the picker is open (the picker's modal loop runs the timer).
                _dialogs.IsShowingDialog = true;
                Advance(TimeSpan.FromMinutes(30));
                shell.Tick();
                _dialogs.IsShowingDialog = false;
            };

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(editor.IsObscured, Is.True);
            Assert.That(shell.Editor, Is.SameAs(editor), "the choice was discarded: no switch");
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath" }), "no save dialogs");

            shell.Tick(); // the pending lock: saves, then the plain unlock page of the old notebook
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.False);
            Assert.That(unlock.NotebookPath, Is.Not.EqualTo(Path.GetFullPath(other)), "the chosen file is not used");
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            AssertDisposed(_store, nb);
        }

        [Test]
        public void Open_FromEditor_LockedWhilePickerOpen_NoSwitch()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);
            object lockedPage = null;
            _dialogs.OnPickOpen = () =>
            {
                shell.Lock(); // no longer the current view when the picker returns
                lockedPage = shell.CurrentView;
            };

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(shell.CurrentView, Is.SameAs(lockedPage), "nothing more happens");
            Assert.That(((UnlockViewModel)shell.CurrentView).NotebookPath, Is.Not.EqualTo(Path.GetFullPath(other)));
        }

        [Test]
        public void Open_FromEditor_ObscuredDuringTheSave_ReleasedNotebookEndsOnThePlainUnlockPage()
        {
            // The idle expiry arrives inside the external-change question; the user then overwrites. The save and the
            // release complete, but the switch ends like the lock it became: the plain unlock page, not the choice.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            _settings.LastNotebookPath = NotebookPath; // as after unlocking it
            ChangeExternally(NotebookPath);
            Edit(shell, "edited");
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.Overwrite);
            _dialogs.OnExternalChange = () =>
            {
                _dialogs.IsShowingDialog = true;
                Advance(TimeSpan.FromMinutes(30));
                shell.Tick();
                _dialogs.IsShowingDialog = false;
                Assert.That(editor.IsObscured, Is.True);
                Assert.That(shell.Editor, Is.SameAs(editor), "deferred during the save");
            };

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            AssertDisposed(_store, nb);
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.False);
            Assert.That(unlock.NotebookPath, Is.EqualTo(Path.GetFullPath(NotebookPath)), "the lock's page");
        }

        [Test]
        public void Open_FromEditor_RejectedChosenPath_ShowsAnError_TheCurrentNotebookStaysOpenUntouched()
        {
            // Fix round 1, X4 (red team L1): the chosen path is checked BEFORE the current notebook is closed.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string id = Edit(shell, "edited");
            byte[] before = File.ReadAllBytes(NotebookPath);
            foreach (string rejected in new[]
            {
                _dir.File("gone.kdbx"),                 // e.g. deleted after it was picked
                @"\\server\share\notebook.kdbx",        // a network path: never touched
            })
            {
                _dialogs.Calls.Clear();
                _dialogs.OpenPaths.Enqueue(rejected);

                shell.OpenNotebookCommand.Execute(null);

                Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath", "ShowError" }), rejected);
                Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Cannot open notebook"));
                Assert.That(_dialogs.LastErrorMessage, Is.EqualTo(UnlockViewModel.RejectedStartupPathMessage(rejected)));
                AssertStillInEditor(shell, editor, nb, id, "edited");
                Assert.That(nb.Notes.Count, Is.EqualTo(2), "not disposed");
                Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before), "nothing saved");
                Assert.That(_clipboard.ClearCount, Is.EqualTo(0));
            }
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "still open and usable");
        }

        [Test]
        public void Open_FromEditor_AcceptedChosenPath_StillSwitches()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath" }));
            Assert.That(((UnlockViewModel)shell.CurrentView).NotebookPath, Is.EqualTo(Path.GetFullPath(other)));
            AssertDisposed(_store, nb);
        }

        // ---- the "Could not save" wording during a switch (fix round 1, X5) ----

        private const string SwitchSuffix = "\n\nSave As keeps your changes in a new file. Close without saving discards "
            + "them. Cancel keeps this notebook open.";
        private const string CloseSuffix = "\n\nSave As keeps your changes in a new file. Close without saving discards "
            + "them. Cancel keeps the window open.";

        [TestCase(true)]
        [TestCase(false)]
        public void Switch_CouldNotSave_SaysCancelKeepsThisNotebookOpen_ConfirmationUnchanged(bool open)
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string id = Edit(shell, "edited");
            MakeFileReadOnly();
            if (open) _dialogs.OpenPaths.Enqueue(OtherNotebook());
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.CloseWithoutSaving);
            _dialogs.Confirms.Enqueue(false);

            if (open) shell.OpenNotebookCommand.Execute(null);
            else shell.NewNotebookCommand.Execute(null);

            Assert.That(_dialogs.LastCloseChoiceTitle, Is.EqualTo("Could not save"));
            Assert.That(_dialogs.LastCloseChoiceMessage, Does.EndWith(SwitchSuffix));
            Assert.That(_dialogs.LastConfirmTitle, Is.EqualTo("Close without saving"), "the second confirmation");
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo("Discard all unsaved changes? This cannot be undone."));
            AssertStillInEditor(shell, editor, nb, id, "edited");
        }

        [Test]
        public void Close_CouldNotSave_KeepsTheWindowWording()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            MakeFileReadOnly();
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.Cancel);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.LastCloseChoiceMessage, Does.EndWith(CloseSuffix));
        }

        // ---- the switch's "Could not save" dialog meets an idle lock or a session end (fix round 1, X10) ----

        [Test]
        public void Switch_ObscuredDuringCouldNotSave_ThenCloseWithoutSavingConfirmed_PlainUnlockPage()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            _settings.LastNotebookPath = NotebookPath; // as after unlocking it
            Edit(shell, "edited");
            MakeFileReadOnly();
            byte[] before = File.ReadAllBytes(NotebookPath);
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.CloseWithoutSaving);
            _dialogs.Confirms.Enqueue(true);
            _dialogs.OnCloseChoice = () =>
            {
                _dialogs.IsShowingDialog = true;
                Advance(TimeSpan.FromMinutes(30));
                shell.Tick();
                _dialogs.IsShowingDialog = false;
                Assert.That(editor.IsObscured, Is.True, "obscured, the lock waits for the dialog");
                Assert.That(shell.Editor, Is.SameAs(editor));
            };

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(_dialogs.Calls,
                Is.EqualTo(new[] { "PickOpenNotebookPath", "AskCloseAfterSaveFailure", "ConfirmDefaultNo" }));
            Assert.That(shell.Editor, Is.Null);
            AssertDisposed(_store, nb);
            Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before), "the confirmed discard wrote nothing");
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.False);
            Assert.That(unlock.NotebookPath, Is.EqualTo(Path.GetFullPath(NotebookPath)), "the plain unlock page");
            Assert.That(unlock.NotebookPath, Is.Not.EqualTo(Path.GetFullPath(other)), "not the chosen file");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Switch_SessionEndDuringCouldNotSave_SavesOrRefuses_NeverLosesTheNotes(bool writableAgain)
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string id = Edit(shell, "edited");
            MakeFileReadOnly();
            _dialogs.OpenPaths.Enqueue(OtherNotebook());
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.Cancel);
            bool? allowed = null;
            _dialogs.OnCloseChoice = () =>
            {
                _dialogs.IsShowingDialog = true;
                if (writableAgain) File.SetAttributes(NotebookPath, FileAttributes.Normal);
                allowed = shell.PrepareForSessionEnd();
                _dialogs.IsShowingDialog = false;
            };

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(allowed, Is.EqualTo(writableAgain), "the session end is allowed only when the notes were saved");
            Assert.That(shell.Editor, Is.SameAs(editor), "Cancel: still in the editor");
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("edited"), "notes intact in memory");
            if (writableAgain)
            {
                Assert.That(nb.IsDirty, Is.False);
                Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"), "saved by the session end");
            }
            else
            {
                Assert.That(nb.IsDirty, Is.True, "refused: the changes stay, unsaved");
                Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("original Ledger"));
                File.SetAttributes(NotebookPath, FileAttributes.Normal);
                Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "still savable");
            }
        }

        // ---- the idle lock is never blocked by the menu refresh (fix round 1, X6) ----

        // ---- fix round 2: the session-end save cannot be re-entered; the timer never throws ----

        [Test]
        public void SessionEndSave_ReentrantClose_NeverHoldsADisposedNotebook()
        {
            // Red team L1: a WM_CLOSE pumped during the session-end save's waits ran TryClose, which disposed the notebook
            // under the save. Simulated by re-entering TryClose from DirtyChanged, raised inside the save.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            bool? reentrantClose = null;
            nb.DirtyChanged += (s, e) =>
            {
                if (reentrantClose == null) reentrantClose = shell.TryClose();
            };

            bool allowed = shell.PrepareForSessionEnd();

            Assert.That(reentrantClose, Is.False, "refused while the session-end save runs");
            Assert.That(allowed, Is.True);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"), "the content is on disk");
            Assert.That(nb.IsDisposed, Is.False);
            Assert.That(shell.Editor, Is.SameAs(editor), "the editor stays in case the session end is aborted");
            var unlock = shell.CurrentView as UnlockViewModel;
            Assert.That(unlock == null || !unlock.HasHeldNotebook, Is.True, "no held page");
        }

        [Test]
        public void SessionEndSave_ReentrantSwitchAndLock_Refused()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            bool? openEnabled = null, newEnabled = null;
            object viewAfterLock = null;
            nb.DirtyChanged += (s, e) =>
            {
                if (openEnabled != null) return;
                openEnabled = shell.OpenNotebookCommand.CanExecute(null);
                newEnabled = shell.NewNotebookCommand.CanExecute(null);
                shell.NewNotebook();
                shell.Lock();
                viewAfterLock = shell.CurrentView;
            };

            Assert.That(shell.PrepareForSessionEnd(), Is.True);

            Assert.That(openEnabled, Is.False);
            Assert.That(newEnabled, Is.False);
            Assert.That(viewAfterLock, Is.SameAs(editor), "neither the switch nor the lock ran inside the save");
            Assert.That(nb.IsDisposed, Is.False);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        public void Hold_DisposedNotebook_ShowsThePlainUnlockPage_NoHeldBanner()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            nb.Dispose(); // e.g. released by a re-entrant close; its save now fails (ObjectDisposedException)
            Assert.That(nb.IsDisposed, Is.True);

            shell.Lock();

            Assert.That(shell.Editor, Is.Null);
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.False, "never a held page for a notebook that cannot come back");
            Assert.That(unlock.HeldWarning, Is.Null, "no false \"held in memory\" text");
            Assert.That(_clipboard.ClearCount, Is.GreaterThan(0));
            Assert.That(shell.TryClose(), Is.True, "nothing is held");
        }

        [Test]
        public void Tick_ThrowingSubscriber_LocksAndDoesNotThrowOutOfTheTimer()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            shell.OpenNotebookCommand.CanExecuteChanged += (s, e) => { throw new InvalidOperationException("menu"); };
            shell.NewNotebookCommand.CanExecuteChanged += (s, e) => { throw new InvalidOperationException("menu"); };
            Advance(TimeSpan.FromMinutes(30));

            Assert.DoesNotThrow(shell.Tick, "hides the notes before the save (full check 2026-10-22, L1)");
            Assert.That(shell.Editor.IsObscured, Is.True);
            Assert.DoesNotThrow(shell.Tick);

            Assert.That(shell.Editor, Is.Null, "locked");
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            AssertDisposed(_store, nb);
            Assert.DoesNotThrow(shell.Tick, "and on the next ticks");
        }

        /// <summary>Re-attack 2026-10-22 (M1): a DispatcherTimer whose Tick handler throws is never rescheduled, so the
        /// main window's tick wrapper must never throw. A listener throwing during the lock's teardown: the lock still
        /// completes and the wrapper hands the error back instead of throwing.</summary>
        [Test]
        public void LockTick_TeardownListenerThrows_LocksAndTheTimerWrapperDoesNotThrow()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            shell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ShellViewModel.WindowTitle)) throw new InvalidOperationException("listener");
            };
            Advance(TimeSpan.FromMinutes(30));
            Assert.That(MainWindow.RunLockTick(shell), Is.Null.Or.InstanceOf<InvalidOperationException>(),
                "hides the notes before the save (full check L1); a throwing listener is reported, not thrown");
            Assert.That(shell.Editor.IsObscured, Is.True);

            Exception error = null;
            Assert.DoesNotThrow(() => error = MainWindow.RunLockTick(shell));

            Assert.That(error, Is.InstanceOf<InvalidOperationException>(), "reported, not thrown");
            Assert.That(shell.Editor, Is.Null, "locked");
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            Assert.That(MainWindow.RunLockTick(shell), Is.Null.Or.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void LockTick_ObscureListenerThrows_TheTimerWrapperDoesNotThrow()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _dialogs.IsShowingDialog = true; // lock deferred: the editor is obscured instead
            int thrown = 0;
            shell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(ShellViewModel.WindowTitle)) return;
                thrown++;
                throw new InvalidOperationException("listener");
            };
            Advance(TimeSpan.FromMinutes(30));

            Exception error = null;
            Assert.DoesNotThrow(() => error = MainWindow.RunLockTick(shell));

            Assert.That(thrown, Is.GreaterThan(0), "the listener did throw during the tick");
            Assert.That(error, Is.InstanceOf<InvalidOperationException>(), "reported, not thrown");
            Assert.That(shell.Editor.IsObscured, Is.True);
            _dialogs.IsShowingDialog = false;
        }

        /// <summary>Final re-attack 2026-10-22 (L2): a throwing countdown listener during the lock never leaves a blank
        /// window: the unlock page is still shown.</summary>
        [Test]
        public void LockTick_CountdownListenerThrows_UnlockPageStillShown()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            int thrown = 0;
            shell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(ShellViewModel.LockCountdownText) || shell.LockCountdownText != "") return;
                thrown++;
                throw new InvalidOperationException("listener");
            };
            Advance(TimeSpan.FromMinutes(30));

            Assert.That(MainWindow.RunLockTick(shell), Is.InstanceOf<InvalidOperationException>());

            Assert.That(thrown, Is.GreaterThan(0));
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>(), "never a blank window");
        }

        [Test]
        public void LockTick_NoShell_DoesNothing()
        {
            Assert.That(MainWindow.RunLockTick(null), Is.Null);
        }

        [Test]
        public void Tick_ThrowingCanExecuteSubscriber_NeverStopsTheIdleLock()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            shell.OpenNotebookCommand.CanExecuteChanged += (s, e) => { throw new InvalidOperationException("menu"); };
            Advance(TimeSpan.FromMinutes(30));

            // The first Tick hides the notes before the save (full check 2026-10-22, L1); the second saves and locks.
            for (int tick = 0; tick < 2; tick++)
            {
                try
                {
                    shell.Tick();
                }
                catch (InvalidOperationException)
                {
                    // The subscriber's failure may surface, but only after the step.
                }
            }

            Assert.That(shell.Editor, Is.Null, "locked");
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"), "saved, then locked");
            AssertDisposed(_store, nb);
        }

        [Test]
        public void Open_FromEditor_WhileDialogShownOrSaving_Disabled()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _dialogs.IsShowingDialog = true;
            Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.False, "dialog shown");
            Assert.That(shell.NewNotebookCommand.CanExecute(null), Is.False, "dialog shown");
            shell.OpenNotebookCommand.Execute(null);
            shell.NewNotebookCommand.Execute(null);
            Assert.That(_dialogs.Calls, Is.Empty, "executing does nothing");
            _dialogs.IsShowingDialog = false;

            bool? openDuringSave = null, newDuringSave = null;
            shell.Editor.BeginBusy = () =>
            {
                openDuringSave = shell.OpenNotebookCommand.CanExecute(null);
                newDuringSave = shell.NewNotebookCommand.CanExecute(null);
                return new NoopDisposable();
            };
            Edit(shell, "edited");
            shell.Editor.SaveNow();
            Assert.That(openDuringSave, Is.False, "saving");
            Assert.That(newDuringSave, Is.False, "saving");
            Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.True);
        }

        [Test]
        public void Switch_TeardownThrows_NotebookReleased_PlainUnlockPage_NeverABlankWindow()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            shell.Editor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "SelectedNote") throw new InvalidOperationException("teardown failure");
            };

            Assert.Throws<InvalidOperationException>(() => shell.NewNotebookCommand.Execute(null));

            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            Assert.That(((UnlockViewModel)shell.CurrentView).HasHeldNotebook, Is.False);
            AssertDisposed(_store, nb);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        // ---- Open / New from the plain unlock page ----

        [Test]
        public void Open_FromPlainUnlockPage_IsBrowseNotebook()
        {
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            var unlock = (UnlockViewModel)shell.CurrentView;
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);
            Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.True);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(shell.CurrentView, Is.SameAs(unlock), "the same page");
            Assert.That(unlock.NotebookPath, Is.EqualTo(other));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickOpenNotebookPath" }));
        }

        [Test]
        public void New_FromPlainUnlockPage_ShowsCreatePage()
        {
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            Assert.That(shell.NewNotebookCommand.CanExecute(null), Is.True);

            shell.NewNotebookCommand.Execute(null);

            Assert.That(shell.CurrentView, Is.InstanceOf<CreateNotebookViewModel>());
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        // ---- New notebook from the editor ----

        [Test]
        public void New_FromEditor_Clean_ShowsCreatePage()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _clipboard.OwnsClipboardContent = true;

            shell.NewNotebookCommand.Execute(null);

            Assert.That(shell.CurrentView, Is.InstanceOf<CreateNotebookViewModel>());
            Assert.That(shell.Editor, Is.Null);
            AssertDisposed(_store, nb);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
            Assert.That(_dialogs.Calls, Is.Empty, "no picker: the create page has its own");
        }

        [Test]
        public void New_FromEditor_Dirty_SavesThenShowsCreatePage()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");

            shell.NewNotebookCommand.Execute(null);

            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            Assert.That(shell.CurrentView, Is.InstanceOf<CreateNotebookViewModel>());
            AssertDisposed(_store, nb);
        }

        [Test]
        public void New_FromEditor_DirtySaveFails_Cancel_StaysInEditor()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string id = Edit(shell, "edited");
            MakeFileReadOnly();
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.Cancel);

            shell.NewNotebookCommand.Execute(null);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskCloseAfterSaveFailure" }));
            AssertStillInEditor(shell, editor, nb, id, "edited");
        }

        // ---- the held unlock page and the create page never switch ----

        [Test]
        public void HeldUnlockPage_OpenAndNewDisabled_ExecutingDoesNothing()
        {
            Notebook nb;
            ShellViewModel shell = LockedWithHeldNotebook(out nb);
            object page = shell.CurrentView;
            _dialogs.Calls.Clear();
            _dialogs.OpenPaths.Enqueue(OtherNotebook());

            Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.False);
            Assert.That(shell.NewNotebookCommand.CanExecute(null), Is.False);
            shell.OpenNotebookCommand.Execute(null);
            shell.NewNotebookCommand.Execute(null);
            shell.OpenNotebook();
            shell.NewNotebook();

            Assert.That(shell.CurrentView, Is.SameAs(page));
            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(nb.IsDirty, Is.True, "the held changes are kept");
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.IoError), "not disposed");
        }

        [Test]
        public void CreatePage_OpenAndNewDisabled_ExecutingDoesNothing()
        {
            ShellViewModel shell = NewShell();
            shell.ShowCreate();
            object page = shell.CurrentView;

            Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.False);
            Assert.That(shell.NewNotebookCommand.CanExecute(null), Is.False);
            shell.OpenNotebookCommand.Execute(null);
            shell.NewNotebookCommand.Execute(null);

            Assert.That(shell.CurrentView, Is.SameAs(page));
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public async Task BusyUnlockPage_OpenAndNewDisabled()
        {
            ShellViewModel shell = NewShell();
            string other = OtherNotebook();
            shell.ShowUnlock();
            var unlock = (UnlockViewModel)shell.CurrentView;
            unlock.NotebookPath = other;
            var gate = new TaskCompletionSource<int>();
            unlock.RunInBackground = a => gate.Task;
            int changes = 0;
            shell.OpenNotebookCommand.CanExecuteChanged += (s, e) => changes++;

            using (SecureString pw = UnlockViewModelTests.Secure("pw"))
            {
                Task attempt = unlock.UnlockAsync(pw);
                Assert.That(unlock.IsBusy, Is.True);
                Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.False);
                Assert.That(shell.NewNotebookCommand.CanExecute(null), Is.False);
                Assert.That(changes, Is.GreaterThan(0), "the menu is told");
                gate.SetResult(0);
                await attempt;
            }
            Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.True);
        }

        [Test]
        public void ObscuredEditor_OpenAndNewDisabled()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _dialogs.IsShowingDialog = true;
            Advance(TimeSpan.FromMinutes(30));
            shell.Tick();
            _dialogs.IsShowingDialog = false;
            Assert.That(shell.Editor.IsObscured, Is.True);

            Assert.That(shell.OpenNotebookCommand.CanExecute(null), Is.False);
            Assert.That(shell.NewNotebookCommand.CanExecute(null), Is.False);
            shell.OpenNotebookCommand.Execute(null);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        // ---- nothing of the old notebook is carried into the next one ----

        [Test]
        public async Task Switch_UndoHistoryAndPasswordBackOff_NotCarriedIntoTheNextNotebook()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            PasswordBackOff oldBackOff = editor.ChangePasswordBackOff;
            editor.SelectedNote = editor.VisibleNotes.First(i => i.DisplayTitle == "Bank");
            _dialogs.Confirms.Enqueue(true);
            editor.DeleteNoteCommand.Execute(null);
            Assert.That(editor.UndoCount, Is.EqualTo(1));
            string other = OtherNotebook();
            _dialogs.OpenPaths.Enqueue(other);

            shell.OpenNotebookCommand.Execute(null);

            Assert.That(editor.UndoCount, Is.EqualTo(0), "the old editor's undo-delete history is emptied");
            Assert.That(editor.UndoDeleteCommand.CanExecute(null), Is.False);
            await Unlock(shell, "pw");
            Assert.That(shell.Editor, Is.Not.Null);
            Assert.That(shell.Editor, Is.Not.SameAs(editor));
            Assert.That(shell.Editor.Notebook.FilePath, Is.EqualTo(Path.GetFullPath(other)));
            Assert.That(shell.Editor.UndoCount, Is.EqualTo(0));
            Assert.That(shell.Editor.UndoDeleteCommand.CanExecute(null), Is.False);
            Assert.That(shell.Editor.ChangePasswordBackOff, Is.Not.SameAs(oldBackOff));
            Assert.That(shell.Editor.VisibleNotes.Select(n => n.DisplayTitle), Is.EqualTo(new[] { "Other" }));
            _notebooks.Add(shell.Editor.Notebook);
        }

        // ---- window title ----

        [Test]
        public void Title_EditorShowsFileName_DirtyStar_AfterSaveAs_OtherPagesLockPad()
        {
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            Assert.That(shell.WindowTitle, Is.EqualTo("LockPad"), "unlock");

            Notebook nb = NewNotebook();
            var raised = new List<string>();
            shell.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
            shell.ShowEditor(nb);
            Quiet(shell);
            Assert.That(shell.WindowTitle, Is.EqualTo("nb.kdbx - LockPad"), "file name only, never the folder");
            Assert.That(raised, Does.Contain("WindowTitle"));

            raised.Clear();
            Edit(shell, "edited");
            Assert.That(shell.WindowTitle, Is.EqualTo("*nb.kdbx - LockPad"));
            Assert.That(raised, Does.Contain("WindowTitle"));

            raised.Clear();
            _dialogs.SaveAsPaths.Enqueue(_dir.File("renamed.kdbx"));
            Assert.That(shell.Editor.SaveAsNow().Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(shell.WindowTitle, Is.EqualTo("renamed.kdbx - LockPad"));
            Assert.That(raised, Does.Contain("WindowTitle"));

            // Save As of a clean notebook changes only the name.
            raised.Clear();
            _dialogs.SaveAsPaths.Enqueue(_dir.File("again.kdbx"));
            Assert.That(shell.Editor.SaveAsNow().Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(shell.WindowTitle, Is.EqualTo("again.kdbx - LockPad"));
            Assert.That(raised, Does.Contain("WindowTitle"));

            shell.Lock();
            Assert.That(shell.WindowTitle, Is.EqualTo("LockPad"), "locked");
            shell.ShowCreate();
            Assert.That(shell.WindowTitle, Is.EqualTo("LockPad"), "create");
        }

        [Test]
        public void Title_ObscuredEditor_LockPad()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            Assert.That(shell.WindowTitle, Is.EqualTo("*nb.kdbx - LockPad"));
            var raised = new List<string>();
            shell.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            _dialogs.IsShowingDialog = true;
            Advance(TimeSpan.FromMinutes(30));
            shell.Tick();

            Assert.That(shell.Editor.IsObscured, Is.True);
            Assert.That(shell.WindowTitle, Is.EqualTo("LockPad"), "nothing about the notebook while locking");
            Assert.That(raised, Does.Contain("WindowTitle"));
        }

        [Test]
        public void Title_HeldUnlockPage_LockPad()
        {
            Notebook nb;
            ShellViewModel shell = LockedWithHeldNotebook(out nb);
            Assert.That(shell.WindowTitle, Is.EqualTo("LockPad"));
        }

        // ---- MainWindow: the File menu and the title binding ----

        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        private static string Header(object item)
        {
            if (item is Separator) return "-";
            return (string)((MenuItem)item).Header;
        }

        private static MenuItem Item(Window window, string name)
        {
            var item = window.FindName(name) as MenuItem;
            Assert.That(item, Is.Not.Null, name);
            return item;
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void FileMenu_ItemsOrderGesturesAndAccessKeys()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                Pump();
                var file = (MenuItem)window.FindName("FileMenu");
                Assert.That(file, Is.Not.Null);
                Assert.That(file.Header, Is.EqualTo("_File"));
                object[] items = file.Items.Cast<object>().ToArray();
                Assert.That(items.Select(Header), Is.EqualTo(new[]
                {
                    "_New notebook" + Ellipsis, "_Open notebook" + Ellipsis, "-",
                    "_Save", "Save _As" + Ellipsis, "-",
                    "_Change master password" + Ellipsis, "-",
                    "_Lock", "-",
                    "E_xit",
                }));
                Assert.That(items.OfType<MenuItem>().Select(m => m.InputGestureText ?? ""), Is.EqualTo(new[]
                {
                    "", "Ctrl+O", "Ctrl+S", "Ctrl+Shift+S", "", "Ctrl+L", "",
                }));

                // One access key per item, none shared.
                var keys = items.OfType<MenuItem>().Select(m =>
                {
                    string h = (string)m.Header;
                    int i = h.IndexOf('_');
                    Assert.That(i, Is.GreaterThanOrEqualTo(0), h + " has an access key");
                    Assert.That(h.IndexOf('_', i + 1), Is.LessThan(0), h + " has one access key");
                    return char.ToLowerInvariant(h[i + 1]);
                }).ToArray();
                Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Length), new string(keys));

                // Ctrl+O on the window opens another notebook; the shortcut table lists it under File.
                KeyBinding ctrlO = window.InputBindings.OfType<KeyBinding>()
                    .SingleOrDefault(b => b.Key == Key.O && b.Modifiers == ModifierKeys.Control);
                Assert.That(ctrlO, Is.Not.Null);
                Assert.That(ctrlO.Command, Is.SameAs(shell.OpenNotebookCommand));
                ShortcutEntry entry = ShortcutTable.Entries.Single(e => e.Keys == "Ctrl+O");
                Assert.That(entry.Group, Is.EqualTo("File"));
                Assert.That(entry.Action, Is.EqualTo("Open another notebook"));
                Assert.That(entry.Kind, Is.EqualTo(ShortcutKind.KeyBinding));

                Assert.That(Item(window, "OpenNotebookMenuItem").Command, Is.SameAs(shell.OpenNotebookCommand));
                Assert.That(Item(window, "NewNotebookMenuItem").Command, Is.SameAs(shell.NewNotebookCommand));
                Assert.That(Item(window, "LockMenuItem").Command, Is.SameAs(shell.LockCommand));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>What the user sees when the File menu opens. A MenuItem in a closed submenu does not query its
        /// command (WPF marks it for a re-query on open), so the command's CanExecute is combined with IsEnabled.</summary>
        private static bool ShownEnabled(MenuItem item)
        {
            return item.IsEnabled && (item.Command == null || item.Command.CanExecute(item.CommandParameter));
        }

        private static void AssertEnabled(Window window, string page, bool newOpen, bool editorItems)
        {
            Pump();
            foreach (string name in new[] { "NewNotebookMenuItem", "OpenNotebookMenuItem" })
                Assert.That(ShownEnabled(Item(window, name)), Is.EqualTo(newOpen), page + ": " + name);
            foreach (string name in new[] { "SaveMenuItem", "SaveAsMenuItem", "ChangePasswordMenuItem", "LockMenuItem" })
                Assert.That(ShownEnabled(Item(window, name)), Is.EqualTo(editorItems), page + ": " + name);
            Assert.That(ShownEnabled(Item(window, "ExitMenuItem")), Is.True, page + ": Exit");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void FileMenu_EnabledStatesPerPage()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                AssertEnabled(window, "plain unlock", true, false);

                // Busy plain unlock page (an attempt in flight).
                var unlock = (UnlockViewModel)shell.CurrentView;
                unlock.NotebookPath = OtherNotebook();
                var gate = new TaskCompletionSource<int>();
                unlock.RunInBackground = a => gate.Task;
                using (SecureString pw = UnlockViewModelTests.Secure("pw"))
                {
                    Task attempt = unlock.UnlockAsync(pw);
                    Assert.That(unlock.IsBusy, Is.True);
                    AssertEnabled(window, "busy unlock", false, false);
                    gate.SetResult(0);
                    for (int i = 0; i < 100 && !attempt.IsCompleted; i++) Pump();
                    Assert.That(attempt.IsCompleted, Is.True);
                }
                AssertEnabled(window, "plain unlock again", true, false);

                shell.ShowCreate();
                AssertEnabled(window, "create", false, false);

                Notebook nb = NewNotebook();
                shell.ShowEditor(nb);
                Quiet(shell);
                AssertEnabled(window, "editor", true, true);

                _dialogs.IsShowingDialog = true;
                Advance(TimeSpan.FromMinutes(30));
                shell.Tick();
                _dialogs.IsShowingDialog = false;
                Assert.That(shell.Editor.IsObscured, Is.True);
                Pump();
                Assert.That(ShownEnabled(Item(window, "NewNotebookMenuItem")), Is.False, "obscured: New");
                Assert.That(ShownEnabled(Item(window, "OpenNotebookMenuItem")), Is.False, "obscured: Open");

                shell.Tick(); // locks: clean, so the plain unlock page
                AssertEnabled(window, "unlock after lock", true, false);

                // Reopen, edit, and lock with a failing save: the held unlock page.
                Notebook again = _store.Open(NotebookPath, Pw());
                _notebooks.Add(again);
                shell.ShowEditor(again);
                Quiet(shell);
                Edit(shell, "edited while away");
                MakeFileReadOnly();
                shell.Lock();
                Assert.That(((UnlockViewModel)shell.CurrentView).HasHeldNotebook, Is.True);
                AssertEnabled(window, "held unlock", false, false);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void FileMenu_Lock_LocksLikeCtrlL()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            var window = new MainWindow { DataContext = shell };
            try
            {
                Pump();
                MenuItem item = Item(window, "LockMenuItem");
                item.Command.Execute(item.CommandParameter);
                Assert.That(shell.Editor, Is.Null);
                Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
                AssertDisposed(_store, nb);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void FileMenu_Exit_ClosesThroughTheNormalClosingPath()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            var window = new MainWindow { DataContext = shell };
            bool closed = false;
            window.Closed += (s, e) => closed = true;
            Edit(shell, "edited");
            MakeFileReadOnly();
            Pump();
            MenuItem exit = Item(window, "ExitMenuItem");

            exit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.That(closed, Is.False, "TryClose asked; the choice defaulted to Cancel");
            Assert.That(_dialogs.LastCloseChoiceTitle, Is.EqualTo("Could not save"));
            Assert.That(shell.Editor, Is.Not.Null);

            File.SetAttributes(NotebookPath, FileAttributes.Normal);
            exit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(closed, Is.True);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_TitleFollowsTheShell()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                Pump();
                Assert.That(window.Title, Is.EqualTo("LockPad"));

                Notebook nb = NewNotebook();
                shell.ShowEditor(nb);
                Quiet(shell);
                Pump();
                Assert.That(window.Title, Is.EqualTo("nb.kdbx - LockPad"));

                Edit(shell, "edited");
                Pump();
                Assert.That(window.Title, Is.EqualTo("*nb.kdbx - LockPad"));

                shell.Lock();
                Pump();
                Assert.That(window.Title, Is.EqualTo("LockPad"));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void TitleConstant_IsTheProductName()
        {
            Assert.That(ShellViewModel.AppTitle, Is.EqualTo("LockPad"));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LockPad.App;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using KeePassLib;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// UX round 2, U3 (user decision 2026-10-19): deleted notes can be restored with Undo delete until the notebook is
    /// locked or closed; the delete confirmation says whether the note exists in the last saved file.
    /// </summary>
    [TestFixture]
    public class UndoDeleteTests
    {
        private const string DeletedStatus = "Note deleted. Undo with Ctrl+Z in the notes list.";
        private const string UndoIntro = "? You can bring it back with Undo delete until you lock or close LockPad. ";
        private const string NeverSavedTail = "This note has never been saved, so after that it cannot be recovered.";
        private const string SavedTail = "After you save, its last saved version is kept in the .bak backup file until you "
            + "save again.";

        private TempDir _dir;
        private FakeDialogService _dialogs;
        private FakeClipboardService _clipboard;
        private SettingsStore _settingsStore;
        private AppSettings _settings;
        private NotebookStore _store;
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
            _notebooks = new List<Notebook>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _notebooks) nb.Dispose();
            string path = NotebookPath;
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
            _dir.Dispose();
        }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        private string NotebookPath { get { return _dir.File("nb.kdbx"); } }

        /// <summary>Creates nb.kdbx with the (title, text) pairs, saved, so every note is in the saved file.</summary>
        private Notebook NewNotebook(params string[] titleTextPairs)
        {
            using (new NotebookStore().Create(NotebookPath, Pw(), TestKdf.Fast())) { }
            Notebook nb = _store.Open(NotebookPath, Pw());
            _notebooks.Add(nb);
            for (int i = 0; i + 1 < titleTextPairs.Length; i += 2)
            {
                Note n = nb.AddNote(titleTextPairs[i]);
                nb.UpdateNote(n.Id, titleTextPairs[i], titleTextPairs[i + 1]);
            }
            if (titleTextPairs.Length > 0)
                Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            return nb;
        }

        private Notebook Reopen()
        {
            Notebook nb = new NotebookStore().Open(NotebookPath, Pw());
            _notebooks.Add(nb);
            return nb;
        }

        private EditorViewModel NewVm(Notebook nb)
        {
            var vm = new EditorViewModel(nb, _store, _dialogs, _settingsStore, _settings);
            vm.BeginBusy = () => new Nothing();
            return vm;
        }

        private ShellViewModel NewShell()
        {
            return new ShellViewModel(_store, _settingsStore, _settings, _dialogs, _clipboard, new FakeClock(),
                TestKdf.Fast);
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose() { }
        }

        private static NoteItemViewModel Item(EditorViewModel vm, string title)
        {
            return vm.VisibleNotes.Single(i => i.DisplayTitle == title);
        }

        private void Delete(EditorViewModel vm, string title)
        {
            vm.SelectedNote = Item(vm, title);
            _dialogs.Confirms.Enqueue(true);
            vm.DeleteNoteCommand.Execute(null);
        }

        private static string[] Titles(EditorViewModel vm)
        {
            return vm.VisibleNotes.Select(i => i.DisplayTitle).ToArray();
        }

        /// <summary>True when the file's KeePass DeletedObjects list records the entry <paramref name="id"/>.</summary>
        private bool FileRecordsDeletion(string id)
        {
            var db = new PwDatabase();
            using (Credentials c = Pw())
            {
                try
                {
                    db.Open(IOConnectionInfo.FromPath(NotebookPath), c.CompositeKey, null);
                    return db.DeletedObjects.Any(d => d.Uuid.ToHexString() == id);
                }
                finally
                {
                    db.Close();
                }
            }
        }

        private static void PumpDispatcher()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        // ---- undo ----

        [Test]
        public void DeleteThenUndo_RestoresTitleTextPositionAndSelection_Dirty()
        {
            Notebook nb = NewNotebook("A", "a", "B", "b", "C", "c");
            EditorViewModel vm = NewVm(nb);
            string idB = Item(vm, "B").Id;
            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.False, "nothing to undo yet");

            Delete(vm, "B");

            Assert.That(Titles(vm), Is.EqualTo(new[] { "A", "C" }));
            Assert.That(vm.StatusText, Is.EqualTo(DeletedStatus));
            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.True);

            vm.UndoDeleteCommand.Execute(null);

            Assert.That(Titles(vm), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(nb.Notes.Select(n => n.Title), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("B"));
            Assert.That(vm.SelectedNote.Id, Is.EqualTo(idB), "the same note id");
            Assert.That(vm.EditorText, Is.EqualTo("b"));
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.False);
        }

        [Test]
        public void MultipleDeletes_UndoInReverseOrder()
        {
            Notebook nb = NewNotebook("A", "a", "B", "b", "C", "c", "D", "d");
            EditorViewModel vm = NewVm(nb);
            Delete(vm, "B");
            Delete(vm, "D");
            Delete(vm, "A");
            Assert.That(Titles(vm), Is.EqualTo(new[] { "C" }));

            vm.UndoDeleteCommand.Execute(null);
            Assert.That(Titles(vm), Is.EqualTo(new[] { "A", "C" }));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("A"));

            vm.UndoDeleteCommand.Execute(null);
            Assert.That(Titles(vm), Is.EqualTo(new[] { "A", "C", "D" }));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("D"));

            vm.UndoDeleteCommand.Execute(null);
            Assert.That(Titles(vm), Is.EqualTo(new[] { "A", "B", "C", "D" }));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("B"));
            Assert.That(vm.EditorText, Is.EqualTo("b"));
            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.False);
        }

        [Test]
        public void Undo_WhileTheFilterWouldHideIt_ClearsTheFilterAndSelectsIt()
        {
            EditorViewModel vm = NewVm(NewNotebook("Bank", "x", "Ledger", "y"));
            Delete(vm, "Bank");
            vm.FilterText = "ledger";

            vm.UndoDeleteCommand.Execute(null);

            Assert.That(vm.FilterText, Is.EqualTo(""));
            Assert.That(Titles(vm), Is.EqualTo(new[] { "Bank", "Ledger" }));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("Bank"));
        }

        [Test]
        public void Undo_RaisesNoteRestored_AfterSelectingTheNote()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a", "B", "b"));
            Delete(vm, "A");
            string selectedAtEvent = null;
            vm.NoteRestored += (s, e) => selectedAtEvent = vm.SelectedNote.DisplayTitle;

            vm.UndoDeleteCommand.Execute(null);

            Assert.That(selectedAtEvent, Is.EqualTo("A"));
        }

        [Test]
        public void DeclinedDelete_PushesNothing()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a"));
            vm.DeleteNoteCommand.Execute(null); // Confirm answers No

            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.False);
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
        }

        [Test]
        public void UndoButtonState_FollowsTheStack()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a", "B", "b"));
            int changed = 0;
            vm.UndoDeleteCommand.CanExecuteChanged += (s, e) => changed++;

            Delete(vm, "A");
            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.True);
            Assert.That(changed, Is.GreaterThan(0));

            changed = 0;
            vm.UndoDeleteCommand.Execute(null);
            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.False);
            Assert.That(changed, Is.GreaterThan(0));
        }

        [Test]
        public void StatusMessage_GivesWayToTheNormalStatus_OnTheNextChange()
        {
            Notebook nb = NewNotebook("A", "a", "B", "b");
            EditorViewModel vm = NewVm(nb);
            Delete(vm, "A");
            Assert.That(vm.StatusText, Is.EqualTo(DeletedStatus));
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            vm.EditorText = "typed";

            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
            Assert.That(raised, Does.Contain("StatusText"));

            Delete(vm, "B");
            Assert.That(vm.StatusText, Is.EqualTo(DeletedStatus));
            Assert.That(vm.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
        }

        // ---- delete message ----

        [Test]
        public void DeleteMessage_LoadedNote_IsSaved()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            vm.DeleteNoteCommand.Execute(null);
            Assert.That(_dialogs.LastConfirmTitle, Is.EqualTo("Delete note"));
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo("Delete \"Ledger\"" + UndoIntro + SavedTail));
        }

        [Test]
        public void DeleteMessage_NoteCreatedThisSession_NeverSaved_ThenSaved_IsSaved()
        {
            EditorViewModel vm = NewVm(NewNotebook());
            _dialogs.Prompts.Enqueue("Fresh");
            vm.NewNoteCommand.Execute(null);

            vm.DeleteNoteCommand.Execute(null); // declined
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo("Delete \"Fresh\"" + UndoIntro + NeverSavedTail));

            Assert.That(vm.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));
            vm.DeleteNoteCommand.Execute(null); // declined
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo("Delete \"Fresh\"" + UndoIntro + SavedTail));
        }

        [Test]
        public void DeleteMessage_UntitledNote_UsesTheDisplayTitle()
        {
            EditorViewModel vm = NewVm(NewNotebook("", "x"));
            vm.DeleteNoteCommand.Execute(null);
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo("Delete \"(untitled)\"" + UndoIntro + SavedTail));
        }

        // ---- restore after a save ----

        [Test]
        public void RestoreAfterASave_RoundTripsThroughReopen_NotRecordedAsDeleted()
        {
            Notebook nb = NewNotebook("A", "a", "B", "b");
            EditorViewModel vm = NewVm(nb);
            string idB = Item(vm, "B").Id;
            Delete(vm, "B");
            Assert.That(vm.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(FileRecordsDeletion(idB), Is.True, "the saved deletion is recorded by KeePassLib");
            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.True, "a save keeps the undo stack");

            vm.UndoDeleteCommand.Execute(null);
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(vm.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));

            Notebook reopened = Reopen();
            Assert.That(reopened.Notes.Select(n => n.Title), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(reopened.FindNote(idB), Is.Not.Null, "restored with the same id");
            Assert.That(reopened.FindNote(idB).Text, Is.EqualTo("b"));
            Assert.That(FileRecordsDeletion(idB), Is.False, "a live entry never carries a deletion record");
        }

        [Test]
        public void RestoreAfterAFailedSave_RoundTripsThroughReopen_NotRecordedAsDeleted()
        {
            // A failed save has already removed the entry from the database and recorded the deletion in memory.
            Notebook nb = NewNotebook("A", "a", "B", "b");
            EditorViewModel vm = NewVm(nb);
            string idB = Item(vm, "B").Id;
            Delete(vm, "B");
            File.SetAttributes(NotebookPath, FileAttributes.ReadOnly);
            Assert.That(vm.SaveNow().Status, Is.Not.EqualTo(SaveStatus.Saved));
            File.SetAttributes(NotebookPath, FileAttributes.Normal);

            vm.UndoDeleteCommand.Execute(null);
            Assert.That(vm.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));

            Notebook reopened = Reopen();
            Assert.That(reopened.Notes.Select(n => n.Title), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(reopened.FindNote(idB).Text, Is.EqualTo("b"));
            Assert.That(FileRecordsDeletion(idB), Is.False);
        }

        [Test]
        public void DeleteThenUndoBeforeSaving_SavesNoDeletion()
        {
            Notebook nb = NewNotebook("A", "a", "B", "b");
            EditorViewModel vm = NewVm(nb);
            string idA = Item(vm, "A").Id;
            Delete(vm, "A");
            vm.UndoDeleteCommand.Execute(null);

            Assert.That(vm.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));

            Assert.That(Reopen().Notes.Select(n => n.Title), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(FileRecordsDeletion(idA), Is.False);
        }

        // ---- the stack is cleared when the session ends ----

        [Test]
        public void Detach_ClearsTheStack()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a", "B", "b"));
            Delete(vm, "A");
            Assert.That(vm.UndoCount, Is.EqualTo(1));

            vm.Detach();

            Assert.That(vm.UndoCount, Is.EqualTo(0));
            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.False);
        }

        private EditorViewModel ShowWithOneDeletion(ShellViewModel shell, Notebook nb)
        {
            _notebooks.Remove(nb); // the shell owns it now
            shell.ShowEditor(nb);
            EditorViewModel editor = shell.Editor;
            editor.BeginBusy = () => new Nothing();
            Delete(editor, "A");
            Assert.That(editor.UndoCount, Is.EqualTo(1));
            return editor;
        }

        [Test]
        public void Lock_ClearsTheStack()
        {
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));

            shell.Lock();

            Assert.That(shell.Editor, Is.Null);
            Assert.That(editor.UndoCount, Is.EqualTo(0));
        }

        [Test]
        public void Hold_ClearsTheStack()
        {
            ShellViewModel shell = NewShell();
            Notebook nb = NewNotebook("A", "a", "B", "b");
            EditorViewModel editor = ShowWithOneDeletion(shell, nb);
            File.SetAttributes(NotebookPath, FileAttributes.ReadOnly);

            shell.Lock(); // the save fails: the notebook is held

            Assert.That(shell.Editor, Is.Null);
            Assert.That(nb.IsDirty, Is.True, "held, unsaved");
            Assert.That(editor.UndoCount, Is.EqualTo(0));
            _notebooks.Add(nb);
        }

        [Test]
        public void WindowsLock_ClearsTheStack()
        {
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));

            shell.OnWindowsLocked();

            Assert.That(shell.Editor, Is.Null);
            Assert.That(editor.UndoCount, Is.EqualTo(0));
        }

        [Test]
        public void Suspend_ClearsTheStack_AtOnce()
        {
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));

            shell.OnSuspending(); // obscures now; the lock is queued by the window

            Assert.That(editor.IsObscured, Is.True);
            Assert.That(editor.UndoCount, Is.EqualTo(0));
            Assert.That(editor.UndoDeleteCommand.CanExecute(null), Is.False);
        }

        [Test]
        public void ObscuredEditor_DeleteConfirmedInsideTheDialog_IsNotUndoable()
        {
            ShellViewModel shell = NewShell();
            Notebook nb = NewNotebook("A", "a", "B", "b");
            _notebooks.Remove(nb);
            shell.ShowEditor(nb);
            EditorViewModel editor = shell.Editor;
            _dialogs.OnConfirm = () => shell.OnSuspending(); // sleep while the delete confirmation is open
            _dialogs.Confirms.Enqueue(true);

            editor.DeleteNoteCommand.Execute(null);

            Assert.That(editor.IsObscured, Is.True);
            Assert.That(editor.UndoCount, Is.EqualTo(0), "the lock follows; the deleted text is not kept");
            _dialogs.OnConfirm = null;
            shell.Lock();
        }

        [Test]
        public void Close_ClearsTheStack()
        {
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));

            Assert.That(shell.TryClose(), Is.True);

            Assert.That(editor.UndoCount, Is.EqualTo(0));
        }

        [Test]
        public void OpeningAnotherNotebook_ClearsTheStack()
        {
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));
            Assert.That(editor.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(editor.UndoCount, Is.EqualTo(1));
            string other = _dir.File("other.kdbx");
            using (new NotebookStore().Create(other, Pw(), TestKdf.Fast())) { }

            shell.ShowEditor(new NotebookStore().Open(other, Pw()));

            Assert.That(editor.UndoCount, Is.EqualTo(0));
            Assert.That(shell.Editor.UndoCount, Is.EqualTo(0));
            shell.Lock();
        }

        // ---- red-team UX round 2, L1 / L2 (PoCs B1-B3) ----

        [Test]
        public void SessionEndAllowed_EmptiesTheStackAtOnce()
        {
            // B1: a session end is allowed after the save; the stack must not wait for the window close.
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));

            Assert.That(shell.PrepareForSessionEnd(), Is.True);

            Assert.That(editor.UndoCount, Is.EqualTo(0));
            Assert.That(editor.UndoDeleteCommand.CanExecute(null), Is.False);
            Assert.That(shell.TryClose(), Is.True);
        }

        [Test]
        public void ThrowingCanExecuteListener_Lock_EmptiesTheStack_AndCompletesTheTeardown()
        {
            // B2: clearing the stack can never stop the rest of ClearForLock.
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));
            editor.UndoDeleteCommand.CanExecuteChanged += (s, e) => { throw new InvalidOperationException("listener"); };

            Assert.DoesNotThrow(() => shell.Lock());

            Assert.That(editor.UndoCount, Is.EqualTo(0));
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            Assert.That(editor.SelectedNote, Is.Null, "ClearForLock ran to the end");
            Assert.That(editor.VisibleNotes, Is.Empty);
        }

        [Test]
        public void ThrowingCanExecuteListener_Obscure_StillHidesTheNote_AndEmptiesTheStack()
        {
            // B3: the view is told to hide the note before the stack is cleared, and clearing never throws.
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));
            bool notified = false;
            editor.PropertyChanged += (s, e) => { if (e.PropertyName == "IsObscured") notified = true; };
            editor.UndoDeleteCommand.CanExecuteChanged += (s, e) => { throw new InvalidOperationException("listener"); };
            _dialogs.IsShowingDialog = true;

            Assert.DoesNotThrow(() => shell.OnWindowsLocked());

            Assert.That(editor.IsObscured, Is.True);
            Assert.That(notified, Is.True, "the view hides the note");
            Assert.That(editor.UndoCount, Is.EqualTo(0));

            _dialogs.IsShowingDialog = false;
            Assert.DoesNotThrow(() => shell.Tick()); // the dialog closed: the lock follows
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            Assert.That(editor.SelectedNote, Is.Null);
        }

        [Test]
        public void ThrowingIsObscuredListener_StillEmptiesTheStack()
        {
            ShellViewModel shell = NewShell();
            EditorViewModel editor = ShowWithOneDeletion(shell, NewNotebook("A", "a", "B", "b"));
            editor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "IsObscured") throw new InvalidOperationException("view listener");
            };
            _dialogs.IsShowingDialog = true;

            try { shell.OnWindowsLocked(); }
            catch (InvalidOperationException) { } // a view listener's own failure is not swallowed by Obscure

            Assert.That(editor.IsObscured, Is.True);
            Assert.That(editor.UndoCount, Is.EqualTo(0));
            _dialogs.IsShowingDialog = false;
            shell.Lock();
        }

        // ---- read-only ----

        [Test]
        public void ReadOnlyNotebook_UndoDisabled()
        {
            string p = _dir.File("foreign.kdbx");
            using (Credentials c = Pw())
            {
                var db = new PwDatabase();
                db.New(IOConnectionInfo.FromPath(p), c.CompositeKey);
                db.KdfParameters = TestKdf.Fast();
                db.SaveAs(IOConnectionInfo.FromPath(p), false, null);
                db.Close();
            }
            Notebook nb = new NotebookStore().Open(p, Pw());
            _notebooks.Add(nb);
            Assert.That(nb.IsReadOnly, Is.True);

            EditorViewModel vm = NewVm(nb);

            Assert.That(vm.UndoDeleteCommand.CanExecute(null), Is.False);
            Assert.DoesNotThrow(() => vm.UndoDeleteCommand.Execute(null));
        }

        // ---- view ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EditorView_UndoDeleteButton_NextToNewRenameDelete()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            EditorViewModel vm = NewVm(NewNotebook("A", "a"));
            var view = new EditorView { DataContext = vm };
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            PumpDispatcher();

            var button = view.FindName("UndoDeleteButton") as Button;
            Assert.That(button, Is.Not.Null, "a button named UndoDeleteButton");
            Assert.That(button.Content, Is.EqualTo("Undo delete"));
            Assert.That(button.Command, Is.SameAs(vm.UndoDeleteCommand));
            var row = button.Parent as Panel;
            Assert.That(row, Is.Not.Null);
            Assert.That(row.Children.OfType<Button>().Select(b => b.Content as string),
                Is.EqualTo(new[] { "New", "Rename", "Delete", "Undo delete" }));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CtrlZ_RestoresOnlyFromTheNotesList()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                Notebook nb = NewNotebook("A", "a");
                _notebooks.Remove(nb);
                shell.ShowEditor(nb);
                EditorViewModel vm = shell.Editor;
                var view = new EditorView { DataContext = vm };
                view.Measure(new Size(900, 600));
                view.Arrange(new Rect(0, 0, 900, 600));
                PumpDispatcher();

                var list = (ListBox)view.FindName("NotesList");
                KeyBinding ctrlZ = list.InputBindings.OfType<KeyBinding>()
                    .SingleOrDefault(b => b.Key == Key.Z && b.Modifiers == ModifierKeys.Control);
                Assert.That(ctrlZ, Is.Not.Null, "Ctrl+Z on the notes list");
                Assert.That(ctrlZ.Command, Is.SameAs(vm.UndoDeleteCommand));

                // The editor and filter boxes keep their own Ctrl+Z (TextBox undo): no binding there, none above them.
                foreach (string name in new[] { "Editor", "FilterBox" })
                {
                    var box = view.FindName(name) as TextBox;
                    Assert.That(box, Is.Not.Null, name);
                    Assert.That(box.InputBindings.OfType<KeyBinding>().Any(b => b.Key == Key.Z), Is.False, name);
                }
                Assert.That(view.InputBindings.Count, Is.EqualTo(0));
                Assert.That(window.InputBindings.OfType<KeyBinding>()
                    .Any(b => b.Key == Key.Z && b.Modifiers == ModifierKeys.Control), Is.False, "no window-wide Ctrl+Z");
                shell.Lock();
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EditorView_AfterUndo_FocusStaysInTheList()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            EditorViewModel vm = NewVm(NewNotebook("A", "a", "B", "b"));
            var view = new EditorView { DataContext = vm };
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            PumpDispatcher();
            var focused = new List<object>();
            view.FocusElement = el => focused.Add(el);
            Delete(vm, "A");
            PumpDispatcher();
            focused.Clear();

            vm.UndoDeleteCommand.Execute(null);
            PumpDispatcher();

            Assert.That(focused.Count, Is.EqualTo(1));
            var list = (ListBox)view.FindName("NotesList");
            var item = focused[0] as ListBoxItem;
            if (item != null) Assert.That(item.Content, Is.SameAs(vm.SelectedNote));
            else Assert.That(focused[0], Is.SameAs(list));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("A"));
        }
    }
}

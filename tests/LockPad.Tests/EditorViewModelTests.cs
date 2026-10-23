using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LockPad.App;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class EditorViewModelTests
    {
        private const string KeepsChangingMessage = "The file keeps changing on disk. Use Save As to keep your changes.";

        private TempDir _dir;
        private FakeDialogService _dialogs;
        private FakeClipboardService _clipboard;
        private SettingsStore _settingsStore;
        private AppSettings _settings;
        private NotebookStore _store;
        private List<Notebook> _notebooks;
        private int _busyBegun, _busyEnded;

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
            _busyBegun = _busyEnded = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _notebooks) nb.Dispose();
            _dir.Dispose();
        }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        private string NotebookPath { get { return _dir.File("nb.kdbx"); } }

        /// <summary>Creates nb.kdbx, opens it and adds (title, text) pairs. With notes, saves so it starts clean.</summary>
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

        private Notebook Reopen(string path)
        {
            Notebook nb = new NotebookStore().Open(path, Pw());
            _notebooks.Add(nb);
            return nb;
        }

        private EditorViewModel NewVm(Notebook nb)
        {
            var vm = new EditorViewModel(nb, _store, _dialogs, _settingsStore, _settings);
            vm.BeginBusy = () => { _busyBegun++; return new ActionDisposable(() => _busyEnded++); };
            return vm;
        }

        private static NoteItemViewModel Item(EditorViewModel vm, string title)
        {
            return vm.VisibleNotes.Single(i => i.DisplayTitle == title);
        }

        /// <summary>Saves the same file from a second session so the editor's copy is out of date.</summary>
        private void ChangeExternally(string path)
        {
            var other = new NotebookStore();
            using (Notebook nb = other.Open(path, Pw()))
            {
                nb.AddNote("from elsewhere");
                Assert.That(other.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
        }

        private int Count(string call)
        {
            return _dialogs.Calls.Count(c => c == call);
        }

        private sealed class ActionDisposable : IDisposable
        {
            private readonly Action _onDispose;
            public ActionDisposable(Action onDispose) { _onDispose = onDispose; }
            public void Dispose() { _onDispose(); }
        }

        // ---- notes list ----

        [Test]
        public void NewNote_AddsSelectsAndMarksDirty()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
            int before = vm.VisibleNotes.Count;
            _dialogs.Prompts.Enqueue("New note"); // the prefilled name accepted with Enter

            vm.NewNoteCommand.Execute(null);

            Assert.That(vm.VisibleNotes.Count, Is.EqualTo(before + 1));
            Assert.That(vm.SelectedNote, Is.Not.Null);
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("New note"));
            Assert.That(nb.FindNote(vm.SelectedNote.Id).Title, Is.EqualTo("New note"));
            Assert.That(vm.EditorText, Is.EqualTo(""));
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
        }

        // ---- UX round 2, U2: New note asks for the name first ----

        [Test]
        public void NewNote_AsksForTheName_Ok_CreatesItWithTheTypedTitle_SelectedAndDirty()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            _dialogs.Prompts.Enqueue("Cold wallet");

            vm.NewNoteCommand.Execute(null);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "Prompt" }));
            Assert.That(_dialogs.LastPromptTitle, Is.EqualTo("New note"));
            Assert.That(_dialogs.LastPromptLabel, Is.EqualTo("Name of the new note:"));
            Assert.That(_dialogs.LastPromptInitial, Is.EqualTo("New note"));
            Assert.That(nb.Notes.Select(n => n.Title), Is.EqualTo(new[] { "Ledger", "Cold wallet" }));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("Cold wallet"));
            Assert.That(nb.FindNote(vm.SelectedNote.Id).Title, Is.EqualTo("Cold wallet"));
            Assert.That(vm.EditorText, Is.EqualTo(""));
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
        }

        [Test]
        public void NewNote_PromptCancelled_CreatesNothing_NotDirty()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            NoteItemViewModel selected = vm.SelectedNote;
            int created = 0;
            vm.NoteCreated += (s, e) => created++;

            vm.NewNoteCommand.Execute(null); // no answer queued -> Prompt returns null (Cancel, Esc, closed)

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "Prompt" }));
            Assert.That(nb.Notes.Select(n => n.Title), Is.EqualTo(new[] { "Ledger" }));
            Assert.That(vm.VisibleNotes.Count, Is.EqualTo(1));
            Assert.That(vm.SelectedNote, Is.SameAs(selected));
            Assert.That(nb.IsDirty, Is.False);
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
            Assert.That(created, Is.EqualTo(0), "focus is not moved");
        }

        [Test]
        public void NewNote_EmptyTitle_IsAllowed_DisplaysUntitled()
        {
            Notebook nb = NewNotebook();
            EditorViewModel vm = NewVm(nb);
            _dialogs.Prompts.Enqueue("");

            vm.NewNoteCommand.Execute(null);

            Assert.That(nb.Notes.Single().Title, Is.EqualTo(""));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("(untitled)"));
            Assert.That(nb.IsDirty, Is.True);
        }

        [Test]
        public void NewNote_TitleFollowsTheNormalTitleRules_SingleLine()
        {
            Notebook nb = NewNotebook();
            EditorViewModel vm = NewVm(nb);
            _dialogs.Prompts.Enqueue("two\r\nlines");

            vm.NewNoteCommand.Execute(null);

            Assert.That(nb.Notes.Single().Title, Is.EqualTo("two lines"));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("two lines"));
        }

        [Test]
        public void NewNote_Ok_RaisesNoteCreated_AfterTheNoteIsSelected()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            string selectedWhenRaised = null;
            int created = 0;
            vm.NoteCreated += (s, e) => { created++; selectedWhenRaised = vm.SelectedNote.DisplayTitle; };
            _dialogs.Prompts.Enqueue("Seed");

            vm.NewNoteCommand.Execute(null);

            Assert.That(created, Is.EqualTo(1));
            Assert.That(selectedWhenRaised, Is.EqualTo("Seed"));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EditorView_NoteCreated_MovesFocusIntoTheEditorTextBox()
        {
            // Real keyboard focus needs a shown window (manual checklist); the view's focus call goes through a seam.
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            var view = new EditorView { DataContext = vm };
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            PumpDispatcher();
            var focused = new List<object>();
            view.FocusElement = el => focused.Add(el);
            _dialogs.Prompts.Enqueue("Seed");

            vm.NewNoteCommand.Execute(null);
            PumpDispatcher();

            Assert.That(focused, Is.EqualTo(new object[] { view.FindName("Editor") }));
            Assert.That(((UIElement)view.FindName("Editor")).IsEnabled, Is.True, "the new note is selected");

            focused.Clear();
            vm.NewNoteCommand.Execute(null); // cancelled
            PumpDispatcher();
            Assert.That(focused, Is.Empty);
        }

        [Test]
        public void NewNote_WhileFilterWouldHideIt_ClearsFilterAndSelectsIt()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x", "Bank", "y"));
            vm.FilterText = "bank";
            _dialogs.Prompts.Enqueue("New note");

            vm.NewNoteCommand.Execute(null);

            Assert.That(vm.FilterText, Is.EqualTo(""));
            Assert.That(vm.VisibleNotes.Count, Is.EqualTo(3));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("New note"));
        }

        [Test]
        public void Open_SelectsFirstNote()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "ledger text", "Bank", "bank text"));
            Assert.That(vm.SelectedNote.DisplayTitle, Is.EqualTo("Ledger"));
            Assert.That(vm.EditorText, Is.EqualTo("ledger text"));
            Assert.That(vm.HasSelection, Is.True);
        }

        [Test]
        public void EditingText_UpdatesNotebook()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            string id = vm.SelectedNote.Id;

            vm.EditorText = "abc";

            Assert.That(nb.FindNote(id).Text, Is.EqualTo("abc"));
            Assert.That(nb.FindNote(id).Title, Is.EqualTo("Ledger"));
            Assert.That(vm.EditorText, Is.EqualTo("abc"));
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
        }

        [Test]
        public void SelectingNote_ShowsItsText()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "ledger text", "Bank", "bank text"));
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            vm.SelectedNote = Item(vm, "Bank");

            Assert.That(vm.EditorText, Is.EqualTo("bank text"));
            Assert.That(raised, Does.Contain("EditorText"));
        }

        [Test]
        public void Filter_MatchesTitleOrTextIgnoringCase()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x", "Bank", "seed WORDS"));

            vm.FilterText = "words";
            Assert.That(vm.VisibleNotes.Select(i => i.DisplayTitle), Is.EqualTo(new[] { "Bank" }));

            vm.FilterText = "LEDGER";
            Assert.That(vm.VisibleNotes.Select(i => i.DisplayTitle), Is.EqualTo(new[] { "Ledger" }));

            vm.FilterText = "";
            Assert.That(vm.VisibleNotes.Select(i => i.DisplayTitle), Is.EqualTo(new[] { "Ledger", "Bank" }));
        }

        [Test]
        public void Filter_HidingSelected_ClearsSelection_NoEdit()
        {
            Notebook nb = NewNotebook("Ledger", "ledger text", "Bank", "bank text");
            EditorViewModel vm = NewVm(nb);
            vm.SelectedNote = Item(vm, "Ledger");

            vm.FilterText = "bank";

            Assert.That(vm.SelectedNote, Is.Null);
            Assert.That(vm.HasSelection, Is.False);
            Assert.That(vm.EditorText, Is.EqualTo(""));

            vm.EditorText = "typed with nothing selected";

            Assert.That(nb.Notes.Select(n => n.Text), Is.EqualTo(new[] { "ledger text", "bank text" }));
            Assert.That(nb.IsDirty, Is.False);
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
        }

        [Test]
        public void Filter_KeepsSelectionWhileStillVisible()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x", "Bank", "y"));
            NoteItemViewModel bank = Item(vm, "Bank");
            vm.SelectedNote = bank;

            vm.FilterText = "an";

            Assert.That(vm.SelectedNote, Is.SameAs(bank));
            Assert.That(vm.EditorText, Is.EqualTo("y"));
        }

        [Test]
        public void NoteItem_EmptyTitle_DisplaysUntitled()
        {
            Assert.That(new NoteItemViewModel("1", "").DisplayTitle, Is.EqualTo("(untitled)"));
            Assert.That(new NoteItemViewModel("2", "   ").DisplayTitle, Is.EqualTo("(untitled)"));
            Assert.That(new NoteItemViewModel("3", null).DisplayTitle, Is.EqualTo("(untitled)"));
            Assert.That(new NoteItemViewModel("4", "Seed").DisplayTitle, Is.EqualTo("Seed"));
            Assert.That(new NoteItemViewModel("4", "Seed").Id, Is.EqualTo("4"));

            EditorViewModel vm = NewVm(NewNotebook("", "text"));
            Assert.That(vm.VisibleNotes.Single().DisplayTitle, Is.EqualTo("(untitled)"));
        }

        [Test]
        public void Rename_UpdatesTitleAndList()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            NoteItemViewModel item = vm.SelectedNote;
            var raised = new List<string>();
            item.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
            _dialogs.Prompts.Enqueue("Cold wallet");

            vm.RenameNoteCommand.Execute(null);

            Assert.That(_dialogs.LastPromptTitle, Is.EqualTo("Rename note"));
            Assert.That(_dialogs.LastPromptLabel, Is.EqualTo("Title:"));
            Assert.That(_dialogs.LastPromptInitial, Is.EqualTo("Ledger"));
            Assert.That(nb.FindNote(item.Id).Title, Is.EqualTo("Cold wallet"));
            Assert.That(nb.FindNote(item.Id).Text, Is.EqualTo("x"));
            Assert.That(item.DisplayTitle, Is.EqualTo("Cold wallet"));
            Assert.That(raised, Does.Contain("DisplayTitle"));
            Assert.That(vm.SelectedNote, Is.SameAs(item));
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
        }

        /// <summary>Re-attack 2026-10-22 (L1): an answer given after the editor was obscured (the lock became due while
        /// the dialog was open) may not be the user's, as for Change master password: nothing changes.</summary>
        [Test]
        public void Delete_ConfirmedAfterObscure_DeletesNothing()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            _dialogs.Confirms.Enqueue(true);
            _dialogs.OnConfirm = vm.Obscure; // idle expiry while the confirmation is open

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(nb.Notes.Count, Is.EqualTo(1));
            Assert.That(nb.IsDirty, Is.False);
        }

        [Test]
        public void NewNote_AnsweredAfterObscure_AddsNothing()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            _dialogs.Prompts.Enqueue("planted");
            _dialogs.OnPrompt = vm.Obscure; // idle expiry while the prompt is open

            vm.NewNoteCommand.Execute(null);

            Assert.That(nb.Notes.Count, Is.EqualTo(1));
            Assert.That(nb.IsDirty, Is.False);
        }

        [Test]
        public void Rename_AnsweredAfterObscure_ChangesNothing()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            _dialogs.Prompts.Enqueue("Cold wallet");
            _dialogs.OnPrompt = vm.Obscure;

            vm.RenameNoteCommand.Execute(null);

            Assert.That(nb.Notes.Single().Title, Is.EqualTo("Ledger"));
            Assert.That(nb.IsDirty, Is.False);
        }

        [Test]
        public void Rename_Cancelled_NoChange()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);

            vm.RenameNoteCommand.Execute(null); // no answer queued -> Prompt returns null

            Assert.That(nb.Notes.Single().Title, Is.EqualTo("Ledger"));
            Assert.That(nb.IsDirty, Is.False);
        }

        [Test]
        public void Delete_Declined_KeepsNote()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            _dialogs.Confirms.Enqueue(false);

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(_dialogs.LastConfirmTitle, Is.EqualTo("Delete note"));
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo(
                "Delete \"Ledger\"? You can bring it back with Undo delete until you lock or close LockPad. After you save, "
                + "its last saved version is kept in the .bak backup file until you save again."));
            Assert.That(nb.Notes.Count, Is.EqualTo(1));
            Assert.That(vm.VisibleNotes.Count, Is.EqualTo(1));
            Assert.That(vm.SelectedNote, Is.Not.Null);
            Assert.That(nb.IsDirty, Is.False);
        }

        [Test]
        public void Delete_Confirmed_RemovesNote()
        {
            Notebook nb = NewNotebook("Ledger", "x", "Bank", "y");
            EditorViewModel vm = NewVm(nb);
            vm.SelectedNote = Item(vm, "Ledger");
            _dialogs.Confirms.Enqueue(true);

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(nb.Notes.Select(n => n.Title), Is.EqualTo(new[] { "Bank" }));
            Assert.That(vm.VisibleNotes.Select(i => i.DisplayTitle), Is.EqualTo(new[] { "Bank" }));
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("Note deleted. Undo with Ctrl+Z in the notes list."));
        }

        [Test]
        public void Delete_Middle_SelectsNext()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a", "B", "b", "C", "c"));
            vm.SelectedNote = Item(vm, "B");
            _dialogs.Confirms.Enqueue(true);

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(vm.VisibleNotes.Select(i => i.DisplayTitle), Is.EqualTo(new[] { "A", "C" }));
            Assert.That(vm.SelectedNote, Is.SameAs(Item(vm, "C")));
            Assert.That(vm.EditorText, Is.EqualTo("c"));
        }

        [Test]
        public void Delete_Last_SelectsPrevious()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a", "B", "b", "C", "c"));
            vm.SelectedNote = Item(vm, "C");
            _dialogs.Confirms.Enqueue(true);

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(vm.SelectedNote, Is.SameAs(Item(vm, "B")));
            Assert.That(vm.EditorText, Is.EqualTo("b"));
        }

        [Test]
        public void Delete_Only_LeavesNoSelection()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a"));
            _dialogs.Confirms.Enqueue(true);

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(vm.VisibleNotes, Is.Empty);
            Assert.That(vm.SelectedNote, Is.Null);
            Assert.That(vm.EditorText, Is.EqualTo(""));
            Assert.That(vm.HasSelection, Is.False);
        }

        [Test]
        public void Delete_WithFilter_SelectsNextVisibleNote()
        {
            EditorViewModel vm = NewVm(NewNotebook("Bank A", "a", "Ledger", "l", "Bank B", "b"));
            vm.FilterText = "bank";
            vm.SelectedNote = Item(vm, "Bank A");
            _dialogs.Confirms.Enqueue(true);

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(vm.SelectedNote, Is.SameAs(Item(vm, "Bank B")));
        }

        [Test]
        public void Delete_UntitledNote_ConfirmUsesDisplayTitle()
        {
            EditorViewModel vm = NewVm(NewNotebook("", "x"));
            vm.DeleteNoteCommand.Execute(null);
            Assert.That(_dialogs.LastConfirmMessage, Does.StartWith("Delete \"(untitled)\"?"));
        }

        [Test]
        public void RenameAndDelete_DisabledWithoutSelection()
        {
            EditorViewModel vm = NewVm(NewNotebook());
            Assert.That(vm.SelectedNote, Is.Null);
            Assert.That(vm.RenameNoteCommand.CanExecute(null), Is.False);
            Assert.That(vm.DeleteNoteCommand.CanExecute(null), Is.False);
            Assert.That(vm.NewNoteCommand.CanExecute(null), Is.True);

            int changed = 0;
            vm.DeleteNoteCommand.CanExecuteChanged += (s, e) => changed++;
            _dialogs.Prompts.Enqueue("New note");
            vm.NewNoteCommand.Execute(null);
            Assert.That(vm.DeleteNoteCommand.CanExecute(null), Is.True);
            Assert.That(changed, Is.GreaterThan(0));
        }

        // ---- save ----

        [Test]
        public void Save_Success_StatusSaved()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "edited";
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));

            SaveResult r = vm.SaveNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(_busyBegun, Is.EqualTo(1));
            Assert.That(_busyEnded, Is.EqualTo(1));
            Assert.That(Reopen(NotebookPath).Notes.Single().Text, Is.EqualTo("edited"));
        }

        [Test]
        public void SaveCommand_Saves()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            vm.EditorText = "edited";
            vm.SaveCommand.Execute(null);
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
        }

        [Test]
        public void Save_ExternalChange_Overwrite_Saves()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "our edit";
            ChangeExternally(NotebookPath);
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.Overwrite);

            SaveResult r = vm.SaveNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(Count("AskExternalChange"), Is.EqualTo(1));
            Assert.That(Count("ShowError"), Is.EqualTo(0));
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
            Notebook reopened = Reopen(NotebookPath);
            Assert.That(reopened.Notes.Select(n => n.Text), Is.EqualTo(new[] { "our edit" }));
        }

        [Test]
        public void Save_ExternalChange_Cancel_StaysDirty()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            vm.EditorText = "our edit";
            ChangeExternally(NotebookPath);

            SaveResult r = vm.SaveNow(); // AskExternalChange answers Cancel by default

            Assert.That(r.Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
            Assert.That(Count("AskExternalChange"), Is.EqualTo(1));
            Assert.That(Count("ShowError"), Is.EqualTo(0));
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
            Assert.That(_busyEnded, Is.EqualTo(_busyBegun));
        }

        [Test]
        public void Save_ExternalChange_SaveAs_SavesToNewPath()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "our edit";
            ChangeExternally(NotebookPath);
            string newPath = _dir.File("copy.kdbx");
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.SaveAs);
            _dialogs.SaveAsPaths.Enqueue(newPath);

            SaveResult r = vm.SaveNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(newPath)));
            Assert.That(Reopen(newPath).Notes.Single().Text, Is.EqualTo("our edit"));
            Assert.That(Reopen(NotebookPath).Notes.Count, Is.EqualTo(2), "the externally changed file is untouched");
        }

        // ---- Fix round 2, B (L3): every Save As route refuses backup-copy names ----

        private const string BackupNameMessage = "This name is reserved for backup copies. Choose another name.";

        [Test]
        public void SaveAs_BackupCopyName_Refused_ShowsError_NothingWritten()
        {
            // The route behind File > Save As and Ctrl+Shift+S (both bound to SaveAsCommand -> SaveAsNow).
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "our edit";
            string target = _dir.File("copy.kdbx.bak"); // a single-backup name (V7)
            _dialogs.SaveAsPaths.Enqueue(target);

            vm.SaveAsCommand.Execute(null);

            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Save failed"));
            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo(BackupNameMessage));
            Assert.That(File.Exists(target), Is.False);
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(NotebookPath)));
        }

        [Test]
        public void Save_ExternalChange_SaveAsToBackupCopyName_Refused()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "our edit";
            ChangeExternally(NotebookPath);
            string target = _dir.File("other.kdbx.bak1");
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.SaveAs);
            _dialogs.SaveAsPaths.Enqueue(target);

            SaveResult r = vm.SaveNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
            Assert.That(r.Message, Is.EqualTo(BackupNameMessage));
            Assert.That(_dialogs.LastErrorMessage, Does.StartWith(BackupNameMessage));
            Assert.That(File.Exists(target), Is.False);
            Assert.That(nb.IsDirty, Is.True);
        }

        [Test]
        public void Save_ExternalChange_SaveAsPickerCancelled_IsCancel()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            vm.EditorText = "our edit";
            ChangeExternally(NotebookPath);
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.SaveAs);

            SaveResult r = vm.SaveNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
            Assert.That(Count("PickSaveAsPath"), Is.EqualTo(1));
            Assert.That(Count("ShowError"), Is.EqualTo(0));
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
        }

        [Test]
        public void Save_ExternalChange_FileChangesAgainDuringOverwrite_AsksAgain_ThenSaves()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "our edit";
            byte[] versionA = File.ReadAllBytes(NotebookPath);
            ChangeExternally(NotebookPath); // now version B on disk
            int hookRuns = 0;
            _store.TestHook = (stage, temp) =>
            {
                // During the first overwrite only: the file changes again after it was checked.
                if (stage == "before-replace" && hookRuns++ == 0) File.WriteAllBytes(NotebookPath, versionA);
            };
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.Overwrite);
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.Overwrite);

            SaveResult r = vm.SaveNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(Count("AskExternalChange"), Is.EqualTo(2));
            Assert.That(Count("ShowError"), Is.EqualTo(0));
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
            _store.TestHook = null;
            Assert.That(Reopen(NotebookPath).Notes.Select(n => n.Text), Is.EqualTo(new[] { "our edit" }));
        }

        [Test]
        public void Save_ExternalChange_FileKeepsChanging_StopsAfterFiveAsks()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "our edit";
            byte[] versionA = File.ReadAllBytes(NotebookPath);
            ChangeExternally(NotebookPath);
            byte[] versionB = File.ReadAllBytes(NotebookPath);
            _store.TestHook = (stage, temp) =>
            {
                if (stage != "before-replace") return;
                byte[] current = File.ReadAllBytes(NotebookPath);
                File.WriteAllBytes(NotebookPath, current.SequenceEqual(versionA) ? versionB : versionA);
            };
            for (int i = 0; i < 8; i++) _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.Overwrite);

            SaveResult r = vm.SaveNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.ExternalChangeDetected));
            Assert.That(Count("AskExternalChange"), Is.EqualTo(5));
            Assert.That(Count("ShowError"), Is.EqualTo(1));
            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Save failed"));
            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo(KeepsChangingMessage));
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
            Assert.That(nb.FindNote(vm.SelectedNote.Id).Text, Is.EqualTo("our edit"));
        }

        [Test]
        public void Save_Failure_ShowsError()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            vm.EditorText = "our edit";
            File.SetAttributes(NotebookPath, File.GetAttributes(NotebookPath) | FileAttributes.ReadOnly);

            SaveResult r = vm.SaveNow();

            Assert.That(r.Status, Is.Not.EqualTo(SaveStatus.Saved));
            Assert.That(_dialogs.Calls, Does.Contain("ShowError"));
            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Save failed"));
            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo(
                r.Message + " Use File > Save As (Ctrl+Shift+S) to save a copy elsewhere."), "RT-M3: names the way out");
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
            Assert.That(_busyEnded, Is.EqualTo(_busyBegun));
        }

        [Test]
        public void SaveAs_NewPath_Saves_FilePathChanges()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "our edit";
            string newPath = _dir.File("other.kdbx");
            _dialogs.SaveAsPaths.Enqueue(newPath);

            SaveResult r = vm.SaveAsNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(newPath)));
            Assert.That(vm.StatusText, Is.EqualTo("Saved"));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickSaveAsPath" }));
            Assert.That(_settings.LastNotebookPath, Is.EqualTo(Path.GetFullPath(newPath)));
            Assert.That(_busyBegun, Is.EqualTo(1));
            Assert.That(_busyEnded, Is.EqualTo(1));
            Assert.That(Reopen(newPath).Notes.Single().Text, Is.EqualTo("our edit"));
        }

        [Test]
        public void SaveAs_Cancel_NoError()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            vm.EditorText = "our edit";

            SaveResult r = vm.SaveAsNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.Cancelled));
            Assert.That(Count("ShowError"), Is.EqualTo(0));
            Assert.That(nb.FilePath, Is.EqualTo(Path.GetFullPath(NotebookPath)));
            Assert.That(vm.StatusText, Is.EqualTo("Unsaved changes"));
        }

        [Test]
        public void SaveAs_ExistingFile_ShowsError()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            vm.EditorText = "our edit";
            string existing = _dir.File("taken.kdbx");
            File.WriteAllText(existing, "x");
            _dialogs.SaveAsPaths.Enqueue(existing);

            SaveResult r = vm.SaveAsNow();

            Assert.That(r.Status, Is.EqualTo(SaveStatus.IoError));
            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Save failed"));
            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo("File already exists."));
        }

        [Test]
        public void StatusHandler_Throwing_DoesNotBreakSave()
        {
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            vm.EditorText = "our edit";
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "StatusText") throw new InvalidOperationException("listener failure");
            };

            SaveResult r = null;
            Assert.DoesNotThrow(() => r = vm.SaveNow());
            Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void Detach_StopsListeningToNotebook()
        {
            Notebook nb = NewNotebook("Ledger", "x");
            EditorViewModel vm = NewVm(nb);
            int statusChanges = 0;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "StatusText") statusChanges++; };

            vm.Detach();
            nb.AddNote("after detach");

            Assert.That(statusChanges, Is.EqualTo(0));
        }

        // ---- read-only ----

        [Test]
        public void ReadOnly_CommandsDisabled()
        {
            string p = _dir.File("foreign.kdbx");
            using (Credentials c = Pw())
            {
                var db = new PwDatabase();
                db.New(IOConnectionInfo.FromPath(p), c.CompositeKey);
                db.KdfParameters = TestKdf.Fast();
                var e = new PwEntry(true, true);
                e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "t"));
                e.Strings.Set(PwDefs.NotesField, new ProtectedString(true, "note text"));
                db.RootGroup.AddEntry(e, true);
                db.SaveAs(IOConnectionInfo.FromPath(p), false, null);
                db.Close();
            }
            Notebook nb = Reopen(p);
            Assert.That(nb.IsReadOnly, Is.True);
            EditorViewModel vm = NewVm(nb);

            Assert.That(vm.IsReadOnly, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("Read-only"));
            Assert.That(vm.NewNoteCommand.CanExecute(null), Is.False);
            Assert.That(vm.RenameNoteCommand.CanExecute(null), Is.False);
            Assert.That(vm.DeleteNoteCommand.CanExecute(null), Is.False);
            Assert.That(vm.SaveCommand.CanExecute(null), Is.False);
            Assert.That(vm.SaveAsCommand.CanExecute(null), Is.False);
            Assert.That(vm.ZoomInCommand.CanExecute(null), Is.True);

            // Reading still works (copying is the view's, see TextBoxClipboardGuardTests); typing changes nothing.
            Assert.That(vm.EditorText, Is.EqualTo("note text"));
            vm.EditorText = "attempted edit";
            Assert.That(nb.Notes.Single().Text, Is.EqualTo("note text"));
        }

        // ---- view settings ----

        [Test]
        public void Zoom_ClampsAndPersists()
        {
            EditorViewModel vm = NewVm(NewNotebook());
            AppSettings shared = _settings;
            Assert.That(vm.FontSize, Is.EqualTo(14));

            for (int i = 0; i < 50; i++) vm.ZoomInCommand.Execute(null);
            Assert.That(vm.FontSize, Is.EqualTo(48));
            Assert.That(_settings, Is.SameAs(shared));
            Assert.That(_settings.FontSize, Is.EqualTo(48));
            Assert.That(_settingsStore.Load().FontSize, Is.EqualTo(48));

            vm.ZoomResetCommand.Execute(null);
            Assert.That(vm.FontSize, Is.EqualTo(14));
            Assert.That(_settingsStore.Load().FontSize, Is.EqualTo(14));

            for (int i = 0; i < 50; i++) vm.ZoomOutCommand.Execute(null);
            Assert.That(vm.FontSize, Is.EqualTo(8));
            Assert.That(_settingsStore.Load().FontSize, Is.EqualTo(8));

            vm.ZoomInCommand.Execute(null);
            Assert.That(vm.FontSize, Is.EqualTo(9));
        }

        [Test]
        public void WordWrap_TogglePersists()
        {
            EditorViewModel vm = NewVm(NewNotebook());
            Assert.That(vm.WordWrap, Is.True);
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            vm.ToggleWordWrapCommand.Execute(null);

            Assert.That(vm.WordWrap, Is.False);
            Assert.That(_settings.WordWrap, Is.False);
            Assert.That(_settingsStore.Load().WordWrap, Is.False);
            Assert.That(raised, Does.Contain("WordWrap"));

            vm.ToggleWordWrapCommand.Execute(null);
            Assert.That(_settingsStore.Load().WordWrap, Is.True);
        }

        // ---- find ----

        [TestCase("abc ABC abc", "abc", 0, 0)]
        [TestCase("abc ABC abc", "abc", 1, 4)]
        [TestCase("abc ABC abc", "abc", 5, 8)]
        [TestCase("abc ABC abc", "abc", 9, 0)]   // wraps around
        [TestCase("abc ABC abc", "abc", 11, 0)]  // from the end
        [TestCase("abc", "x", 0, -1)]
        [TestCase("abc", "", 0, -1)]
        [TestCase("", "a", 0, -1)]
        [TestCase("SEED words", "Words", 0, 5)]
        public void FindNext_CaseInsensitive_WrapsAround(string text, string query, int from, int expected)
        {
            Assert.That(EditorViewModel.FindNext(text, query, from), Is.EqualTo(expected));
        }

        // ---- shell ----

        [Test]
        public void Shell_ShowEditor_CreatesEditor_LeavingDetachesIt()
        {
            var shell = new ShellViewModel(_store, _settingsStore, _settings, _dialogs, _clipboard, new FakeClock(),
                TestKdf.Fast);
            Assert.That(shell.Editor, Is.Null);
            Notebook nb = NewNotebook("Ledger", "x");

            shell.ShowEditor(nb);

            EditorViewModel editor = shell.Editor;
            Assert.That(editor, Is.Not.Null);
            Assert.That(editor.Notebook, Is.SameAs(nb));
            Assert.That(shell.CurrentView, Is.SameAs(editor));

            int statusChanges = 0;
            editor.PropertyChanged += (s, e) => { if (e.PropertyName == "StatusText") statusChanges++; };
            shell.ShowUnlock(nb, "held");
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            nb.AddNote("after leaving");
            Assert.That(statusChanges, Is.EqualTo(0), "the old editor no longer listens to the notebook");
            _notebooks.Remove(nb);
            shell.ShowEditor(nb); // same notebook again: not disposed
            Assert.That(nb.FindNote(nb.Notes[0].Id), Is.Not.Null);
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

            // Opening another notebook disposes the previous one.
            string other = _dir.File("other.kdbx");
            using (new NotebookStore().Create(other, Pw(), TestKdf.Fast())) { }
            Notebook nb2 = Reopen(other);
            shell.ShowEditor(nb2);
            Assert.That(shell.Editor.Notebook, Is.SameAs(nb2));
            Assert.Throws<ObjectDisposedException>(() => _store.Save(nb, false));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EditorView_InstantiatesWithoutXamlErrors()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x", "Bank", "y"));
            var view = new EditorView { DataContext = vm };
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            view.UpdateLayout();
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EditorView_WordWrapToggle_IsBoundTwoWay()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            EditorViewModel vm = NewVm(NewNotebook("Ledger", "x"));
            var view = new EditorView { DataContext = vm };
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            PumpDispatcher();

            var toggle = view.FindName("WordWrapToggle") as ToggleButton;
            Assert.That(toggle, Is.Not.Null, "a Word wrap CheckBox named WordWrapToggle");
            Binding binding = BindingOperations.GetBinding(toggle, ToggleButton.IsCheckedProperty);
            Assert.That(binding, Is.Not.Null);
            Assert.That(binding.Path.Path, Is.EqualTo("WordWrap"));
            Assert.That(binding.Mode, Is.EqualTo(BindingMode.TwoWay));
            Assert.That(toggle.IsChecked, Is.True);

            toggle.IsChecked = false;
            Assert.That(vm.WordWrap, Is.False);
            Assert.That(_settingsStore.Load().WordWrap, Is.False);

            vm.ToggleWordWrapCommand.Execute(null);
            PumpDispatcher();
            Assert.That(toggle.IsChecked, Is.True);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EditorView_HasNoShortcutKeyBindings()
        {
            // Shortcuts live on MainWindow so they keep working when focus leaves the editor (e.g. after a delete).
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var view = new EditorView { DataContext = NewVm(NewNotebook()) };
            Assert.That(view.InputBindings.Count, Is.EqualTo(0));
        }

        /// <summary>Runs queued dispatcher work (deferred binding attachment) as the app's message loop would.</summary>
        private static void PumpDispatcher()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        private static ICommand CommandFor(Window window, Key key, ModifierKeys modifiers)
        {
            PumpDispatcher();
            KeyBinding match = window.InputBindings.OfType<KeyBinding>()
                .SingleOrDefault(b => b.Key == key && b.Modifiers == modifiers);
            Assert.That(match, Is.Not.Null, "no window key binding for " + modifiers + "+" + key);
            return match.Command;
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_ShortcutsBindToEditorCommands_InertWithoutEditor()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var shell = new ShellViewModel(_store, _settingsStore, _settings, _dialogs, _clipboard, new FakeClock(),
                TestKdf.Fast);
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                Assert.That(shell.Editor, Is.Null);
                Assert.That(CommandFor(window, Key.N, ModifierKeys.Control), Is.Null, "inert while no editor");
                Assert.That(CommandFor(window, Key.Z, ModifierKeys.Alt), Is.Null);

                Notebook nb = NewNotebook("Ledger", "x");
                shell.ShowEditor(nb);
                EditorViewModel e = shell.Editor;

                Assert.That(CommandFor(window, Key.N, ModifierKeys.Control), Is.SameAs(e.NewNoteCommand));
                Assert.That(CommandFor(window, Key.S, ModifierKeys.Control), Is.SameAs(e.SaveCommand));
                Assert.That(CommandFor(window, Key.S, ModifierKeys.Control | ModifierKeys.Shift), Is.SameAs(e.SaveAsCommand));
                Assert.That(CommandFor(window, Key.F2, ModifierKeys.None), Is.SameAs(e.RenameNoteCommand));
                Assert.That(CommandFor(window, Key.OemPlus, ModifierKeys.Control), Is.SameAs(e.ZoomInCommand));
                Assert.That(CommandFor(window, Key.Add, ModifierKeys.Control), Is.SameAs(e.ZoomInCommand));
                Assert.That(CommandFor(window, Key.OemMinus, ModifierKeys.Control), Is.SameAs(e.ZoomOutCommand));
                Assert.That(CommandFor(window, Key.Subtract, ModifierKeys.Control), Is.SameAs(e.ZoomOutCommand));
                Assert.That(CommandFor(window, Key.D0, ModifierKeys.Control), Is.SameAs(e.ZoomResetCommand));
                Assert.That(CommandFor(window, Key.NumPad0, ModifierKeys.Control), Is.SameAs(e.ZoomResetCommand));
                Assert.That(CommandFor(window, Key.Z, ModifierKeys.Alt), Is.SameAs(e.ToggleWordWrapCommand));
                Assert.That(window.InputBindings.OfType<KeyBinding>().Any(b => b.Key == Key.Delete), Is.False,
                    "Del stays on the notes list only");

                shell.ShowUnlock(nb, "held");
                Assert.That(CommandFor(window, Key.S, ModifierKeys.Control), Is.Null, "inert again without an editor");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_CtrlN_AsksForTheName_ThroughTheSamePath()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var shell = new ShellViewModel(_store, _settingsStore, _settings, _dialogs, _clipboard, new FakeClock(),
                TestKdf.Fast);
            var window = new MainWindow { DataContext = shell };
            try
            {
                Notebook nb = NewNotebook("Ledger", "x");
                shell.ShowEditor(nb);
                ICommand ctrlN = CommandFor(window, Key.N, ModifierKeys.Control);

                ctrlN.Execute(null); // cancelled
                Assert.That(_dialogs.LastPromptTitle, Is.EqualTo("New note"));
                Assert.That(nb.Notes.Count, Is.EqualTo(1));
                Assert.That(nb.IsDirty, Is.False);

                _dialogs.Prompts.Enqueue("Typed");
                ctrlN.Execute(null);
                Assert.That(_dialogs.Calls.Count(c => c == "Prompt"), Is.EqualTo(2));
                Assert.That(nb.Notes.Select(n => n.Title), Is.EqualTo(new[] { "Ledger", "Typed" }));
                Assert.That(shell.Editor.SelectedNote.DisplayTitle, Is.EqualTo("Typed"));
            }
            finally
            {
                window.Close();
            }
        }
    }
}

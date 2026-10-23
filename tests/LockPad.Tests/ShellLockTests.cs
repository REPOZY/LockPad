using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LockPad.App;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.Core;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class ShellLockTests
    {
        private const string HeldPrefix = "Unsaved changes are held in memory. Saving failed:";
        private const string HeldConfirmTitle = "Unsaved changes";
        private const string HeldConfirmMessage =
            "Unsaved changes are held in memory because saving failed. Close anyway and lose them?";
        private const string CouldNotSaveTitle = "Could not save";

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

        private ShellViewModel NewShell()
        {
            var shell = new ShellViewModel(_store, _settingsStore, _settings, _dialogs, _clipboard, _clock, TestKdf.Fast);
            shell.WallClock = () => _wall;
            return shell;
        }

        /// <summary>Advances both the monotonic clock and the wall clock (normal time passing, no sleep).</summary>
        private void Advance(TimeSpan t)
        {
            _clock.Advance(t);
            _wall = _wall + t;
        }

        /// <summary>Creates nb.kdbx with the given (title, text) pairs, saved and clean, and opens it.</summary>
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

        private Notebook NewReadOnlyNotebook()
        {
            string p = NotebookPath;
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
            Notebook nb = _store.Open(p, Pw());
            _notebooks.Add(nb);
            Assert.That(nb.IsReadOnly, Is.True);
            return nb;
        }

        /// <summary>A shell showing the editor for a fresh notebook with one note ("Ledger", "original").</summary>
        private ShellViewModel OpenShell(out Notebook nb)
        {
            ShellViewModel shell = NewShell();
            nb = NewNotebook("Ledger", "original", "Bank", "bank text");
            shell.ShowEditor(nb);
            Quiet(shell);
            return shell;
        }

        /// <summary>Replaces the wait cursor so saves need no WPF input system.</summary>
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
        private void MakeFileWritable() { File.SetAttributes(NotebookPath, FileAttributes.Normal); }

        /// <summary>Locks with a failed save (read-only file attribute) so the notebook is held in memory.</summary>
        private ShellViewModel LockedWithHeldNotebook(out Notebook nb, out string noteId)
        {
            ShellViewModel shell = OpenShell(out nb);
            noteId = Edit(shell, "edited while away");
            MakeFileReadOnly();
            shell.Lock();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(((UnlockViewModel)shell.CurrentView).HasHeldNotebook, Is.True);
            return shell;
        }

        private static async Task Unlock(ShellViewModel shell, string password)
        {
            var unlock = (UnlockViewModel)shell.CurrentView;
            using (SecureString ss = UnlockViewModelTests.Secure(password)) await unlock.UnlockAsync(ss);
            Quiet(shell);
        }

        private static void AssertDisposed(NotebookStore store, Notebook nb)
        {
            Assert.Throws<ObjectDisposedException>(() => store.Save(nb, false));
        }

        private int Count(string call)
        {
            return _dialogs.Calls.Count(c => c == call);
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }

        // ---- idle lock (plan) ----

        [Test]
        public void Tick_BeforeTimeout_DoesNotLock()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Advance(new TimeSpan(0, 4, 59));
            shell.Tick();
            Assert.That(shell.Editor, Is.Not.Null);
            Assert.That(shell.CurrentView, Is.SameAs(shell.Editor));
        }

        [Test]
        public void Tick_AfterTimeout_Clean_LocksAndDisposes()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Advance(TimeSpan.FromMinutes(5));
            shell.Tick();

            Assert.That(shell.Editor, Is.Null);
            var unlock = shell.CurrentView as UnlockViewModel;
            Assert.That(unlock, Is.Not.Null);
            Assert.That(unlock.HeldWarning, Is.Null);
            Assert.That(unlock.HasHeldNotebook, Is.False);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            AssertDisposed(_store, nb);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void Tick_AfterTimeout_Dirty_HidesTheNotesFirst_NextTickSavesAndLocks()
        {
            // Full check 2026-10-22, L1: the lock's save runs for seconds on the UI thread, so the notes are hidden and
            // the clipboard cleared one Tick before it; the frozen window never shows them.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            Advance(TimeSpan.FromMinutes(5));

            shell.Tick();
            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(editor.IsObscured, Is.True);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("original"), "not saved yet");

            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(((UnlockViewModel)shell.CurrentView).HasHeldNotebook, Is.False);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            AssertDisposed(_store, nb);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void Resume_AfterLongSleep_Dirty_HidesTheNotesFirst_SecondTickSavesAndLocks()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            _wall = _wall + TimeSpan.FromHours(2);

            shell.OnResume();
            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(editor.IsObscured, Is.True);

            // Re-attack 2026-10-22 (fix 5, Low): the timer outranks rendering, so a Tick already queued could start the
            // save before the window redrew. The first Tick after hiding outside a Tick never saves.
            shell.Tick();
            Assert.That(shell.Editor, Is.SameAs(editor), "the window redraws before the save");
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("original"));

            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        public void LateInputHide_ThenQueuedResume_DoesNotSaveBeforeTheSecondTick()
        {
            // Final re-attack 2026-10-22 (Low 1): a resume queued with the late input locked at once, before the redraw.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            Advance(TimeSpan.FromMinutes(5));
            shell.RecordActivity();
            shell.OnResume();
            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("original"));

            shell.Tick();
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("original"));
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        public void TickHide_ThenQueuedResume_LeavesTheSaveToTheNextTick()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            Advance(TimeSpan.FromMinutes(5));
            shell.Tick();
            Assert.That(editor.IsObscured, Is.True);
            shell.OnResume();
            Assert.That(shell.Editor, Is.SameAs(editor), "the save waits for the next Tick");
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("original"));

            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        public void LateInputHide_ListenerThrows_FirstTickStillDoesNotSave()
        {
            // Final re-attack 2026-10-22 (Low 2): the wait-for-redraw mark was set only after the obscure step returned.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            editor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(EditorViewModel.IsObscured)) throw new InvalidOperationException("listener");
            };
            Advance(TimeSpan.FromMinutes(5));
            Assert.Throws<InvalidOperationException>(() => shell.RecordActivity());
            Assert.That(editor.IsObscured, Is.True);

            shell.Tick();
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("original"));
        }

        [Test]
        public void EditorText_WhileObscured_IsIgnored_NeverSaved()
        {
            // Re-attack 2026-10-22 (High): a key routed to the hidden editor (Enter, Backspace, Ctrl+V) changed the
            // note, and the lock saved that change unseen.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "seed words");
            Advance(TimeSpan.FromMinutes(5));
            shell.RecordActivity();
            Assert.That(editor.IsObscured, Is.True);

            editor.EditorText = "seed words\r\n";
            Assert.That(editor.EditorText, Is.EqualTo("seed words"));
            shell.Tick();
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("seed words"));
        }

        [Test]
        public void LateInput_HidingOrLocking_IsSwallowed_OnTimeInputIsNot()
        {
            // Re-attack 2026-10-22 (High): the main window swallowed a late key only when the editor was gone; a key
            // that only hid it went on to the hidden text box.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Assert.That(MainWindow.RecordActivityLocked(shell), Is.False, "input before the deadline reaches the editor");

            Edit(shell, "edited");
            Advance(TimeSpan.FromMinutes(5));
            Assert.That(MainWindow.RecordActivityLocked(shell), Is.True, "hidden: swallowed");
            Assert.That(shell.Editor.IsObscured, Is.True);
            Assert.That(MainWindow.RecordActivityLocked(shell), Is.True, "locked: swallowed");
            Assert.That(shell.Editor, Is.Null);
            Assert.That(MainWindow.RecordActivityLocked(null), Is.False);
        }

        [Test]
        public void Lock_Dirty_SavesThenLocks()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");

            shell.Lock();

            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.False);
            Assert.That(unlock.HeldWarning, Is.Null);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            AssertDisposed(_store, nb);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void Lock_SaveFails_HoldsNotebookWithWarning()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            MakeFileReadOnly();

            shell.Lock();

            Assert.That(shell.Editor, Is.Null);
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.True);
            Assert.That(unlock.HeldWarning, Does.StartWith(HeldPrefix));
            Assert.That(unlock.HeldWarning.Length, Is.GreaterThan(HeldPrefix.Length + 1), "the failure reason follows");
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            Assert.That(_dialogs.Calls, Is.Empty, "no dialogs while the user may be away");
            // Still open (not disposed) and still dirty.
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.IoError));
        }

        [Test]
        public async Task Lock_SaveFails_UnlockRestoresEdit()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);

            await Unlock(shell, "pw");

            Assert.That(shell.Editor, Is.Not.Null);
            Assert.That(shell.Editor.Notebook, Is.SameAs(nb));
            Assert.That(shell.Editor.Notebook.FindNote(id).Text, Is.EqualTo("edited while away"));
            Assert.That(nb.IsDirty, Is.True);
        }

        [Test]
        public void Lock_ExternalChange_HoldsWithoutDialog()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            ChangeExternally(NotebookPath);
            Edit(shell, "edited");

            shell.Lock();

            Assert.That(_dialogs.Calls, Does.Not.Contain("AskExternalChange"));
            Assert.That(_dialogs.Calls, Is.Empty);
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.True);
            Assert.That(unlock.HeldWarning, Does.StartWith(HeldPrefix));
        }

        [Test]
        public void RecordActivity_ResetsTimer()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Advance(TimeSpan.FromMinutes(4));
            shell.RecordActivity();
            Advance(TimeSpan.FromMinutes(4));
            shell.Tick();
            Assert.That(shell.Editor, Is.Not.Null);
        }

        [Test]
        public void SettingsChanged_UpdatesIdleTimeout()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _dialogs.SettingsResult = true;
            _dialogs.OnShowSettings = vm => vm.IdleLockMinutes = 1;
            shell.OpenSettings();
            Assert.That(_settings.IdleLockMinutes, Is.EqualTo(1));

            Advance(TimeSpan.FromMinutes(1));
            shell.Tick();

            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
        }

        [Test]
        public void IdleTimeout_ReadFromSharedSettings_Clamped()
        {
            _settings.IdleLockMinutes = 2;
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Advance(new TimeSpan(0, 1, 59));
            shell.Tick();
            Assert.That(shell.Editor, Is.Not.Null);
            Advance(TimeSpan.FromSeconds(1));
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);

            // Out-of-range values never disable the lock: 0 is treated as 1 minute.
            _settings.IdleLockMinutes = 0;
            Notebook nb2 = NewNotebookAt("second.kdbx");
            shell.ShowEditor(nb2);
            Advance(TimeSpan.FromMinutes(1));
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
        }

        private Notebook NewNotebookAt(string name)
        {
            string p = _dir.File(name);
            using (new NotebookStore().Create(p, Pw(), TestKdf.Fast())) { }
            Notebook nb = _store.Open(p, Pw());
            _notebooks.Add(nb);
            return nb;
        }

        [Test]
        public void Tick_UpdatesCountdownText_EmptyWhenLocked()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            shell.Tick();
            Assert.That(shell.LockCountdownText, Is.EqualTo("Locks in 5:00"));
            Advance(TimeSpan.FromMinutes(4));
            shell.Tick();
            Assert.That(shell.LockCountdownText, Is.EqualTo("Locks in 1:00"));
            Advance(TimeSpan.FromSeconds(50.5));
            shell.Tick();
            Assert.That(shell.LockCountdownText, Is.EqualTo("Locks in 0:10"), "partial seconds round up");
            Advance(TimeSpan.FromSeconds(10));
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.LockCountdownText, Is.EqualTo(""));
            shell.Tick();
            Assert.That(shell.LockCountdownText, Is.EqualTo(""));
        }

        [Test]
        public void Lock_WithoutEditor_IsNoOp()
        {
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            object page = shell.CurrentView;
            shell.Lock();
            shell.Tick();
            Assert.That(shell.CurrentView, Is.SameAs(page));
            Assert.That(_clipboard.ClearCount, Is.EqualTo(0));
        }

        [Test]
        public void RecordActivity_AfterDeadline_LocksInsteadOfReviving()
        {
            // E.g. the first mouse move after waking: input processed before the next Tick must not extend the session.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Advance(TimeSpan.FromMinutes(5));
            shell.RecordActivity();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
        }

        [Test]
        public void RecordActivity_AfterDeadline_Dirty_HidesTheNotesFirst_SecondTickSavesAndLocks()
        {
            // Full-check red team 2026-10-22: late input took the direct lock, so its save ran with the notes visible.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            Edit(shell, "edited");
            Advance(TimeSpan.FromMinutes(5));

            shell.RecordActivity();
            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(editor.IsObscured, Is.True);

            shell.Tick(); // possibly queued before the window redrew: never saves
            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("original"));

            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        public void ShowEditor_StartsAFullTimeout()
        {
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            Advance(TimeSpan.FromMinutes(30)); // time spent on the unlock page
            Notebook nb = NewNotebook("Ledger", "x");
            shell.ShowEditor(nb);
            shell.Tick();
            Assert.That(shell.Editor, Is.Not.Null);
            Assert.That(shell.LockCountdownText, Is.EqualTo("Locks in 5:00"));
        }

        // ---- decision 1: lock after wake ----

        [Test]
        public void Resume_AfterLongSleep_Locks()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _wall = _wall + TimeSpan.FromHours(2); // the monotonic clock did not advance during sleep
            shell.OnResume();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
        }

        [Test]
        public void Resume_AfterShortSleep_DoesNotLock()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _wall = _wall + TimeSpan.FromMinutes(1);
            shell.OnResume();
            Assert.That(shell.Editor, Is.Not.Null);
        }

        [Test]
        public void Tick_WallClockExpired_Locks()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _wall = _wall + TimeSpan.FromMinutes(6);
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
        }

        [Test]
        public void WallClockSetBackwards_TickClampsLastActivity_WallExpiryStillWorks()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _wall = _wall - TimeSpan.FromHours(3);
            shell.Tick(); // clamps the last activity to the new wall-clock time
            Assert.That(shell.Editor, Is.Not.Null);

            _wall = _wall + TimeSpan.FromMinutes(5); // e.g. a sleep after the clock change; monotonic clock stalled
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
        }

        [Test]
        public void WallClockSetBackwards_MonotonicStillLocks()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _wall = _wall - TimeSpan.FromHours(3);
            _clock.Advance(TimeSpan.FromMinutes(5));
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
        }

        // ---- decision 2: never lock inside a dialog or a save ----

        [Test]
        public void Tick_WhileDialogOpen_DefersLock()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _dialogs.IsShowingDialog = true;
            Advance(TimeSpan.FromMinutes(10));
            shell.Tick();
            shell.OnResume();
            Assert.That(shell.Editor, Is.Not.Null, "deferred while a dialog is open");

            _dialogs.IsShowingDialog = false;
            shell.Tick();
            Assert.That(shell.Editor, Is.Null, "locks on the first Tick after the dialog closed");
        }

        [Test]
        public void Resume_WhileDialogOpen_LocksOnFirstTickAfterDialog()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            _dialogs.IsShowingDialog = true;
            _wall = _wall + TimeSpan.FromHours(1);
            shell.OnResume();
            Assert.That(shell.Editor, Is.Not.Null);
            _dialogs.IsShowingDialog = false;
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
        }

        [Test]
        public void Tick_DuringSave_DefersLock()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            EditorViewModel editor = shell.Editor;
            EditorViewModel duringSave = null;
            bool savingFlag = false;
            _store.TestHook = (stage, path) =>
            {
                if (stage != "before-replace") return;
                Advance(TimeSpan.FromMinutes(10));
                savingFlag = editor.IsSaving;
                shell.Tick();
                shell.OnResume();
                duringSave = shell.Editor;
            };

            Assert.That(editor.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));
            _store.TestHook = null;

            Assert.That(savingFlag, Is.True);
            Assert.That(duringSave, Is.SameAs(editor), "no lock in the middle of a save");
            Assert.That(editor.IsSaving, Is.False);
            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
        }

        // ---- SEC-H1 / RT-M4: idle expiry under a dialog or a save hides the content at once ----

        [Test]
        public void IdleExpiry_WhileDialogOpen_ObscuresAndClearsClipboard_KeepsEditorAndDialog()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string id = Edit(shell, "seed words");
            _clipboard.OwnsClipboardContent = true; // something was copied before the user walked away
            _dialogs.IsShowingDialog = true;
            Assert.That(editor.IsObscured, Is.False);

            Advance(TimeSpan.FromMinutes(5));
            shell.Tick();

            Assert.That(editor.IsObscured, Is.True, "the note text and list are hidden at the deadline");
            Assert.That(_clipboard.OwnsClipboardContent, Is.False, "LockPad's clipboard content is cleared at once");
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            Assert.That(shell.Editor, Is.SameAs(editor), "no teardown under the dialog");
            Assert.That(shell.CurrentView, Is.SameAs(editor));
            Assert.That(editor.SelectedNote, Is.Not.Null, "the editor's content is not cleared");
            Assert.That(editor.EditorText, Is.EqualTo("seed words"));
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("seed words"));
            Assert.That(_dialogs.IsShowingDialog, Is.True, "the dialog is left open");
            Assert.That(_dialogs.Calls, Is.Empty, "no dialog shown or closed");

            shell.Tick();
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1), "cleared once, on the transition");
        }

        [Test]
        public void Obscured_DialogCloses_NextTickSavesAndLocks()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited under the dialog");
            _dialogs.IsShowingDialog = true;
            Advance(TimeSpan.FromMinutes(5));
            shell.Tick();
            Assert.That(shell.Editor.IsObscured, Is.True);

            _dialogs.IsShowingDialog = false;
            shell.Tick();

            Assert.That(shell.Editor, Is.Null);
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.False);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited under the dialog"));
            AssertDisposed(_store, nb);
        }

        [Test]
        public void Obscured_DialogCloses_SaveFails_Holds()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            string id = Edit(shell, "edited");
            _dialogs.IsShowingDialog = true;
            Advance(TimeSpan.FromMinutes(5));
            shell.Tick();
            MakeFileReadOnly();

            _dialogs.IsShowingDialog = false;
            shell.Tick();

            Assert.That(shell.Editor, Is.Null);
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.True);
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("edited"));
        }

        [Test]
        public void Obscured_ActivityDoesNotUnobscure_AlwaysProceedsToLock()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            _dialogs.IsShowingDialog = true;
            Advance(TimeSpan.FromMinutes(5));
            shell.Tick();
            Assert.That(editor.IsObscured, Is.True);

            // Activity while the dialog is still open, and a longer timeout chosen inside the Settings dialog.
            shell.RecordActivity();
            _settings.IdleLockMinutes = 60;
            shell.RecordActivity();
            shell.Tick();
            shell.OnResume();
            Assert.That(editor.IsObscured, Is.True, "activity never un-obscures");
            Assert.That(shell.Editor, Is.SameAs(editor), "still deferred while the dialog is open");

            _dialogs.IsShowingDialog = false;
            shell.Tick();
            Assert.That(shell.Editor, Is.Null, "the lock still happens although the new timeout has not expired");
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
        }

        [Test]
        public void Obscured_SettingsOkWithLongerTimeout_ThenActivity_Locks()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            bool? obscuredInside = null;
            _dialogs.SettingsResult = true;
            _dialogs.OnShowSettings = vm =>
            {
                // The Settings dialog stays open past the deadline; then someone raises the timeout and presses OK.
                _dialogs.IsShowingDialog = true;
                Advance(TimeSpan.FromMinutes(5));
                shell.Tick();
                obscuredInside = editor.IsObscured;
                vm.IdleLockMinutes = 60;
                shell.RecordActivity();
                _dialogs.IsShowingDialog = false;
            };

            shell.OpenSettings(); // OK applies the 60-minute timeout: the idle deadline is no longer expired
            Assert.That(obscuredInside, Is.True);
            Assert.That(_settings.IdleLockMinutes, Is.EqualTo(60));

            shell.RecordActivity(); // e.g. the click that dismissed the dialog reaches the window before the next Tick

            Assert.That(shell.Editor, Is.Null, "an obscured session never revives");
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
        }

        [Test]
        public void Resume_AfterLongSleep_WhileDialogOpen_Obscures()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            _clipboard.OwnsClipboardContent = true;
            _dialogs.IsShowingDialog = true;
            _wall = _wall + TimeSpan.FromHours(2); // wall-clock expiry; the monotonic clock stalled in sleep

            shell.OnResume();

            Assert.That(editor.IsObscured, Is.True);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
            Assert.That(shell.Editor, Is.SameAs(editor));
        }

        [Test]
        public void IdleExpiry_WhileSaveInProgress_ObscuresAndClearsClipboard_ThenLocksAfterSave()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            EditorViewModel editor = shell.Editor;
            _clipboard.OwnsClipboardContent = true;
            bool? obscuredDuringSave = null, ownedDuringSave = null;
            EditorViewModel duringSave = null;
            _store.TestHook = (stage, path) =>
            {
                if (stage != "before-replace") return;
                Advance(TimeSpan.FromMinutes(10));
                shell.Tick();
                obscuredDuringSave = editor.IsObscured;
                ownedDuringSave = _clipboard.OwnsClipboardContent;
                duringSave = shell.Editor;
            };

            Assert.That(editor.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));
            _store.TestHook = null;

            Assert.That(obscuredDuringSave, Is.True);
            Assert.That(ownedDuringSave, Is.False);
            Assert.That(duringSave, Is.SameAs(editor), "no teardown in the middle of a save");
            Assert.That(editor.IsObscured, Is.True, "stays obscured until the lock");

            shell.Tick();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
        }

        // ---- decision 3: held unlock page only on the unlocked -> locked transition ----

        [Test]
        public void HeldLock_RepeatedTickLockResume_KeepSameUnlockPage()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            object page = shell.CurrentView;

            for (int i = 0; i < 3; i++)
            {
                Advance(TimeSpan.FromMinutes(10));
                _wall = _wall + TimeSpan.FromHours(1);
                shell.Tick();
                shell.Lock();
                shell.OnResume();
                shell.RecordActivity();
            }

            Assert.That(shell.CurrentView, Is.SameAs(page));
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        // ---- decision 4: never dispose a dirty notebook implicitly ----

        [Test]
        public async Task HeldLock_RestoreThenEditAndSave_FileHasBothEdits()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            await Unlock(shell, "pw");
            MakeFileWritable();

            EditorViewModel e = shell.Editor;
            e.SelectedNote = e.VisibleNotes.First(i => i.DisplayTitle == "Bank");
            e.EditorText = "second edit";
            Assert.That(e.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));

            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited while away"));
            Assert.That(NoteText(NotebookPath, "Bank"), Is.EqualTo("second edit"));
        }

        [Test]
        public void ShowEditor_DifferentNotebookWhilePreviousDirty_Throws_KeepsPrevious()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "unsaved");
            EditorViewModel editor = shell.Editor;
            Notebook other = NewNotebookAt("other.kdbx");

            Assert.Throws<InvalidOperationException>(() => shell.ShowEditor(other));

            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(shell.CurrentView, Is.SameAs(editor));
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved), "not disposed");
        }

        // ---- decision 9: lock clears the screen ----

        [Test]
        public void Lock_ClearsEditorText_AndDetaches()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            editor.FilterText = "led";
            Assert.That(editor.EditorText, Is.EqualTo("original"));
            var raised = new List<string>();
            editor.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            shell.Lock();

            Assert.That(editor.SelectedNote, Is.Null);
            Assert.That(editor.EditorText, Is.EqualTo(""));
            Assert.That(editor.FilterText, Is.EqualTo(""));
            Assert.That(editor.VisibleNotes, Is.Empty);
            Assert.That(raised, Does.Contain("EditorText"), "the bound TextBox is told to drop the note text");
            Assert.That(shell.LockCountdownText, Is.EqualTo(""));
        }

        [Test]
        public void HeldLock_ClearsEditorText_KeepsNotes()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            string id = Edit(shell, "secret edit");
            MakeFileReadOnly();

            shell.Lock();

            Assert.That(editor.EditorText, Is.EqualTo(""));
            Assert.That(editor.SelectedNote, Is.Null);
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("secret edit"), "clearing the screen never edits the note");
            int statusChanges = 0;
            editor.PropertyChanged += (s, e) => statusChanges++;
            nb.AddNote("later");
            Assert.That(statusChanges, Is.EqualTo(0), "detached");
        }

        // ---- TryClose (plan + decisions 5, 8, 8a) ----

        [Test]
        public void TryClose_NoNotebook_ReturnsTrue()
        {
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            Assert.That(shell.TryClose(), Is.True);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void TryClose_Clean_DisposesClearsClipboard_ReturnsTrue()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            byte[] before = File.ReadAllBytes(NotebookPath);
            Assert.That(shell.TryClose(), Is.True);
            AssertDisposed(_store, nb);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before), "nothing to save");
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.Null, "the window shows nothing while it closes");
        }

        [Test]
        public void TryClose_Dirty_Saves_ReturnsTrue()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");

            Assert.That(shell.TryClose(), Is.True);

            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            AssertDisposed(_store, nb);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        // ---- RT-M3: a lasting save failure on close offers Save As / Close without saving / Cancel ----

        private const string DiscardMessage = "Discard all unsaved changes? This cannot be undone.";

        /// <summary>A dirty editor whose save fails (read-only file); returns the failure reason.</summary>
        private ShellViewModel OpenShellWithFailingSave(out Notebook nb, out EditorViewModel editor, out string reason)
        {
            ShellViewModel shell = OpenShell(out nb);
            editor = shell.Editor;
            Edit(shell, "edited");
            MakeFileReadOnly();
            reason = _store.Save(nb, false).Message; // the same failure TryClose's save will hit
            Assert.That(reason, Is.Not.Empty);
            return shell;
        }

        private void AssertStillOpen(ShellViewModel shell, EditorViewModel editor, Notebook nb)
        {
            Assert.That(shell.Editor, Is.SameAs(editor));
            Assert.That(shell.CurrentView, Is.SameAs(editor));
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(nb.FindNote(editor.SelectedNote.Id).Text, Is.EqualTo("edited"), "edits intact");
        }

        [Test]
        public void TryClose_SaveFails_Cancel_StaysOpen()
        {
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.Cancel);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskCloseAfterSaveFailure" }), "one dialog, no 'Save failed'");
            Assert.That(_dialogs.LastCloseChoiceTitle, Is.EqualTo(CouldNotSaveTitle));
            Assert.That(_dialogs.LastCloseChoiceMessage, Does.Contain(reason));
            AssertStillOpen(shell, editor, nb);
        }

        [Test]
        public void TryClose_SaveFails_SaveAsSucceeds_Closes()
        {
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);
            string copy = _dir.File("copy.kdbx");
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.SaveAs);
            _dialogs.SaveAsPaths.Enqueue(copy);
            _clipboard.OwnsClipboardContent = true;

            Assert.That(shell.TryClose(), Is.True);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskCloseAfterSaveFailure", "PickSaveAsPath" }));
            Assert.That(NoteText(copy, "Ledger"), Is.EqualTo("edited"));
            AssertDisposed(_store, nb);
            Assert.That(shell.Editor, Is.Null);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
        }

        [Test]
        public void TryClose_SaveFails_SaveAsToBackupCopyName_Refused_StaysOpen()
        {
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);
            string target = _dir.File("rescue.kdbx.bak2");
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.SaveAs);
            _dialogs.SaveAsPaths.Enqueue(target);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo("This name is reserved for backup copies. Choose another name."));
            Assert.That(File.Exists(target), Is.False);
            AssertStillOpen(shell, editor, nb);
        }

        [Test]
        public void TryClose_SaveFails_SaveAsPickerCancelled_StaysOpen()
        {
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.SaveAs); // no path queued: the picker is closed

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskCloseAfterSaveFailure", "PickSaveAsPath" }));
            AssertStillOpen(shell, editor, nb);
        }

        [Test]
        public void TryClose_SaveFails_SaveAsFails_StaysOpen_KeepsTheExistingFile()
        {
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);
            string taken = _dir.File("taken.kdbx");
            File.WriteAllText(taken, "not a notebook");
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.SaveAs);
            _dialogs.SaveAsPaths.Enqueue(taken);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskCloseAfterSaveFailure", "PickSaveAsPath", "ShowError" }));
            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo("File already exists."), "the Save As guards apply");
            Assert.That(File.ReadAllText(taken), Is.EqualTo("not a notebook"));
            AssertStillOpen(shell, editor, nb);
        }

        [Test]
        public void TryClose_SaveFails_CloseWithoutSaving_DiscardNo_StaysOpen()
        {
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.CloseWithoutSaving);
            _dialogs.Confirms.Enqueue(false);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskCloseAfterSaveFailure", "ConfirmDefaultNo" }));
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo(DiscardMessage));
            AssertStillOpen(shell, editor, nb);
        }

        [Test]
        public void TryClose_SaveFails_CloseWithoutSaving_DiscardYes_ClosesClearsAndDisposes()
        {
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);
            byte[] before = File.ReadAllBytes(NotebookPath);
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.CloseWithoutSaving);
            _dialogs.Confirms.Enqueue(true);
            _clipboard.OwnsClipboardContent = true;

            Assert.That(shell.TryClose(), Is.True);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskCloseAfterSaveFailure", "ConfirmDefaultNo" }));
            Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before), "nothing written");
            AssertDisposed(_store, nb);
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.Null);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
            Assert.That(editor.EditorText, Is.EqualTo(""), "the view was emptied");
        }

        [Test]
        public void TryClose_FileGone_OffersSaveAs()
        {
            // Red-team PoC P9: the notebook file disappeared (USB stick removed, moved by sync).
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            File.Delete(NotebookPath);
            string copy = _dir.File("rescued.kdbx");
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.SaveAs);
            _dialogs.SaveAsPaths.Enqueue(copy);

            Assert.That(shell.TryClose(), Is.True);

            Assert.That(_dialogs.LastCloseChoiceMessage, Does.Contain("File not found."));
            Assert.That(NoteText(copy, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        public void PrepareForSessionEnd_SaveFails_NeverShowsTheCloseChoice()
        {
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);

            Assert.That(shell.PrepareForSessionEnd(), Is.False, "refuses quietly");

            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(((UnlockViewModel)shell.CurrentView).HasHeldNotebook, Is.True);
        }

        [Test]
        public void TryClose_ExternalChange_UserCancels_ReturnsFalse_NoErrorDialog()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            ChangeExternally(NotebookPath);
            Edit(shell, "edited");
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.Cancel);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskExternalChange" }), "the user is present: the dialog is allowed");
            Assert.That(shell.Editor, Is.Not.Null);
            Assert.That(nb.IsDirty, Is.True);
        }

        [Test]
        public void TryClose_ExternalChange_SaveAsPickerClosed_ReturnsFalse_NoErrorDialog()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            ChangeExternally(NotebookPath);
            Edit(shell, "edited");
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.SaveAs);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "AskExternalChange", "PickSaveAsPath" }));
            Assert.That(shell.Editor, Is.Not.Null);
        }

        [Test]
        public void TryClose_ExternalChange_Overwrite_ReturnsTrue()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            ChangeExternally(NotebookPath);
            Edit(shell, "edited");
            _dialogs.ExternalChanges.Enqueue(ExternalChangeChoice.Overwrite);

            Assert.That(shell.TryClose(), Is.True);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        public void TryClose_ReadOnlyNotebook_ReturnsTrue_NoSave()
        {
            ShellViewModel shell = NewShell();
            Notebook nb = NewReadOnlyNotebook();
            byte[] before = File.ReadAllBytes(NotebookPath);
            shell.ShowEditor(nb);
            Quiet(shell);

            Assert.That(shell.TryClose(), Is.True);

            Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before));
            Assert.That(_dialogs.Calls, Is.Empty);
            AssertDisposed(_store, nb);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        // ---- Fix round 2, C (L4): discarding a held notebook needs both default-No confirmations ----

        private void AssertHeldIntact(ShellViewModel shell, object page, Notebook nb, string id)
        {
            Assert.That(shell.CurrentView, Is.SameAs(page));
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("edited while away"));
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.IoError), "still open, not disposed");
        }

        [Test]
        public void TryClose_HeldNotebook_FirstNo_StaysOpen()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            object page = shell.CurrentView;
            _dialogs.Confirms.Enqueue(false);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo" }), "default No (Enter and Esc answer No)");
            Assert.That(_dialogs.LastConfirmTitle, Is.EqualTo(HeldConfirmTitle));
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo(HeldConfirmMessage));
            AssertHeldIntact(shell, page, nb, id);
        }

        [Test]
        public void TryClose_HeldNotebook_FirstYesSecondNo_StaysOpen()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            object page = shell.CurrentView;
            _dialogs.Confirms.Enqueue(true);
            _dialogs.Confirms.Enqueue(false);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo", "ConfirmDefaultNo" }));
            Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo("Discard all unsaved changes? This cannot be undone."));
            AssertHeldIntact(shell, page, nb, id);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1), "only the lock's clear: nothing was torn down");
        }

        [Test]
        public void TryClose_HeldNotebook_BothYes_Closes()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            _dialogs.Confirms.Enqueue(true);
            _dialogs.Confirms.Enqueue(true);

            Assert.That(shell.TryClose(), Is.True);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo", "ConfirmDefaultNo" }));
            AssertDisposed(_store, nb);
        }

        [Test]
        public void TryClose_DeclinedDiscard_ThenLockHolds_HeldCloseStillNeedsBothConfirmations()
        {
            // Re-attack PoC L4 (R4): decline "Close without saving", let the idle lock hold the notebook, close again.
            Notebook nb;
            EditorViewModel editor;
            string reason;
            ShellViewModel shell = OpenShellWithFailingSave(out nb, out editor, out reason);
            _dialogs.CloseChoices.Enqueue(CloseSaveFailureChoice.CloseWithoutSaving);
            _dialogs.Confirms.Enqueue(false);
            Assert.That(shell.TryClose(), Is.False);
            Advance(TimeSpan.FromMinutes(30));
            shell.Tick(); // hides the notes before the save (full check 2026-10-22, L1)
            shell.Tick();
            Assert.That(((UnlockViewModel)shell.CurrentView).HasHeldNotebook, Is.True);

            int before = _dialogs.Calls.Count;
            _dialogs.Confirms.Enqueue(true); // one Yes is no longer enough
            Assert.That(shell.TryClose(), Is.False);
            Assert.That(_dialogs.Calls.Skip(before), Is.EqualTo(new[] { "ConfirmDefaultNo", "ConfirmDefaultNo" }));
            Assert.That(nb.FindNote(editor.SelectedNote == null ? nb.Notes[0].Id : editor.SelectedNote.Id), Is.Not.Null);
            Assert.That(nb.IsDirty, Is.True);
        }

        [Test]
        public async Task TryClose_WhileUnlockBusy_ReturnsFalse_HeldNotDisposed()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            var unlock = (UnlockViewModel)shell.CurrentView;
            var gate = new TaskCompletionSource<int>();
            unlock.DelayAsync = t => gate.Task;

            using (SecureString wrong = UnlockViewModelTests.Secure("nope"))
            {
                Task attempt = unlock.UnlockAsync(wrong);
                for (int i = 0; i < 500 && !unlock.IsWaiting; i++) await Task.Delay(10);
                Assert.That(unlock.IsBusy, Is.True);

                Assert.That(shell.TryClose(), Is.False);
                Assert.That(_dialogs.Calls, Is.Empty, "does nothing while the unlock page is busy");
                Assert.That(nb.FindNote(id).Text, Is.EqualTo("edited while away"));

                gate.SetResult(0);
                await attempt;
            }

            _dialogs.Confirms.Enqueue(false);
            Assert.That(shell.TryClose(), Is.False);
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo" }));
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.IoError), "never disposed");
        }

        [Test]
        public void TryClose_WhileDialogOpen_ReturnsFalse_DoesNothing()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            _dialogs.IsShowingDialog = true;
            byte[] before = File.ReadAllBytes(NotebookPath);

            Assert.That(shell.TryClose(), Is.False);

            Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before));
            Assert.That(shell.Editor, Is.Not.Null);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        // ---- decision 7: session end ----

        [Test]
        public void PrepareForSessionEnd_NoNotebook_Allows()
        {
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            Assert.That(shell.PrepareForSessionEnd(), Is.True);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void PrepareForSessionEnd_Clean_AllowsAfterClipboardClear()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Assert.That(shell.PrepareForSessionEnd(), Is.True);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void PrepareForSessionEnd_ReadOnly_Allows()
        {
            ShellViewModel shell = NewShell();
            shell.ShowEditor(NewReadOnlyNotebook());
            Assert.That(shell.PrepareForSessionEnd(), Is.True);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void PrepareForSessionEnd_Dirty_SavesWithoutDialogs_Allows()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            Assert.That(shell.PrepareForSessionEnd(), Is.True);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void PrepareForSessionEnd_DirtySaveFails_CancelsAndHolds()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            string id = Edit(shell, "edited");
            MakeFileReadOnly();

            Assert.That(shell.PrepareForSessionEnd(), Is.False);

            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(shell.Editor, Is.Null);
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.True);
            Assert.That(unlock.HeldWarning, Does.StartWith(HeldPrefix));
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("edited"));
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void PrepareForSessionEnd_ExternalChange_CancelsWithoutDialog()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            ChangeExternally(NotebookPath);
            Edit(shell, "edited");
            Assert.That(shell.PrepareForSessionEnd(), Is.False);
            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(((UnlockViewModel)shell.CurrentView).HasHeldNotebook, Is.True);
        }

        [Test]
        public void PrepareForSessionEnd_HeldSaveStillFails_Cancels_KeepsUnlockPage()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            object page = shell.CurrentView;

            Assert.That(shell.PrepareForSessionEnd(), Is.False);

            Assert.That(shell.CurrentView, Is.SameAs(page));
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("edited while away"));
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void PrepareForSessionEnd_HeldSaveNowSucceeds_Allows()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            MakeFileWritable();

            Assert.That(shell.PrepareForSessionEnd(), Is.True);

            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited while away"));
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.False, "nothing is held any more");
            AssertDisposed(_store, nb);
        }

        [Test]
        public async Task PrepareForSessionEnd_HeldWhileUnlockBusy_Cancels()
        {
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            MakeFileWritable();
            var unlock = (UnlockViewModel)shell.CurrentView;
            var gate = new TaskCompletionSource<int>();
            unlock.DelayAsync = t => gate.Task;
            byte[] before = File.ReadAllBytes(NotebookPath);

            using (SecureString wrong = UnlockViewModelTests.Secure("nope"))
            {
                Task attempt = unlock.UnlockAsync(wrong);
                for (int i = 0; i < 500 && !unlock.IsWaiting; i++) await Task.Delay(10);

                Assert.That(shell.PrepareForSessionEnd(), Is.False);
                Assert.That(File.ReadAllBytes(NotebookPath), Is.EqualTo(before), "no save under the background compare");
                Assert.That(shell.CurrentView, Is.SameAs(unlock));

                gate.SetResult(0);
                await attempt;
            }
        }

        [Test]
        public void PrepareForSessionEnd_DirtyWhileDialogOpen_SavesQuietly_Allows_KeepsEditor()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            EditorViewModel editor = shell.Editor;
            _dialogs.IsShowingDialog = true;

            Assert.That(shell.PrepareForSessionEnd(), Is.True);

            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
            Assert.That(shell.Editor, Is.SameAs(editor), "the open dialog may still use the editor");
            Assert.That(shell.CurrentView, Is.SameAs(editor));
            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void PrepareForSessionEnd_DirtySaveFailsWhileDialogOpen_Cancels_NoHold()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            string id = Edit(shell, "edited");
            EditorViewModel editor = shell.Editor;
            _dialogs.IsShowingDialog = true;
            MakeFileReadOnly();

            Assert.That(shell.PrepareForSessionEnd(), Is.False);

            Assert.That(shell.Editor, Is.SameAs(editor), "no Hold while a dialog is open");
            Assert.That(shell.CurrentView, Is.SameAs(editor));
            Assert.That(editor.EditorText, Is.EqualTo("edited"), "the view is not cleared under the dialog");
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("edited"));
            Assert.That(nb.IsDirty, Is.True);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void PrepareForSessionEnd_WhileSaving_Cancels()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            bool? during = null;
            bool entered = false;
            _store.TestHook = (stage, path) =>
            {
                if (stage != "before-replace" || entered) return;
                entered = true; // never re-enter if the call under test saves again
                during = shell.PrepareForSessionEnd();
            };

            Assert.That(shell.Editor.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));
            _store.TestHook = null;

            Assert.That(during, Is.False);
        }

        [Test]
        public void SessionEndAndClose_DuringLocksSave_ReturnFalse_LockCompletesNormally()
        {
            // Argon2 waits on the STA thread pump sent messages, so WM_QUERYENDSESSION / WM_CLOSE can arrive inside Lock.
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            bool? sessionEnd = null, close = null;
            bool entered = false;
            _store.TestHook = (stage, path) =>
            {
                if (stage != "before-replace" || entered) return;
                entered = true; // never re-enter if the calls under test save again
                sessionEnd = shell.PrepareForSessionEnd();
                close = shell.TryClose();
            };

            shell.Lock();
            _store.TestHook = null;

            Assert.That(sessionEnd, Is.False);
            Assert.That(close, Is.False);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"), "the outer lock's save completed");
            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HasHeldNotebook, Is.False, "no spurious hold");
            Assert.That(unlock.HeldWarning, Is.Null);
            AssertDisposed(_store, nb);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void PrepareForSessionEnd_CleanWhileDialogOpen_Allows()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            EditorViewModel editor = shell.Editor;
            _dialogs.IsShowingDialog = true;
            Assert.That(shell.PrepareForSessionEnd(), Is.True);
            Assert.That(shell.Editor, Is.SameAs(editor), "nothing is torn down under the open dialog");
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        // ---- decision 6: clipboard clear on exit ----

        [Test]
        public void ClearWithRetry_NotOwned_DoesNothing()
        {
            var clip = new FakeClipboardService();
            int waits = 0;
            Assert.That(ClipboardExitHelper.ClearWithRetry(clip, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500),
                t => waits++), Is.True);
            Assert.That(clip.ClearCount, Is.EqualTo(0));
            Assert.That(waits, Is.EqualTo(0));
        }

        [Test]
        public void ClearWithRetry_ClearsOnThirdAttempt()
        {
            var clip = new FakeClipboardService { OwnsClipboardContent = true, ClearsBeforeRelease = 2 };
            var waits = new List<TimeSpan>();

            bool cleared = ClipboardExitHelper.ClearWithRetry(clip, TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(500), t => waits.Add(t));

            Assert.That(cleared, Is.True);
            Assert.That(clip.ClearCount, Is.EqualTo(3));
            Assert.That(clip.OwnsClipboardContent, Is.False);
            Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500) }));
        }

        [Test]
        public void ClearWithRetry_NeverClears_GivesUpAtBudget()
        {
            var clip = new FakeClipboardService { OwnsClipboardContent = true, NeverRelease = true };
            int waits = 0;

            bool cleared = ClipboardExitHelper.ClearWithRetry(clip, MainWindow.ExitClipboardBudget,
                MainWindow.ExitClipboardInterval, t => waits++);

            Assert.That(cleared, Is.False);
            Assert.That(waits, Is.EqualTo(40), "20 s of 500 ms waits");
            Assert.That(clip.ClearCount, Is.EqualTo(41), "one attempt before and one after every wait");
            Assert.That(clip.OwnsClipboardContent, Is.True);
        }

        /// <summary>C3 (user decision 2026-10-18): the exit clear retries for up to 20 s (Win+V history has been seen
        /// holding the clipboard for about 15 s).</summary>
        [Test]
        public void ExitClipboardBudget_Is20Seconds_In500msSteps()
        {
            Assert.That(MainWindow.ExitClipboardBudget, Is.EqualTo(TimeSpan.FromSeconds(20)));
            Assert.That(MainWindow.ExitClipboardInterval, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
        }

        [Test]
        public void ClearWithRetry_RejectsBadArguments()
        {
            var clip = new FakeClipboardService();
            Action<TimeSpan> wait = t => { };
            Assert.Throws<ArgumentNullException>(() => ClipboardExitHelper.ClearWithRetry(null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), wait));
            Assert.Throws<ArgumentNullException>(() => ClipboardExitHelper.ClearWithRetry(clip, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), null));
            Assert.Throws<ArgumentOutOfRangeException>(() => ClipboardExitHelper.ClearWithRetry(clip, TimeSpan.FromSeconds(1), TimeSpan.Zero, wait));
        }

        // ---- decision 2: dialog counter ----

        [Test]
        public void DialogService_IsShowingDialog_TrueOnlyWhileShowing_EvenOnException()
        {
            var service = new DialogService();
            Assert.That(service.IsShowingDialog, Is.False);
            bool inside = false;
            Assert.That(service.Track(() => { inside = service.IsShowingDialog; return 7; }), Is.EqualTo(7));
            Assert.That(inside, Is.True);
            Assert.That(service.IsShowingDialog, Is.False);

            bool nested = false;
            service.Track(() => service.Track(() => { nested = service.IsShowingDialog; return 0; }));
            Assert.That(nested, Is.True);
            Assert.That(service.IsShowingDialog, Is.False);

            Assert.Throws<InvalidOperationException>(() => service.Track<int>(() => { throw new InvalidOperationException(); }));
            Assert.That(service.IsShowingDialog, Is.False);
        }

        // ---- MainWindow wiring (decisions 6, 11, 12) ----

        [Test]
        public void LockShortcut_CtrlL_Locks_OthersIgnored()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Assert.That(MainWindow.HandleLockShortcut(shell, Key.L, ModifierKeys.Control | ModifierKeys.Alt), Is.False,
                "AltGr+L types a character on some layouts");
            Assert.That(MainWindow.HandleLockShortcut(shell, Key.K, ModifierKeys.Control), Is.False);
            Assert.That(MainWindow.HandleLockShortcut(shell, Key.L, ModifierKeys.None), Is.False);
            Assert.That(shell.Editor, Is.Not.Null);

            Assert.That(MainWindow.HandleLockShortcut(shell, Key.L, ModifierKeys.Control), Is.True);
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            Assert.That(MainWindow.HandleLockShortcut(shell, Key.L, ModifierKeys.Control), Is.True, "no-op while locked");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_MouseInput_RecordsActivity()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            var window = new MainWindow { DataContext = shell };
            try
            {
                Advance(TimeSpan.FromMinutes(4));
                window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseDownEvent
                });
                Advance(TimeSpan.FromMinutes(4));
                shell.Tick();
                Assert.That(shell.Editor, Is.Not.Null, "mouse down counts as activity");

                window.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                });
                Advance(TimeSpan.FromMinutes(4));
                shell.Tick();
                Assert.That(shell.Editor, Is.Not.Null, "mouse wheel counts as activity");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_MouseMove_OnlyARealMoveCounts()
        {
            // WPF raises synthetic mouse moves when the layout changes under a resting cursor (e.g. the countdown text).
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var window = new MainWindow { DataContext = NewShell() };
            try
            {
                Assert.That(window.IsNewMousePosition(new Point(10, 10)), Is.True);
                Assert.That(window.IsNewMousePosition(new Point(10, 10)), Is.False);
                Assert.That(window.IsNewMousePosition(new Point(11, 10)), Is.True);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_Close_SaveFails_StaysOpen_ThenClosesAfterFix()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            var window = new MainWindow { DataContext = shell };
            bool closed = false;
            window.Closed += (s, e) => closed = true;
            Edit(shell, "edited");
            MakeFileReadOnly();

            window.Close();

            Assert.That(closed, Is.False, "the close choice defaulted to Cancel");
            Assert.That(_dialogs.LastCloseChoiceTitle, Is.EqualTo(CouldNotSaveTitle));
            Assert.That(shell.Editor, Is.Not.Null);

            MakeFileWritable();
            window.Close();
            Assert.That(closed, Is.True);
            Assert.That(NoteText(NotebookPath, "Ledger"), Is.EqualTo("edited"));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_Close_RetriesClipboardClearWithPumpingWait()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            var window = new MainWindow { DataContext = shell };
            bool closed = false;
            window.Closed += (s, e) => closed = true;
            _clipboard.OwnsClipboardContent = true;
            _clipboard.ClearsBeforeRelease = 2; // TryClose's clear fails, the first retry fails, the second succeeds

            var watch = System.Diagnostics.Stopwatch.StartNew();
            window.Close();
            watch.Stop();

            Assert.That(closed, Is.True);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(3));
            Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400)), "waited between attempts");
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_Closing_TryCloseThrows_CancelsWithoutRethrow_ReportsAsync()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            Edit(shell, "edited");
            nb.Dispose(); // dirty but disposed: the save inside TryClose throws ObjectDisposedException
            var window = new MainWindow { DataContext = shell };
            bool closed = false;
            window.Closed += (s, e) => closed = true;

            var args = new System.ComponentModel.CancelEventArgs();
            Assert.DoesNotThrow(() => window.HandleClosing(args));
            Assert.That(args.Cancel, Is.True);
            Assert.That(_dialogs.Calls, Is.Empty, "reported asynchronously, after the close was cancelled");

            PumpDispatcher();
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowError" }));
            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Unexpected error"));
            Assert.That(_dialogs.LastErrorMessage, Is.Not.Empty);

            // Through the real Closing event as well: no exception escapes and the window stays.
            Assert.DoesNotThrow(() => window.Close());
            Assert.That(closed, Is.False);
            Assert.That(shell.Editor, Is.Not.Null);

            // Clean up: without a shell the window closes.
            window.DataContext = null;
            window.Close();
            Assert.That(closed, Is.True);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_Closing_AfterSessionEndAllowed_SingleClipboardClear_NoRetryLoop()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            var window = new MainWindow { DataContext = shell };
            _clipboard.OwnsClipboardContent = true;
            _clipboard.NeverRelease = true;
            window.SessionEndAllowed = true;

            var args = new System.ComponentModel.CancelEventArgs();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            window.HandleClosing(args);
            watch.Stop();

            Assert.That(args.Cancel, Is.False);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(2), "TryClose's clear plus one more, no retry loop");
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(400)), "logoff is not delayed");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_Closing_WithoutSessionEnd_RunsRetryLoop()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            var window = new MainWindow { DataContext = shell };
            _clipboard.OwnsClipboardContent = true;
            _clipboard.ClearsBeforeRelease = 1;

            var args = new System.ComponentModel.CancelEventArgs();
            window.HandleClosing(args);

            Assert.That(args.Cancel, Is.False);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(2), "the retry loop ran");
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
        }

        // ---- fix round 2: teardown is exception-safe (F2) ----

        /// <summary>Makes EditorViewModel.ClearForLock throw (its SelectedNote change notifies this handler).</summary>
        private static void BreakClearForLock(EditorViewModel editor)
        {
            editor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "SelectedNote") throw new InvalidOperationException("teardown failure");
            };
        }

        [Test]
        public void TryClose_TeardownThrows_EditorHidden_NotebookDisposed_NoResurrection()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            BreakClearForLock(shell.Editor);

            Assert.Throws<InvalidOperationException>(() => shell.TryClose());

            Assert.That(shell.CurrentView, Is.Not.InstanceOf<EditorViewModel>());
            Assert.That(shell.Editor, Is.Null);
            AssertDisposed(_store, nb);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1), "the clipboard is still cleared");

            shell.Lock();
            shell.Tick();
            Assert.That(shell.CurrentView, Is.Not.InstanceOf<EditorViewModel>());
            Assert.That(shell.TryClose(), Is.True, "a later close succeeds");
            Assert.That(shell.CurrentView, Is.Not.InstanceOf<EditorViewModel>());
        }

        [Test]
        public void TryClose_ShellPropertyChangedThrows_TeardownStillCompletes()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            shell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Editor") throw new InvalidOperationException("listener failure");
            };

            Assert.Throws<InvalidOperationException>(() => shell.TryClose());

            Assert.That(shell.CurrentView, Is.Not.InstanceOf<EditorViewModel>());
            Assert.That(shell.Editor, Is.Null);
            AssertDisposed(_store, nb);
        }

        [Test]
        public void Lock_TeardownThrows_StillShowsUnlock_Disposes_ClearsClipboard()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            BreakClearForLock(shell.Editor);

            Assert.Throws<InvalidOperationException>(() => shell.Lock());

            var unlock = shell.CurrentView as UnlockViewModel;
            Assert.That(unlock, Is.Not.Null);
            Assert.That(unlock.HasHeldNotebook, Is.False);
            Assert.That(shell.Editor, Is.Null);
            AssertDisposed(_store, nb);
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
            Assert.That(shell.LockCountdownText, Is.EqualTo(""));

            object page = shell.CurrentView;
            shell.Lock();
            shell.Tick();
            Assert.That(shell.CurrentView, Is.SameAs(page));
        }

        [Test]
        public void HeldLock_TeardownThrows_StillShowsHeldUnlock_KeepsNotebook()
        {
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            string id = Edit(shell, "unsaved edit");
            MakeFileReadOnly();
            BreakClearForLock(shell.Editor);

            Assert.Throws<InvalidOperationException>(() => shell.Lock());

            var unlock = shell.CurrentView as UnlockViewModel;
            Assert.That(unlock, Is.Not.Null);
            Assert.That(unlock.HasHeldNotebook, Is.True);
            Assert.That(unlock.HeldWarning, Does.StartWith(HeldPrefix));
            Assert.That(nb.FindNote(id).Text, Is.EqualTo("unsaved edit"), "never disposed: the changes are held");
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.IoError));
            Assert.That(_clipboard.ClearCount, Is.EqualTo(1));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_Closing_TeardownThrows_CancelsWithBlankView_SecondCloseCloses()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            BreakClearForLock(shell.Editor);
            var window = new MainWindow { DataContext = shell };
            bool closed = false;
            window.Closed += (s, e) => closed = true;

            Assert.DoesNotThrow(() => window.Close());

            Assert.That(closed, Is.False);
            Assert.That(shell.CurrentView, Is.Not.InstanceOf<EditorViewModel>());
            AssertDisposed(_store, nb);
            PumpDispatcher();
            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Unexpected error"));

            window.Close();
            Assert.That(closed, Is.True);
        }

        // ---- fix round 2: no session end while the window is mid-close (F1) ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SessionEnding_WhileCloseInProgress_Cancels_WithoutConsultingShell()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            string id;
            ShellViewModel shell = LockedWithHeldNotebook(out nb, out id);
            var window = new MainWindow { DataContext = shell };
            bool closed = false;
            window.Closed += (s, e) => closed = true;
            bool? inProgress = null, allowed = null, reentrantCancel = null;
            bool consulted = false;
            _dialogs.Confirms.Enqueue(false); // keep the held notebook: the close is refused
            _dialogs.OnConfirm = () =>
            {
                _dialogs.OnConfirm = null; // one shot: never recurse if a nested close reached Confirm again
                // WM_QUERYENDSESSION / a second WM_CLOSE arriving inside the Confirm dialog's modal loop.
                inProgress = window.IsCloseInProgress;
                allowed = LockPad.App.App.AllowSessionEnd(window, () => { consulted = true; return true; });
                var again = new System.ComponentModel.CancelEventArgs();
                window.HandleClosing(again);
                reentrantCancel = again.Cancel;
            };

            var args = new System.ComponentModel.CancelEventArgs();
            window.HandleClosing(args);

            Assert.That(inProgress, Is.True);
            Assert.That(allowed, Is.False, "session end cancelled while the window is mid-close");
            Assert.That(consulted, Is.False, "PrepareForSessionEnd is not consulted");
            Assert.That(window.SessionEndAllowed, Is.False);
            Assert.That(reentrantCancel, Is.True, "a second close inside the first is refused");
            Assert.That(_dialogs.Calls.Count(c => c == "ConfirmDefaultNo"), Is.EqualTo(1));
            Assert.That(args.Cancel, Is.True);
            Assert.That(window.IsCloseInProgress, Is.False, "cleared when the close handling ends");
            Assert.That(closed, Is.False);

            // Clean up.
            window.DataContext = null;
            window.Close();
            Assert.That(closed, Is.True);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void AllowSessionEnd_NotClosing_FollowsShellDecision_SetsFlagOnlyWhenAllowed()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var window = new MainWindow { DataContext = NewShell() };
            try
            {
                Assert.That(window.IsCloseInProgress, Is.False);
                Assert.That(LockPad.App.App.AllowSessionEnd(window, () => false), Is.False);
                Assert.That(window.SessionEndAllowed, Is.False);
                Assert.That(LockPad.App.App.AllowSessionEnd(window, () => { throw new InvalidOperationException(); }), Is.False,
                    "an unknown state never risks unsaved notes");
                Assert.That(window.SessionEndAllowed, Is.False);
                Assert.That(LockPad.App.App.AllowSessionEnd(window, () => true), Is.True);
                Assert.That(window.SessionEndAllowed, Is.True);
                Assert.That(LockPad.App.App.AllowSessionEnd(null, () => true), Is.True, "no window yet");
            }
            finally
            {
                window.Close();
            }
        }

        private static void PumpDispatcher()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_StatusBar_ShowsLockCountdown()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Notebook nb;
            ShellViewModel shell = OpenShell(out nb);
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.Tick();
                window.Measure(new Size(900, 600));
                window.Arrange(new Rect(0, 0, 900, 600));
                window.UpdateLayout();
                var item = window.FindName("LockCountdownItem") as System.Windows.Controls.Primitives.StatusBarItem;
                Assert.That(item, Is.Not.Null);
                System.Windows.Data.Binding binding = System.Windows.Data.BindingOperations.GetBinding(
                    item, System.Windows.Controls.ContentControl.ContentProperty);
                Assert.That(binding, Is.Not.Null);
                Assert.That(binding.Path.Path, Is.EqualTo("LockCountdownText"));
            }
            finally
            {
                window.Close();
            }
        }
    }
}

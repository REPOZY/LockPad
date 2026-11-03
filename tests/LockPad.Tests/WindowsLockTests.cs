using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LockPad.App;
using LockPad.App.ViewModels;
using LockPad.Core;
using Microsoft.Win32;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// User decision 2026-10-19: LockPad always locks when Windows locks (session lock, user switch, remote disconnect)
    /// and before sleep or hibernate, exactly like Ctrl+L. No real SystemEvents are raised: the shell entry points are
    /// called directly, and the window's event mapping, dispatching and subscription go through seams.
    /// </summary>
    [TestFixture]
    public class WindowsLockTests
    {
        private TempDir _dir;
        private FakeDialogService _dialogs;
        private FakeClipboardService _clipboard;
        private NotebookStore _store;
        private List<Notebook> _notebooks;

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
            _clipboard = new FakeClipboardService();
            _store = new NotebookStore();
            _notebooks = new List<Notebook>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string f in Directory.GetFiles(_dir.Path)) File.SetAttributes(f, FileAttributes.Normal);
            foreach (Notebook nb in _notebooks) nb.Dispose();
            _dir.Dispose();
        }

        /// <summary>The notebook file of the last <see cref="OpenShell"/>.</summary>
        private string NotebookPath { get; set; }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }

        /// <summary>A shell showing a saved notebook with one note "Ledger" = "original" (idle timer far from due).</summary>
        private ShellViewModel OpenShell(string fileName = "nb.kdbx")
        {
            NotebookPath = _dir.File(fileName);
            using (new NotebookStore().Create(NotebookPath, Pw(), TestKdf.Fast())) { }
            Notebook nb = _store.Open(NotebookPath, Pw());
            _notebooks.Add(nb);
            Note n = nb.AddNote("Ledger");
            nb.UpdateNote(n.Id, "Ledger", "original");
            Assert.That(_store.Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));

            var shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                _clipboard, new FakeClock(), TestKdf.Fast);
            DateTime wall = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
            shell.WallClock = () => wall;
            shell.ShowEditor(nb);
            shell.Editor.BeginBusy = () => new NoopDisposable(); // saves need no WPF input system
            return shell;
        }

        private static void Edit(ShellViewModel shell, string text)
        {
            EditorViewModel e = shell.Editor;
            e.SelectedNote = e.VisibleNotes.First(i => i.DisplayTitle == "Ledger");
            e.EditorText = text;
        }

        private string SavedText()
        {
            using (Notebook nb = new NotebookStore().Open(NotebookPath, Pw()))
                return nb.Notes.Single(n => n.Title == "Ledger").Text;
        }

        private static UnlockViewModel UnlockPage(ShellViewModel shell)
        {
            var unlock = shell.CurrentView as UnlockViewModel;
            Assert.That(unlock, Is.Not.Null, "the unlock page is shown");
            return unlock;
        }

        // ---- shell ----

        [Test]
        public void WindowsLocked_Dirty_SavesAndLocks()
        {
            ShellViewModel shell = OpenShell();
            Edit(shell, "edited before Win+L");
            _clipboard.OwnsClipboardContent = true;

            shell.OnWindowsLocked();

            Assert.That(shell.Editor, Is.Null);
            Assert.That(UnlockPage(shell).HasHeldNotebook, Is.False);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
            Assert.That(SavedText(), Is.EqualTo("edited before Win+L"));
            Assert.That(_dialogs.Calls, Is.Empty, "no dialog: the user is away");
        }

        [Test]
        public void WindowsLocked_SaveFails_Holds()
        {
            ShellViewModel shell = OpenShell();
            Edit(shell, "kept in memory");
            File.SetAttributes(NotebookPath, FileAttributes.ReadOnly);

            shell.OnWindowsLocked();

            Assert.That(shell.Editor, Is.Null);
            UnlockViewModel unlock = UnlockPage(shell);
            Assert.That(unlock.HasHeldNotebook, Is.True);
            Assert.That(unlock.HeldWarning, Does.StartWith(ShellViewModel.HeldWarningPrefix));
            Assert.That(SavedText(), Is.EqualTo("original"));
        }

        [Test]
        public void WindowsLocked_DialogOpen_ObscuresAndClears_LockFollowsWhenDialogCloses()
        {
            ShellViewModel shell = OpenShell();
            Edit(shell, "edited, then a dialog");
            EditorViewModel editor = shell.Editor;
            _clipboard.OwnsClipboardContent = true;
            _dialogs.IsShowingDialog = true;

            shell.OnWindowsLocked();

            Assert.That(shell.Editor, Is.SameAs(editor), "no teardown under the dialog");
            Assert.That(editor.IsObscured, Is.True);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
            shell.Tick();
            Assert.That(shell.Editor, Is.SameAs(editor), "still deferred");

            _dialogs.IsShowingDialog = false;
            shell.Tick();

            Assert.That(shell.Editor, Is.Null);
            Assert.That(UnlockPage(shell).HasHeldNotebook, Is.False);
            Assert.That(SavedText(), Is.EqualTo("edited, then a dialog"));
        }

        [Test]
        public void WindowsLocked_NoNotebook_AlreadyLocked_OrHeld_IsANoOp()
        {
            var shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                _clipboard, new FakeClock(), TestKdf.Fast);
            shell.ShowUnlock();
            object page = shell.CurrentView;
            shell.OnWindowsLocked();
            shell.OnSuspending();
            Assert.That(shell.CurrentView, Is.SameAs(page), "unlock page, no notebook");

            shell.ShowCreate();
            page = shell.CurrentView;
            shell.OnWindowsLocked();
            Assert.That(shell.CurrentView, Is.SameAs(page), "create page");

            ShellViewModel open = OpenShell("locked.kdbx");
            open.Lock();
            page = open.CurrentView;
            open.OnWindowsLocked();
            Assert.That(open.CurrentView, Is.SameAs(page), "already locked");

            ShellViewModel held = OpenShell("held.kdbx");
            Edit(held, "held");
            File.SetAttributes(NotebookPath, FileAttributes.ReadOnly);
            held.Lock();
            page = held.CurrentView;
            Assert.That(((UnlockViewModel)page).HasHeldNotebook, Is.True);
            held.OnWindowsLocked();
            held.OnSuspending();
            Assert.That(held.CurrentView, Is.SameAs(page), "the held screen is already locked");
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void Suspending_ObscuresAndClearsAtOnce_TheQueuedLockThenSavesAndLocks()
        {
            ShellViewModel shell = OpenShell();
            Edit(shell, "edited before sleep");
            EditorViewModel editor = shell.Editor;
            _clipboard.OwnsClipboardContent = true;

            shell.OnSuspending(); // the synchronous step

            Assert.That(editor.IsObscured, Is.True);
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
            Assert.That(shell.Editor, Is.SameAs(editor), "the full lock (with its save) is queued, not run here");
            Assert.That(SavedText(), Is.EqualTo("original"));

            shell.OnWindowsLocked(); // the queued step

            Assert.That(shell.Editor, Is.Null);
            Assert.That(UnlockPage(shell).HasHeldNotebook, Is.False);
            Assert.That(SavedText(), Is.EqualTo("edited before sleep"));
        }

        [Test]
        public void Suspending_QueuedLockCutOffBySleep_FirstTickAfterResumeLocks()
        {
            ShellViewModel shell = OpenShell();
            Edit(shell, "edited before sleep");
            shell.OnSuspending();

            shell.Tick(); // the queued lock never ran; the obscured editor is due

            Assert.That(shell.Editor, Is.Null);
            Assert.That(SavedText(), Is.EqualTo("edited before sleep"));
        }

        // ---- R1 (red-team M1): a Windows lock or sleep during an unlock or create is not lost ----

        private const string Strong = "q7#Vt9!mZ2@xLp4$Rw8&";

        private ShellViewModel NewShell(Func<KeePassLib.Cryptography.KeyDerivation.KdfParameters> kdf = null)
        {
            var shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                _clipboard, new FakeClock(), kdf ?? TestKdf.Fast);
            DateTime wall = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
            shell.WallClock = () => wall;
            return shell;
        }

        /// <summary>A saved notebook file with "Ledger" = "secret text", not opened.</summary>
        private string CreateFile(string name)
        {
            string path = _dir.File(name);
            using (Notebook nb = new NotebookStore().Create(path, Pw(), TestKdf.Fast()))
            {
                Note n = nb.AddNote("Ledger");
                nb.UpdateNote(n.Id, "Ledger", "secret text");
                Assert.That(new NotebookStore().Save(nb, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            return path;
        }

        private static async System.Threading.Tasks.Task Unlock(UnlockViewModel page, string password)
        {
            using (var ss = UnlockViewModelTests.Secure(password)) await page.UnlockAsync(ss);
        }

        /// <summary>Locked as if Ctrl+L had been pressed right after the editor appeared: the plain unlock page for that
        /// path, nothing held, the clipboard cleared, and nothing left that a Tick or a close could reveal.</summary>
        private void AssertLockedAt(ShellViewModel shell, object previousPage, string path)
        {
            Assert.That(shell.Editor, Is.Null, "the editor never opened");
            UnlockViewModel page = UnlockPage(shell);
            Assert.That(page, Is.Not.SameAs(previousPage), "a fresh unlock page");
            Assert.That(page.HasHeldNotebook, Is.False);
            Assert.That(page.NotebookPath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(_clipboard.OwnsClipboardContent, Is.False);
            shell.Tick();
            shell.OnResume();
            Assert.That(shell.Editor, Is.Null);
            Assert.That(shell.TryClose(), Is.True, "nothing held: closes without asking");
            Assert.That(_dialogs.Calls.Where(c => c != "ShowWarning"), Is.Empty);
        }

        [Test]
        public async System.Threading.Tasks.Task Unlock_WindowsLockWhileReadOnlyWarningIsUp_EndsLocked()
        {
            string backup = _dir.File("copy.kdbx.bak7");
            File.Copy(CreateFile("nb.kdbx"), backup);
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            var page = (UnlockViewModel)shell.CurrentView;
            page.NotebookPath = backup;
            _clipboard.OwnsClipboardContent = true;
            bool hookRan = false;
            _dialogs.OnWarning = () =>
            {
                hookRan = true;
                Assert.That(shell.Editor, Is.Null, "still the unlock page while the warning is up");
                shell.OnSuspending(); // Win+L or lid close while the "Read-only" warning is on screen
                shell.OnWindowsLocked();
            };

            await Unlock(page, "pw");

            Assert.That(hookRan, Is.True);
            AssertLockedAt(shell, page, backup);
        }

        [Test]
        public async System.Threading.Tasks.Task Unlock_SleepDuringTheKdf_EndsLocked()
        {
            string path = CreateFile("nb.kdbx");
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            var page = (UnlockViewModel)shell.CurrentView;
            page.NotebookPath = path;
            _clipboard.OwnsClipboardContent = true;
            page.RunInBackground = work =>
            {
                Assert.That(page.IsBusy, Is.True);
                shell.OnSuspending(); // the synchronous step, the queued lock, then the resume event
                shell.OnWindowsLocked();
                shell.OnResume();
                return System.Threading.Tasks.Task.Run(work);
            };

            await Unlock(page, "pw");

            AssertLockedAt(shell, page, path);
        }

        [Test]
        public async System.Threading.Tasks.Task Create_WindowsLockDuringTheKdf_EndsLocked_PathRemembered()
        {
            ShellViewModel shell = null;
            shell = NewShell(() =>
            {
                shell.OnWindowsLocked(); // inside the create's background work (KDF tuning, create, verify)
                return TestKdf.Fast();
            });
            shell.ShowCreate();
            var page = (CreateNotebookViewModel)shell.CurrentView;
            string path = _dir.File("new.kdbx");
            page.NotebookPath = path;

            using (var a = UnlockViewModelTests.Secure(Strong))
            using (var b = UnlockViewModelTests.Secure(Strong))
                await page.CreateAsync(a, b);

            Assert.That(File.Exists(path), Is.True, "the notebook was created");
            Assert.That(shell.Settings.LastNotebookPath, Is.EqualTo(Path.GetFullPath(path)), "C4: remembered");
            AssertLockedAt(shell, page, path);
            using (Notebook reopened = new NotebookStore().Open(path, new Credentials(Encoding.UTF8.GetBytes(Strong), null)))
                Assert.That(reopened.IsReadOnly, Is.False);
        }

        [Test]
        public async System.Threading.Tasks.Task Create_WindowsLockWhileKeyFileWarningIsUp_EndsLocked()
        {
            ShellViewModel shell = NewShell();
            shell.ShowCreate();
            var page = (CreateNotebookViewModel)shell.CurrentView;
            string path = _dir.File("new.kdbx");
            page.NotebookPath = path;
            page.KeyFilePath = _dir.File("new.keyx");
            page.CreateNewKeyFile = true;
            _dialogs.OnWarning = () => shell.OnWindowsLocked(); // while "Back up your key file" is on screen

            using (var a = UnlockViewModelTests.Secure(Strong))
            using (var b = UnlockViewModelTests.Secure(Strong))
                await page.CreateAsync(a, b);

            Assert.That(_dialogs.LastWarningTitle, Is.EqualTo("Back up your key file"));
            AssertLockedAt(shell, page, path);
        }

        [Test]
        public async System.Threading.Tasks.Task HeldUnlock_WindowsLockDuringTheMatch_StaysOnTheHeldPage()
        {
            ShellViewModel shell = OpenShell();
            Edit(shell, "held edit");
            File.SetAttributes(NotebookPath, FileAttributes.ReadOnly);
            shell.Lock();
            var held = UnlockPage(shell);
            Assert.That(held.HasHeldNotebook, Is.True);
            held.RunInBackground = work =>
            {
                shell.OnWindowsLocked();
                return System.Threading.Tasks.Task.Run(work);
            };

            await Unlock(held, "pw");

            Assert.That(shell.Editor, Is.Null, "the editor is not restored");
            Assert.That(shell.CurrentView, Is.SameAs(held), "still the held page");
            Assert.That(held.HasHeldNotebook, Is.True);

            held.RunInBackground = work => System.Threading.Tasks.Task.Run(work);
            await Unlock(held, "pw"); // the event belonged to the earlier attempt only
            Assert.That(shell.Editor, Is.Not.Null, "the held notebook was kept: a later unlock restores it");
            Assert.That(shell.Editor.EditorText, Is.EqualTo("held edit"));
        }

        [Test]
        public async System.Threading.Tasks.Task IdleUnlockPage_WindowsLock_LaterUnlockShowsTheEditor()
        {
            string path = CreateFile("nb.kdbx");
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            var page = (UnlockViewModel)shell.CurrentView;
            shell.OnWindowsLocked(); // nothing in flight
            shell.OnSuspending();
            page.NotebookPath = path;

            await Unlock(page, "pw");

            Assert.That(shell.Editor, Is.Not.Null);
            _notebooks.Add(shell.Editor.Notebook);
            Assert.That(shell.Editor.IsObscured, Is.False);
        }

        [Test]
        public async System.Threading.Tasks.Task FailedUnlockInFlight_ClearsThePendingLock_NextUnlockShowsTheEditor()
        {
            string path = CreateFile("nb.kdbx");
            ShellViewModel shell = NewShell();
            shell.ShowUnlock();
            var page = (UnlockViewModel)shell.CurrentView;
            page.NotebookPath = path;
            page.RunInBackground = work =>
            {
                shell.OnWindowsLocked();
                return System.Threading.Tasks.Task.Run(work);
            };
            await Unlock(page, "wrong");
            Assert.That(page.ErrorMessage, Is.EqualTo("Wrong password or key file."));

            page.RunInBackground = work => System.Threading.Tasks.Task.Run(work);
            await Unlock(page, "pw");

            Assert.That(shell.Editor, Is.Not.Null, "the event belonged to the failed attempt only");
            _notebooks.Add(shell.Editor.Notebook);
        }

        // ---- window: event mapping, dispatching, subscription ----

        [Test]
        public void SessionSwitch_OnlyLockAndDisconnectsLock()
        {
            var locking = new[]
            {
                SessionSwitchReason.SessionLock, SessionSwitchReason.ConsoleDisconnect, SessionSwitchReason.RemoteDisconnect
            };
            foreach (SessionSwitchReason reason in Enum.GetValues(typeof(SessionSwitchReason)))
            {
                SystemLockTrigger expected = locking.Contains(reason) ? SystemLockTrigger.Lock : SystemLockTrigger.None;
                Assert.That(MainWindow.TriggerForSessionSwitch(reason), Is.EqualTo(expected), reason.ToString());
            }
            Assert.That(MainWindow.TriggerForSessionSwitch(SessionSwitchReason.SessionUnlock), Is.EqualTo(SystemLockTrigger.None));
            Assert.That(MainWindow.TriggerForSessionSwitch(SessionSwitchReason.ConsoleConnect), Is.EqualTo(SystemLockTrigger.None));
            Assert.That(MainWindow.TriggerForSessionSwitch(SessionSwitchReason.SessionLogoff), Is.EqualTo(SystemLockTrigger.None),
                "logoff stays with the session-ending path");
        }

        [Test]
        public void PowerMode_SuspendLocks_ResumeAsBefore_StatusChangeNothing()
        {
            Assert.That(MainWindow.TriggerForPowerMode(PowerModes.Suspend), Is.EqualTo(SystemLockTrigger.Suspend));
            Assert.That(MainWindow.TriggerForPowerMode(PowerModes.Resume), Is.EqualTo(SystemLockTrigger.Resume));
            Assert.That(MainWindow.TriggerForPowerMode(PowerModes.StatusChange), Is.EqualTo(SystemLockTrigger.None));
        }

        [Test, Apartment(ApartmentState.STA)]
        public void Window_Suspend_ObscuresSynchronously_ThenQueuesTheLock()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            ShellViewModel shell = OpenShell();
            Edit(shell, "edited before sleep");
            _clipboard.OwnsClipboardContent = true;
            var window = new MainWindow { DataContext = shell };
            var posted = new List<Action>();
            window.PostToUi = a => posted.Add(a);
            try
            {
                window.HandleSystemTrigger(SystemLockTrigger.Suspend);

                Assert.That(shell.Editor, Is.Not.Null);
                Assert.That(shell.Editor.IsObscured, Is.True, "obscured before the handler returned");
                Assert.That(_clipboard.OwnsClipboardContent, Is.False);
                Assert.That(posted.Count, Is.EqualTo(1), "the full lock is queued");

                posted[0]();
                Assert.That(shell.Editor, Is.Null);
                Assert.That(SavedText(), Is.EqualTo("edited before sleep"));
            }
            finally
            {
                window.DataContext = null;
                window.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void Window_LockQueuesTheShellLock_ResumeQueuesResume_NoneDoesNothing()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            ShellViewModel shell = OpenShell();
            var window = new MainWindow { DataContext = shell };
            var posted = new List<Action>();
            window.PostToUi = a => posted.Add(a);
            try
            {
                window.HandleSystemTrigger(SystemLockTrigger.None);
                Assert.That(posted, Is.Empty);

                window.HandleSystemTrigger(SystemLockTrigger.Resume);
                Assert.That(posted.Count, Is.EqualTo(1));
                posted[0]();
                Assert.That(shell.Editor, Is.Not.Null, "resume locks only when the idle timeout passed");

                window.HandleSystemTrigger(SystemLockTrigger.Lock);
                Assert.That(shell.Editor, Is.Not.Null, "queued, not run on the event's thread");
                Assert.That(posted.Count, Is.EqualTo(2));
                posted[1]();
                Assert.That(shell.Editor, Is.Null);
            }
            finally
            {
                window.DataContext = null;
                window.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void Window_SubscribesOnce_UnsubscribesOnClose()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var window = new MainWindow();
            var calls = new List<bool>();
            window.SystemEventsSubscriber = (w, subscribe) =>
            {
                Assert.That(w, Is.SameAs(window));
                calls.Add(subscribe);
            };
            try
            {
                window.AttachSystemEvents();
                window.AttachSystemEvents(); // Loaded can be raised more than once
                Assert.That(calls, Is.EqualTo(new[] { true }));

                window.DetachSystemEvents();
                window.DetachSystemEvents();
                Assert.That(calls, Is.EqualTo(new[] { true, false }));

                window.AttachSystemEvents();
                Assert.That(calls, Is.EqualTo(new[] { true, false, true }));
            }
            finally
            {
                window.DetachSystemEvents();
                window.Close();
            }
            Assert.That(calls, Is.EqualTo(new[] { true, false, true, false }));
        }
    }
}

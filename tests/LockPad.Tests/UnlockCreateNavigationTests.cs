using System;
using System.Threading;
using System.Windows;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class UnlockCreateNavigationTests
    {
        [Test]
        public void Shell_ShowUnlock_ShowCreate_Cancel_Navigates()
        {
            using (var dir = new TempDir())
            {
                var dialogs = new FakeDialogService();
                var shell = new ShellViewModel(new NotebookStore(), new SettingsStore(dir.File("settings")),
                    new AppSettings(), dialogs, null, new FakeClock(), TestKdf.Fast);

                shell.ShowUnlock();
                var unlock = shell.CurrentView as UnlockViewModel;
                Assert.That(unlock, Is.Not.Null);
                Assert.That(unlock.HeldWarning, Is.Null);

                unlock.NewNotebookCommand.Execute(null);
                var create = shell.CurrentView as CreateNotebookViewModel;
                Assert.That(create, Is.Not.Null);

                create.CancelCommand.Execute(null);
                Assert.That(shell.CurrentView, Is.InstanceOf<UnlockViewModel>());

                shell.ShowCreate();
                Assert.That(shell.CurrentView, Is.InstanceOf<CreateNotebookViewModel>());
            }
        }

        /// <summary>C4 (review M1): creating a notebook remembers its path, as unlocking does, so the next start offers it.</summary>
        [Test]
        public async System.Threading.Tasks.Task Shell_CreateNotebook_RemembersPath_AndSavesSettings()
        {
            using (var dir = new TempDir())
            {
                var settingsStore = new SettingsStore(dir.File("settings"));
                var settings = new AppSettings { LastNotebookPath = dir.File("older.kdbx") };
                var shell = new ShellViewModel(new NotebookStore(), settingsStore, settings, new FakeDialogService(),
                    new FakeClipboardService(), new FakeClock(), TestKdf.Fast);

                shell.ShowCreate();
                var create = (CreateNotebookViewModel)shell.CurrentView;
                string path = dir.File("new.kdbx");
                create.NotebookPath = path;
                const string strong = "q7#Vt9!mZ2@xLp4$Rw8&";
                using (System.Security.SecureString a = UnlockViewModelTests.Secure(strong))
                using (System.Security.SecureString b = UnlockViewModelTests.Secure(strong))
                    await create.CreateAsync(a, b);

                Assert.That(create.ErrorMessage, Is.Null);
                Assert.That(shell.Editor, Is.Not.Null);
                Assert.That(shell.Settings, Is.SameAs(settings), "mutated in place");
                Assert.That(settings.LastNotebookPath, Is.EqualTo(System.IO.Path.GetFullPath(path)));
                Assert.That(settingsStore.Load().LastNotebookPath, Is.EqualTo(System.IO.Path.GetFullPath(path)),
                    "the settings store was saved");

                shell.Lock(); // clean: disposes the notebook and shows the unlock page
                var unlock = (UnlockViewModel)shell.CurrentView;
                Assert.That(unlock.NotebookPath, Is.EqualTo(System.IO.Path.GetFullPath(path)));
            }
        }

        private static string CreateNotebook(TempDir dir, string name)
        {
            string p = dir.File(name);
            using (new NotebookStore().Create(p, new Credentials(System.Text.Encoding.UTF8.GetBytes("pw"), null), TestKdf.Fast())) { }
            return p;
        }

        /// <summary>Bug fix 2026-10-21: the command-line notebook path pre-fills the first unlock page only.</summary>
        [Test]
        public void Shell_StartupPath_FirstUnlockPageOnly_NothingOpened()
        {
            using (var dir = new TempDir())
            {
                string last = CreateNotebook(dir, "last.kdbx");
                string arg = CreateNotebook(dir, "arg.kdbx");
                var settings = new AppSettings { LastNotebookPath = last };
                var shell = new ShellViewModel(new NotebookStore(), new SettingsStore(dir.File("settings")), settings,
                    new FakeDialogService(), new FakeClipboardService(), new FakeClock(), TestKdf.Fast, arg);

                shell.ShowUnlock();
                var first = (UnlockViewModel)shell.CurrentView;
                Assert.That(first.NotebookPath, Is.EqualTo(arg));
                Assert.That(shell.Editor, Is.Null, "pre-fill only: nothing is opened");
                Assert.That(settings.LastNotebookPath, Is.EqualTo(last), "not written because of the argument");

                shell.ShowUnlock();
                Assert.That(((UnlockViewModel)shell.CurrentView).NotebookPath, Is.EqualTo(last), "a later page");
            }
        }

        [Test]
        public void Shell_StartupPath_NotUsedByTheUnlockPageAfterALock()
        {
            using (var dir = new TempDir())
            {
                string last = CreateNotebook(dir, "last.kdbx");
                string arg = CreateNotebook(dir, "arg.kdbx");
                var settings = new AppSettings { LastNotebookPath = last };
                var store = new NotebookStore();
                var shell = new ShellViewModel(store, new SettingsStore(dir.File("settings")), settings,
                    new FakeDialogService(), new FakeClipboardService(), new FakeClock(), TestKdf.Fast, arg);
                shell.ShowUnlock();
                Assert.That(((UnlockViewModel)shell.CurrentView).NotebookPath, Is.EqualTo(arg));

                shell.ShowEditor(store.Open(last, new Credentials(System.Text.Encoding.UTF8.GetBytes("pw"), null)));
                shell.Lock(); // clean: disposes the notebook and shows a new unlock page

                var after = (UnlockViewModel)shell.CurrentView;
                Assert.That(after.NotebookPath, Is.EqualTo(last));
                Assert.That(after.ErrorMessage, Is.Null);
            }
        }

        [Test]
        public void Shell_RejectedStartupPath_FirstPageShowsTheError_LaterPagesDoNot()
        {
            using (var dir = new TempDir())
            {
                string last = CreateNotebook(dir, "last.kdbx");
                string missing = dir.File("missing.kdbx");
                var settings = new AppSettings { LastNotebookPath = last };
                var shell = new ShellViewModel(new NotebookStore(), new SettingsStore(dir.File("settings")), settings,
                    new FakeDialogService(), new FakeClipboardService(), new FakeClock(), TestKdf.Fast, missing);

                shell.ShowUnlock();
                var first = (UnlockViewModel)shell.CurrentView;
                Assert.That(first.NotebookPath, Is.EqualTo(""));
                Assert.That(first.ErrorMessage, Is.EqualTo("The file \"" + missing
                    + "\" cannot be opened. Notebooks must be existing files on a local drive."));

                shell.ShowUnlock();
                var later = (UnlockViewModel)shell.CurrentView;
                Assert.That(later.NotebookPath, Is.EqualTo(last));
                Assert.That(later.ErrorMessage, Is.Null);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Views_InstantiateWithoutXamlErrors()
        {
            // Loads the compiled XAML (no window is shown); catches markup errors the compiler does not.
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            GC.KeepAlive(Application.Current);
            using (var dir = new TempDir())
            {
                var dialogs = new FakeDialogService();
                var unlockVm = new UnlockViewModel(new NotebookStore(), new SettingsStore(dir.File("s")),
                    new AppSettings(), dialogs, nb => { }, () => { }, null, "held");
                var createVm = new CreateNotebookViewModel(new NotebookStore(), dialogs, TestKdf.Fast, nb => { }, () => { });

                var unlock = new UnlockView { DataContext = unlockVm };
                var create = new CreateNotebookView { DataContext = createVm };
                foreach (FrameworkElement view in new FrameworkElement[] { unlock, create })
                {
                    view.Measure(new Size(900, 600));
                    view.Arrange(new Rect(0, 0, 900, 600));
                    view.UpdateLayout();
                }
            }
        }
    }
}

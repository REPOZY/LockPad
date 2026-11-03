using System;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LockPad.App;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// C12 (user decision 2026-10-18): keyboard and mouse input in LockPad's own WPF dialogs (windows owned by the main
    /// window) counts as activity before the idle timeout expires, never after (SEC-H1). No window is shown: the owner's
    /// HWND is created with EnsureHandle so Owner can be set, and input events are raised directly.
    /// </summary>
    [TestFixture]
    public class DialogActivityTests
    {
        private TempDir _dir;
        private FakeDialogService _dialogs;
        private FakeClipboardService _clipboard;
        private FakeClock _clock;
        private DateTime _wall;
        private NotebookStore _store;
        private Notebook _nb;
        private ShellViewModel _shell;
        private MainWindow _main;

        [SetUp]
        public void SetUp()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
            _clipboard = new FakeClipboardService();
            _clock = new FakeClock();
            _wall = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
            _store = new NotebookStore();
            string p = _dir.File("nb.kdbx");
            using (_store.Create(p, Pw(), TestKdf.Fast())) { }
            _nb = _store.Open(p, Pw());
            _shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                _clipboard, _clock, TestKdf.Fast);
            _shell.WallClock = () => _wall;
            _shell.ShowEditor(_nb);
            _main = new MainWindow { DataContext = _shell };
            new WindowInteropHelper(_main).EnsureHandle(); // lets dialogs name it as Owner; nothing is shown
        }

        [TearDown]
        public void TearDown()
        {
            _main.DataContext = null;
            _main.Close();
            _nb.Dispose();
            _dir.Dispose();
            // Each test runs on its own STA thread and creates real (hidden) HWNDs there; shutting that thread's
            // dispatcher down lets the test AppDomain unload cleanly.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        private void Advance(TimeSpan t)
        {
            _clock.Advance(t);
            _wall = _wall + t;
        }

        private Window OwnedDialog(Window dialog)
        {
            dialog.Owner = _main;
            return dialog;
        }

        /// <summary>KeyEventArgs requires an input source, and a window that was never shown has none.</summary>
        private sealed class TestPresentationSource : PresentationSource
        {
            private System.Windows.Media.Visual _root;
            public override System.Windows.Media.Visual RootVisual { get { return _root; } set { _root = value; } }
            public override bool IsDisposed { get { return false; } }
            protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() { return null; }
        }

        private static readonly PresentationSource InputSource = new TestPresentationSource();

        private static void Key(Window w)
        {
            w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, InputSource, 0, System.Windows.Input.Key.A)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
        }

        private static void Click(Window w)
        {
            w.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent
            });
        }

        private static void Wheel(Window w)
        {
            w.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        }

        [Test, Apartment(ApartmentState.STA)]
        public void OwnedDialogInput_BeforeExpiry_PushesTheDeadlineOut()
        {
            var dialogs = new Window[]
            {
                OwnedDialog(new PromptWindow("Rename note", "Title:", "x")),
                OwnedDialog(new SettingsWindow(new SettingsViewModel(new AppSettings()))),
                OwnedDialog(new ConfirmDefaultNoWindow("t", "m")),
                OwnedDialog(new ConfirmWindow("t", "m")),
                OwnedDialog(new CloseChoiceWindow("t", "m")),
                OwnedDialog(new ShortcutsWindow()), // UX round 3, V2
                OwnedDialog(new AboutWindow(t => { })), // 2026-10-21
                OwnedDialog(new ChangePasswordWindow(new ChangePasswordViewModel(_nb, _dialogs, new PasswordBackOff(new SystemClock()))))
            };
            var inputs = new Action<Window>[] { Key, Click, Wheel };
            try
            {
                _dialogs.IsShowingDialog = true;
                Advance(TimeSpan.FromMinutes(4));
                foreach (Window dialog in dialogs)
                {
                    foreach (Action<Window> input in inputs)
                    {
                        input(dialog);
                        // 4 min after this input, 8 min after the previous one: expired unless this input reset it.
                        Advance(TimeSpan.FromMinutes(4));
                        _shell.Tick();
                        Assert.That(_shell.Editor.IsObscured, Is.False, dialog.GetType().Name + " " + input.Method.Name);
                    }
                }

                _dialogs.IsShowingDialog = false;
                _shell.Tick();
                Assert.That(_shell.Editor, Is.Not.Null, "the dialog input counted: no lock once it closes");
                Assert.That(_shell.Editor.IsObscured, Is.False);
            }
            finally
            {
                foreach (Window d in dialogs) d.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void OwnedDialogInput_AfterExpiry_ObscuresInsteadOfResetting_LocksWhenDialogCloses()
        {
            Window dialog = OwnedDialog(new PromptWindow("Rename note", "Title:", "x"));
            try
            {
                _clipboard.OwnsClipboardContent = true;
                _dialogs.IsShowingDialog = true;
                EditorViewModel editor = _shell.Editor;
                Advance(TimeSpan.FromMinutes(5)); // expired; no Tick yet

                Key(dialog);

                Assert.That(editor.IsObscured, Is.True, "late input never extends the session");
                Assert.That(_clipboard.OwnsClipboardContent, Is.False);
                Assert.That(_shell.Editor, Is.SameAs(editor), "no teardown under the dialog");

                Click(dialog);
                Wheel(dialog);
                _shell.Tick();
                Assert.That(editor.IsObscured, Is.True, "dialog input never un-obscures");

                _dialogs.IsShowingDialog = false;
                _shell.Tick();
                Assert.That(_shell.Editor, Is.Null, "the lock still happens when the dialog closes");
                Assert.That(_shell.CurrentView, Is.InstanceOf<UnlockViewModel>());
            }
            finally
            {
                dialog.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void OwnedDialogInput_AfterObscure_StaysObscured_LocksWhenDialogCloses()
        {
            Window dialog = OwnedDialog(new SettingsWindow(new SettingsViewModel(new AppSettings())));
            try
            {
                _dialogs.IsShowingDialog = true;
                EditorViewModel editor = _shell.Editor;
                Advance(TimeSpan.FromMinutes(5));
                _shell.Tick();
                Assert.That(editor.IsObscured, Is.True);

                for (int i = 0; i < 3; i++)
                {
                    Key(dialog);
                    Click(dialog);
                    Advance(TimeSpan.FromSeconds(30));
                    _shell.Tick();
                }
                Assert.That(editor.IsObscured, Is.True);

                _dialogs.IsShowingDialog = false;
                _shell.Tick();
                Assert.That(_shell.Editor, Is.Null);
            }
            finally
            {
                dialog.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void InputInAWindowNotOwnedByTheMainWindow_DoesNotCount()
        {
            var unowned = new PromptWindow("t", "l", "x");
            var otherOwner = new Window();
            new WindowInteropHelper(otherOwner).EnsureHandle();
            var ownedByOther = new PromptWindow("t", "l", "x") { Owner = otherOwner };
            try
            {
                Assert.That(MainWindow.OwningMainWindow(unowned), Is.Null);
                Assert.That(MainWindow.OwningMainWindow(ownedByOther), Is.Null);
                Assert.That(MainWindow.OwningMainWindow(_main), Is.Null, "the main window counts its own input");

                Advance(TimeSpan.FromMinutes(4));
                foreach (Window w in new Window[] { unowned, ownedByOther })
                {
                    Key(w);
                    Click(w);
                    Wheel(w);
                }
                Advance(TimeSpan.FromMinutes(1));
                _shell.Tick();
                Assert.That(_shell.Editor, Is.Null, "none of that input reset the timer");
            }
            finally
            {
                unowned.Close();
                ownedByOther.Close();
                otherOwner.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void NestedOwnedDialog_FindsTheMainWindow()
        {
            Window outer = OwnedDialog(new SettingsWindow(new SettingsViewModel(new AppSettings())));
            new WindowInteropHelper(outer).EnsureHandle();
            var inner = new ConfirmDefaultNoWindow("t", "m") { Owner = outer };
            try
            {
                Assert.That(MainWindow.OwningMainWindow(inner), Is.SameAs(_main));
            }
            finally
            {
                inner.Close();
                outer.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void OwnedDialogInput_NoDialogTracked_DoesNotCount()
        {
            // Every LockPad dialog is shown through DialogService, which raises IsShowingDialog. Without it the shell could
            // lock on this input, tearing down a view an owned window may still use, so the input is ignored.
            Window dialog = OwnedDialog(new PromptWindow("t", "l", "x"));
            try
            {
                Advance(TimeSpan.FromMinutes(4));
                Key(dialog);
                Advance(TimeSpan.FromMinutes(1));
                _shell.Tick();
                Assert.That(_shell.Editor, Is.Null);
            }
            finally
            {
                dialog.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void DialogMouseMove_OnlyARealMoveCounts_PerWindow()
        {
            Window a = OwnedDialog(new PromptWindow("t", "l", "x"));
            Window b = OwnedDialog(new PromptWindow("t", "l", "y"));
            try
            {
                Assert.That(MainWindow.IsNewDialogMousePosition(a, new Point(10, 10)), Is.True);
                Assert.That(MainWindow.IsNewDialogMousePosition(a, new Point(10, 10)), Is.False);
                Assert.That(MainWindow.IsNewDialogMousePosition(b, new Point(10, 10)), Is.True, "tracked per window");
                Assert.That(MainWindow.IsNewDialogMousePosition(a, new Point(11, 10)), Is.True);
            }
            finally
            {
                a.Close();
                b.Close();
            }
        }
    }
}

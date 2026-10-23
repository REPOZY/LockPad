using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LockPad.App;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// File > Change master password (user decisions 2026-10-19): the dialog, its validation, and the change itself run
    /// like Ctrl+S.
    /// </summary>
    [TestFixture]
    public class ChangePasswordTests
    {
        private const string Strong = "Tq8#vLm2!pZx9@Rw4$Kb";
        private const string SuccessMessage = "Your notebook and its backup file (.bak) now open only with the new "
            + "password. Copies you made yourself (for example on another USB stick, or older .bak.kept or .bak1-.bak3 "
            + "files) still open with the old password.";

        private TempDir _dir;
        private FakeDialogService _dialogs;
        private NotebookStore _store;
        private List<Notebook> _notebooks;
        private int _busyBegun, _busyEnded;

        [SetUp]
        public void SetUp()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
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

        private string P { get { return _dir.File("nb.kdbx"); } }

        private static Credentials Pw(string password = "pw", string keyFile = null)
        {
            return new Credentials(Encoding.UTF8.GetBytes(password), keyFile);
        }

        private static SecureString S(string value)
        {
            var s = new SecureString();
            foreach (char c in value) s.AppendChar(c);
            s.MakeReadOnly();
            return s;
        }

        /// <summary>A saved notebook under "pw" with one note, opened in a fresh session (so it starts clean).</summary>
        private Notebook NewNotebook(string keyFile = null)
        {
            using (Notebook created = new NotebookStore().Create(P, Pw("pw", keyFile), TestKdf.Fast()))
            {
                string id = created.AddNote("t").Id;
                created.UpdateNote(id, "t", "saved");
                Assert.That(new NotebookStore().Save(created, false).Status, Is.EqualTo(SaveStatus.Saved));
            }
            Notebook nb = _store.Open(P, Pw("pw", keyFile));
            _notebooks.Add(nb);
            return nb;
        }

        private EditorViewModel NewEditor(Notebook nb)
        {
            var vm = new EditorViewModel(nb, _store, _dialogs, new SettingsStore(_dir.File("settings")), new AppSettings());
            vm.BeginBusy = () => { _busyBegun++; return new ActionDisposable(() => _busyEnded++); };
            return vm;
        }

        private sealed class ActionDisposable : IDisposable
        {
            private readonly Action _onDispose;
            public ActionDisposable(Action onDispose) { _onDispose = onDispose; }
            public void Dispose() { _onDispose(); }
        }

        private int Count(string call)
        {
            return _dialogs.Calls.Count(c => c == call);
        }

        private static bool Opens(string path, string password)
        {
            try
            {
                using (new NotebookStore().Open(path, Pw(password))) return true;
            }
            catch (WrongCredentialsException)
            {
                return false;
            }
        }

        /// <summary>The dialog: the user types these and presses Change password; the dialog closes only on success.</summary>
        private void Answer(string current, string newPassword, string repeat)
        {
            _dialogs.OnShowChangePassword = vm => vm.TryAccept(S(current), S(newPassword), S(repeat));
        }

        // ---- the view model: validation, strength, the weak-password question ----

        [TestCase("", Strong, Strong, "Enter your current password.", ChangePasswordField.Current)]
        [TestCase("pw", "", "", "Enter a new password.", ChangePasswordField.New)]
        [TestCase("pw", "", Strong, "Enter a new password.", ChangePasswordField.New)]
        [TestCase("pw", Strong, Strong + "x", "The new passwords do not match.", ChangePasswordField.New)]
        [TestCase("wrong", Strong, Strong, "The current password is wrong. Try again in 1 s.", ChangePasswordField.Current)]
        [TestCase("pw", "pw", "pw", "The new password is the same as the current one.", ChangePasswordField.New)]
        public void Validation_ShowsTheMessage_AndTheDialogStaysOpen(string current, string newPassword, string repeat,
            string message, ChangePasswordField field)
        {
            Notebook nb = NewNotebook();
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, BackOff(new FakeClock(), new List<TimeSpan>(), Never)))
            {
                Assert.That(vm.TryAccept(S(current), S(newPassword), S(repeat)), Is.False, "the dialog stays open");
                Assert.That(vm.ErrorMessage, Is.EqualTo(message));
                Assert.That(vm.ErrorField, Is.EqualTo(field));
                Assert.That(vm.Current, Is.Null);
                Assert.That(vm.Replacement, Is.Null);
            }
            Assert.That(Count("ConfirmDefaultNo"), Is.EqualTo(0), "no weak-password question before the checks pass");
        }

        [Test]
        public void Validation_AfterAnError_ASuccessfulAttemptClearsTheMessage()
        {
            Notebook nb = NewNotebook();
            var clock = new FakeClock();
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, BackOff(clock, new List<TimeSpan>(), Never)))
            {
                Assert.That(vm.TryAccept(S("wrong"), S(Strong), S(Strong)), Is.False);
                clock.Advance(TimeSpan.FromSeconds(1)); // the back-off wait
                Assert.That(vm.TryAccept(S("pw"), S(Strong), S(Strong)), Is.True);
                Assert.That(vm.ErrorMessage, Is.Null);
                Assert.That(vm.ErrorField, Is.EqualTo(ChangePasswordField.None));
            }
        }

        [Test]
        public void WeakNewPassword_No_BackToTheDialog_NewBoxesClearedAndFocused()
        {
            Notebook nb = NewNotebook();
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff()))
            {
                Assert.That(vm.TryAccept(S("pw"), S("abc"), S("abc")), Is.False);
                Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo" }));
                Assert.That(_dialogs.LastConfirmTitle, Is.EqualTo("Weak password"));
                uint bits = PasswordStrength.EstimateBits(S("abc"));
                Assert.That(_dialogs.LastConfirmMessage, Is.EqualTo("This password is weak (" + bits
                    + " bits). Anyone who gets this file could guess it. Use it anyway?"), "the Create page's wording");
                Assert.That(vm.ErrorMessage, Is.Null);
                Assert.That(vm.ErrorField, Is.EqualTo(ChangePasswordField.New));
                Assert.That(vm.ClearErrorField, Is.True);
                Assert.That(vm.Replacement, Is.Null);
            }
        }

        [Test]
        public void WeakNewPassword_Yes_Accepted()
        {
            Notebook nb = NewNotebook();
            _dialogs.Confirms.Enqueue(true);
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff()))
            {
                Assert.That(vm.TryAccept(S("pw"), S("abc"), S("abc")), Is.True);
                Assert.That(vm.Replacement, Is.Not.Null);
                Assert.That(nb.CredentialsMatch(vm.Current), Is.True);
            }
        }

        [Test]
        public void StrongNewPassword_NoQuestion_Accepted()
        {
            Notebook nb = NewNotebook();
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff()))
            {
                Assert.That(vm.TryAccept(S("pw"), S(Strong), S(Strong)), Is.True);
                Assert.That(_dialogs.Calls, Is.Empty);
            }
        }

        [Test]
        public void WeakThreshold_IsTheCreatePages()
        {
            Notebook nb = NewNotebook();
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff()))
            {
                vm.UpdateStrength(S(Strong));
                Assert.That(vm.StrengthBits, Is.EqualTo(PasswordStrength.EstimateBits(S(Strong))));
                Assert.That(vm.Strength, Is.EqualTo(StrengthRating.Strong));
                Assert.That(vm.StrengthWarning, Is.Null);

                vm.UpdateStrength(S("abc"));
                Assert.That(vm.Strength, Is.EqualTo(StrengthRating.Weak));
                Assert.That(vm.StrengthWarning, Is.EqualTo("Weak password."));

                vm.UpdateStrength(S(""));
                Assert.That(vm.StrengthBits, Is.EqualTo(0));
                Assert.That(vm.StrengthWarning, Is.Null, "no warning before typing starts");
            }
        }

        [Test]
        public void KeyFileNotebook_TheKeyFileIsKept()
        {
            string key = _dir.File("key.keyx");
            Credentials.CreateKeyFile(key);
            Notebook nb = NewNotebook(key);
            Assert.That(nb.KeyFilePath, Is.EqualTo(key));
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff()))
            {
                Assert.That(vm.TryAccept(S("pw"), S(Strong), S(Strong)), Is.True, "the current password plus the kept key file");
                Assert.That(vm.Replacement.KeyFilePath, Is.EqualTo(key));
            }
        }

        /// <summary>Red team L1 / review B6, documented only (README): a different key file at the kept path makes the
        /// whole key differ, so the dialog reports the current password as wrong (and counts it). No data risk: the
        /// change is refused.</summary>
        [Test]
        public void KeyFileReplacedAtTheSamePath_ReportedAsAWrongCurrentPassword_Documented()
        {
            string key = _dir.File("key.keyx");
            Credentials.CreateKeyFile(key);
            Notebook nb = NewNotebook(key);
            File.Delete(key);
            Credentials.CreateKeyFile(key); // another stick under the same letter, or the file regenerated
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff()))
            {
                Assert.That(vm.TryAccept(S("pw"), S(Strong), S(Strong)), Is.False);
                Assert.That(vm.ErrorMessage, Does.StartWith("The current password is wrong."));
                Assert.That(vm.Replacement, Is.Null);
            }
        }

        [Test]
        public void KeyFileGone_Refused_WithTheReason()
        {
            string key = _dir.File("key.keyx");
            Credentials.CreateKeyFile(key);
            Notebook nb = NewNotebook(key);
            File.Delete(key);
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff()))
            {
                Assert.That(vm.TryAccept(S("pw"), S(Strong), S(Strong)), Is.False);
                Assert.That(vm.ErrorMessage, Does.StartWith("The key file could not be read: "));
                Assert.That(vm.Replacement, Is.Null);
            }
        }

        [Test]
        public void ViewModel_PasswordsNeverPassThroughAStringProperty()
        {
            PropertyInfo[] props = typeof(ChangePasswordViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Assert.That(props.Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).OrderBy(n => n),
                Is.EqualTo(new[] { "ErrorMessage", "StrengthWarning" }), "the only public strings are messages");
            Type[] secretTypes = { typeof(SecureString), typeof(char[]), typeof(byte[]), typeof(Credentials) };
            Assert.That(props.Where(p => secretTypes.Contains(p.PropertyType)).Select(p => p.Name), Is.Empty);
            MethodInfo accept = typeof(ChangePasswordViewModel).GetMethod("TryAccept");
            Assert.That(accept.GetParameters().Select(p => p.ParameterType),
                Is.EqualTo(new[] { typeof(SecureString), typeof(SecureString), typeof(SecureString) }));
        }

        [Test]
        public void ViewModel_Dispose_DisposesCredentialsNotHandedOver()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            Assert.That(vm.TryAccept(S("pw"), S(Strong), S(Strong)), Is.True);
            Credentials current = vm.Current, replacement = vm.Replacement;
            vm.Dispose();
            Assert.Throws<ObjectDisposedException>(() => GC.KeepAlive(current.CompositeKey));
            Assert.Throws<ObjectDisposedException>(() => GC.KeepAlive(replacement.CompositeKey));
        }

        // ---- wrong current password back-off (fix round 1, F1) ----

        private static PasswordBackOff BackOff(FakeClock clock, List<TimeSpan> waits, Func<Task> delay = null)
        {
            var b = new PasswordBackOff(clock);
            b.DelayAsync = t =>
            {
                waits.Add(t);
                return delay == null ? Task.FromResult(0) : delay();
            };
            return b;
        }

        private static Task Never()
        {
            return new TaskCompletionSource<bool>().Task;
        }

        /// <summary>A back-off on a clock that never moves, whose visible wait never ends.</summary>
        private static PasswordBackOff NewBackOff()
        {
            return BackOff(new FakeClock(), new List<TimeSpan>(), Never);
        }

        // ---- the screen locked while the dialog is open (fix round 2, X1; review B1) ----

        [Test]
        public void ObscuredWhileTheDialogIsOpen_ThenConfirmed_NoComparison_NoWrite_KeyUnchanged_NothingCounted()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            var waits = new List<TimeSpan>();
            editor.ChangePasswordBackOff = BackOff(new FakeClock(), waits, Never);
            byte[] main = File.ReadAllBytes(P);
            int writes = 0;
            _store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };
            bool wrongAccepted = true, rightAccepted = true;
            _dialogs.OnShowChangePassword = vm =>
            {
                editor.Obscure(); // the idle lock or Win+L while the dialog is open; someone else walks up
                Assert.That(vm.IsAbandoned, Is.True);
                wrongAccepted = vm.TryAccept(S("wrong"), S(Strong), S(Strong));
                rightAccepted = vm.TryAccept(S("pw"), S(Strong), S(Strong));
                Assert.That(vm.ErrorMessage, Is.Null, "nothing is shown");
                return true; // even if the dialog reported a confirmation
            };

            Assert.That(editor.ChangePasswordNow(), Is.Null);

            Assert.That(wrongAccepted, Is.False);
            Assert.That(rightAccepted, Is.False);
            Assert.That(writes, Is.EqualTo(0), "no store call");
            Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
            Assert.That(nb.CredentialsMatch(Pw("pw")), Is.True, "the key is unchanged");
            Assert.That(editor.ChangePasswordBackOff.Failures, Is.EqualTo(0), "no back-off attempt counted");
            Assert.That(waits, Is.Empty);
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword" }), "no message");
        }

        [Test]
        public void AcceptedThenObscuredBeforeTheDialogReturned_NoStoreCall_CredentialsDisposed()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            int writes = 0;
            _store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };
            Credentials current = null, replacement = null;
            _dialogs.OnShowChangePassword = vm =>
            {
                bool accepted = vm.TryAccept(S("pw"), S(Strong), S(Strong));
                current = vm.Current;
                replacement = vm.Replacement;
                editor.Obscure();
                return accepted;
            };

            Assert.That(editor.ChangePasswordNow(), Is.Null);

            Assert.That(current, Is.Not.Null);
            Assert.That(writes, Is.EqualTo(0));
            Assert.That(nb.CredentialsMatch(Pw("pw")), Is.True);
            Assert.Throws<ObjectDisposedException>(() => GC.KeepAlive(current.CompositeKey));
            Assert.Throws<ObjectDisposedException>(() => GC.KeepAlive(replacement.CompositeKey));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword" }));
        }

        [Test]
        public void ShellLeftTheEditorWhileTheDialogIsOpen_NoStoreCall()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            int writes = 0;
            _store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };
            _dialogs.OnShowChangePassword = vm =>
            {
                bool accepted = vm.TryAccept(S("pw"), S(Strong), S(Strong));
                editor.Detach(); // locked, held or replaced by the shell
                return accepted;
            };

            Assert.That(editor.ChangePasswordNow(), Is.Null);

            Assert.That(writes, Is.EqualTo(0));
            Assert.That(nb.CredentialsMatch(Pw("pw")), Is.True);
        }

        // ---- the open dialog is not a save (fix round 2, X2; red team H2) ----

        [Test]
        public void DialogOpen_IsNotSaving_OnlyTheWritesAre()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            bool? savingInDialog = null;
            var savingAtWrites = new List<bool>();
            _store.TestHook = (stage, path) => { if (stage == "after-temp-write") savingAtWrites.Add(editor.IsSaving); };
            _dialogs.OnShowChangePassword = vm =>
            {
                savingInDialog = editor.IsSaving;
                return vm.TryAccept(S("pw"), S(Strong), S(Strong));
            };

            Assert.That(editor.ChangePasswordNow().Status, Is.EqualTo(ChangeKeyStatus.Changed));

            Assert.That(savingInDialog, Is.False, "the open dialog counts as a dialog, not as a save");
            Assert.That(savingAtWrites, Is.EqualTo(new[] { true, true }));
        }

        /// <summary>Red team RT3: with any other open dialog, a session end saves the unsaved notes quietly
        /// (ShellLockTests.PrepareForSessionEnd_DirtyWhileDialogOpen_SavesQuietly_Allows_KeepsEditor).</summary>
        [Test]
        public void SessionEndWhileTheDialogIsOpen_DirtyNotesAreSaved_LikeAnyOtherDialog()
        {
            Notebook nb = NewNotebook();
            _notebooks.Remove(nb);
            var shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
            try
            {
                shell.ShowEditor(nb);
                EditorViewModel editor = shell.Editor;
                editor.BeginBusy = () => new ActionDisposable(() => { });
                editor.SelectedNote = editor.VisibleNotes.First();
                editor.EditorText = "unsaved PIN 4711";
                Assert.That(nb.IsDirty, Is.True);

                bool? allowed = null;
                _dialogs.OnShowChangePassword = vm =>
                {
                    // The user left the dialog open; Windows logs off or restarts for an update.
                    _dialogs.IsShowingDialog = true;
                    allowed = shell.PrepareForSessionEnd();
                    _dialogs.IsShowingDialog = false;
                    return false;
                };
                editor.ChangePasswordNow();

                Assert.That(allowed, Is.True);
                using (Notebook check = new NotebookStore().Open(P, Pw("pw")))
                    Assert.That(check.Notes.Single().Text, Is.EqualTo("unsaved PIN 4711"));
            }
            finally
            {
                nb.Dispose();
            }
        }

        [Test]
        public void SessionEndSaveDuringTheDialog_ThenConfirm_TheChangeStillRuns()
        {
            Notebook nb = NewNotebook();
            _notebooks.Remove(nb);
            var shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
            try
            {
                shell.ShowEditor(nb);
                EditorViewModel editor = shell.Editor;
                editor.BeginBusy = () => new ActionDisposable(() => { });
                editor.SelectedNote = editor.VisibleNotes.First();
                editor.EditorText = "edited";
                _dialogs.OnShowChangePassword = vm =>
                {
                    _dialogs.IsShowingDialog = true;
                    Assert.That(shell.PrepareForSessionEnd(), Is.True); // then the user cancels the logoff
                    _dialogs.IsShowingDialog = false;
                    return vm.TryAccept(S("pw"), S(Strong), S(Strong));
                };

                ChangeKeyResult r = editor.ChangePasswordNow();

                Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), "the quiet save kept the loaded fingerprint current");
                using (Notebook check = new NotebookStore().Open(P, Pw(Strong)))
                    Assert.That(check.Notes.Single().Text, Is.EqualTo("edited"));
            }
            finally
            {
                nb.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_EditorObscuredWhileOpen_BoxesCleared_ChangeDisabled()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            var win = new ChangePasswordWindow(vm);
            try
            {
                Named<PasswordBox>(win, "CurrentInput").Password = "pw";
                Named<PasswordBox>(win, "NewInput").Password = Strong;
                Named<PasswordBox>(win, "RepeatInput").Password = Strong;

                vm.Abandon();
                Pump();

                Assert.That(Named<PasswordBox>(win, "CurrentInput").Password, Is.Empty);
                Assert.That(Named<PasswordBox>(win, "NewInput").Password, Is.Empty);
                Assert.That(Named<PasswordBox>(win, "RepeatInput").Password, Is.Empty);
                Assert.That(Named<Button>(win, "ChangeButton").IsEnabled, Is.False);
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        [Test]
        public void BackOff_ConsecutiveWrongAttempts_Wait1_2_4_8_8Seconds()
        {
            Notebook nb = NewNotebook();
            var clock = new FakeClock();
            var waits = new List<TimeSpan>();
            PasswordBackOff backOff = BackOff(clock, waits);
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, backOff))
            {
                for (int i = 0; i < 5; i++)
                {
                    Assert.That(vm.TryAccept(S("wrong"), S(Strong), S(Strong)), Is.False, "attempt " + (i + 1));
                    clock.Advance(waits.Last());
                }
            }
            Assert.That(waits.Select(w => w.TotalSeconds), Is.EqualTo(new double[] { 1, 2, 4, 8, 8 }));
        }

        [Test]
        public void BackOff_DuringTheWait_MessageSaysWhen_WaitingIsTrue_ThenBackToThePlainMessage()
        {
            Notebook nb = NewNotebook();
            var clock = new FakeClock();
            var waits = new List<TimeSpan>();
            var release = new TaskCompletionSource<bool>();
            PasswordBackOff backOff = BackOff(clock, waits, () => release.Task);
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, backOff))
            {
                Assert.That(vm.IsWaiting, Is.False);
                Assert.That(vm.TryAccept(S("wrong"), S(Strong), S(Strong)), Is.False);
                Assert.That(vm.ErrorMessage, Is.EqualTo("The current password is wrong. Try again in 1 s."));
                Assert.That(vm.ErrorField, Is.EqualTo(ChangePasswordField.Current));
                Assert.That(vm.IsWaiting, Is.True);

                clock.Advance(TimeSpan.FromSeconds(1));
                release.SetResult(true);
                Assert.That(vm.IsWaiting, Is.False);
                Assert.That(vm.ErrorMessage, Is.EqualTo("The current password is wrong."));
            }
        }

        [Test]
        public void BackOff_AttemptDuringTheWait_MakesNoComparison()
        {
            Notebook nb = NewNotebook();
            var clock = new FakeClock();
            var waits = new List<TimeSpan>();
            PasswordBackOff backOff = BackOff(clock, waits, Never);
            using (var vm = new ChangePasswordViewModel(nb, _dialogs, backOff))
            {
                Assert.That(vm.TryAccept(S("wrong"), S(Strong), S(Strong)), Is.False);
                Assert.That(backOff.Failures, Is.EqualTo(1));

                // The right password during the wait is not even compared.
                Assert.That(vm.TryAccept(S("pw"), S(Strong), S(Strong)), Is.False);
                Assert.That(vm.Current, Is.Null);
                Assert.That(vm.ErrorMessage, Is.EqualTo("The current password is wrong. Try again in 1 s."));
                clock.Advance(TimeSpan.FromMilliseconds(500));
                Assert.That(vm.TryAccept(S("wrong"), S(Strong), S(Strong)), Is.False);
                Assert.That(backOff.Failures, Is.EqualTo(1), "a wrong password during the wait is not counted either");
                Assert.That(waits.Count, Is.EqualTo(1));

                clock.Advance(TimeSpan.FromMilliseconds(500));
                Assert.That(vm.TryAccept(S("pw"), S(Strong), S(Strong)), Is.True, "after the wait");
            }
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public void BackOff_ReopeningTheDialogKeepsTheWait_NoStoreCall_SuccessResetsIt()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            var clock = new FakeClock();
            var waits = new List<TimeSpan>();
            editor.ChangePasswordBackOff = BackOff(clock, waits, Never);
            int writes = 0;
            _store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };

            _dialogs.OnShowChangePassword = vm => vm.TryAccept(S("wrong"), S(Strong), S(Strong));
            Assert.That(editor.ChangePasswordNow(), Is.Null, "wrong, then the user closes the dialog");

            _dialogs.OnShowChangePassword = vm =>
            {
                Assert.That(vm.IsWaiting, Is.True, "a new dialog keeps the wait");
                Assert.That(vm.ErrorMessage, Is.EqualTo("The current password is wrong. Try again in 1 s."));
                return vm.TryAccept(S("pw"), S(Strong), S(Strong));
            };
            Assert.That(editor.ChangePasswordNow(), Is.Null);
            Assert.That(writes, Is.EqualTo(0), "no store call while waiting");

            clock.Advance(TimeSpan.FromSeconds(1));
            _dialogs.OnShowChangePassword = vm => vm.TryAccept(S("wrong"), S(Strong), S(Strong));
            editor.ChangePasswordNow();
            Assert.That(waits.Last(), Is.EqualTo(TimeSpan.FromSeconds(2)), "consecutive across dialogs");

            clock.Advance(TimeSpan.FromSeconds(2));
            Answer("pw", Strong, Strong);
            Assert.That(editor.ChangePasswordNow().Status, Is.EqualTo(ChangeKeyStatus.Changed));
            Assert.That(editor.ChangePasswordBackOff.Failures, Is.EqualTo(0), "a successful change resets it");

            _store.TestHook = null;
            _dialogs.OnShowChangePassword = vm => vm.TryAccept(S("wrong"), S(Strong + "2"), S(Strong + "2"));
            editor.ChangePasswordNow();
            Assert.That(waits.Last(), Is.EqualTo(TimeSpan.FromSeconds(1)), "starts again at 1 s");
        }

        [Test]
        public void BackOff_IsPerEditor_ANewSessionStartsFresh()
        {
            Notebook nb = NewNotebook();
            EditorViewModel first = NewEditor(nb);
            EditorViewModel second = NewEditor(nb);
            Assert.That(first.ChangePasswordBackOff, Is.Not.Null);
            Assert.That(second.ChangePasswordBackOff, Is.Not.SameAs(first.ChangePasswordBackOff));
            Assert.That(second.ChangePasswordBackOff.Failures, Is.EqualTo(0));
        }

        [Test]
        public void BackOff_UsesTheHeldUnlockSchedule()
        {
            for (int failures = 1; failures <= 6; failures++)
            {
                var clock = new FakeClock();
                var b = new PasswordBackOff(clock);
                TimeSpan wait = TimeSpan.Zero;
                for (int i = 0; i < failures; i++) wait = b.RecordFailure();
                Assert.That(wait, Is.EqualTo(UnlockViewModel.HeldBackOff(failures)), failures + " failures");
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_WrongCurrent_ChangeButtonDisabledForTheWait()
        {
            Notebook nb = NewNotebook();
            var clock = new FakeClock();
            var waits = new List<TimeSpan>();
            var release = new TaskCompletionSource<bool>();
            var vm = new ChangePasswordViewModel(nb, _dialogs, BackOff(clock, waits, () => release.Task));
            var win = new ChangePasswordWindow(vm);
            try
            {
                var change = Named<Button>(win, "ChangeButton");
                Assert.That(change.IsEnabled, Is.True);
                Named<PasswordBox>(win, "CurrentInput").Password = "wrong";
                Named<PasswordBox>(win, "NewInput").Password = Strong;
                Named<PasswordBox>(win, "RepeatInput").Password = Strong;
                Click(change);
                Pump();
                Assert.That(change.IsEnabled, Is.False, "disabled for the wait");
                Assert.That(Named<TextBlock>(win, "ErrorText").Text,
                    Is.EqualTo("The current password is wrong. Try again in 1 s."));

                clock.Advance(TimeSpan.FromSeconds(1));
                release.SetResult(true);
                Pump();
                Assert.That(change.IsEnabled, Is.True);
                Assert.That(Named<TextBlock>(win, "ErrorText").Text, Is.EqualTo("The current password is wrong."));
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        // ---- the editor: running the change ----

        [Test]
        public void Change_Success_InfoMessage_NotebookUsesTheNewPassword()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);

            ChangeKeyResult r = editor.ChangePasswordNow();

            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), r.Message);
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword", "ShowInfo" }));
            Assert.That(_dialogs.LastInfoTitle, Is.EqualTo("Master password changed"));
            Assert.That(_dialogs.LastInfoMessage, Is.EqualTo(SuccessMessage));
            Assert.That(nb.CredentialsMatch(Pw(Strong)), Is.True, "later saves, lock and unlock use the new key");
            Assert.That(editor.StatusText, Is.EqualTo("Saved"));
            Assert.That(Opens(P, Strong), Is.True);
            Assert.That(Opens(P, "pw"), Is.False);
            Assert.That(Opens(P + ".bak", Strong), Is.True);
            Assert.That(Opens(P + ".bak", "pw"), Is.False);
        }

        [Test]
        public void Change_RunsLikeCtrlS_IsSavingAndBusyThroughout()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);
            var savingAtWrites = new List<bool>();
            var busyAtWrites = new List<bool>();
            _store.TestHook = (stage, path) =>
            {
                if (stage != "after-temp-write") return;
                savingAtWrites.Add(editor.IsSaving);
                busyAtWrites.Add(_busyBegun == 1 && _busyEnded == 0);
            };

            editor.ChangePasswordNow();

            Assert.That(savingAtWrites, Is.EqualTo(new[] { true, true }), "both writes");
            Assert.That(busyAtWrites, Is.EqualTo(new[] { true, true }));
            Assert.That(editor.IsSaving, Is.False);
            Assert.That(_busyEnded, Is.EqualTo(1));
        }

        [Test]
        public void Change_IdleExpiryDuringTheChange_ObscuresThenLocksAfter()
        {
            Notebook nb = NewNotebook();
            var clock = new FakeClock();
            DateTime wall = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
            var shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                new FakeClipboardService(), clock, TestKdf.Fast);
            shell.WallClock = () => wall;
            shell.ShowEditor(nb);
            EditorViewModel editor = shell.Editor;
            editor.BeginBusy = () => new ActionDisposable(() => { });
            Answer("pw", Strong, Strong);
            bool obscuredDuringChange = false;
            _store.TestHook = (stage, path) =>
            {
                if (stage != "after-temp-write" || obscuredDuringChange) return;
                clock.Advance(TimeSpan.FromMinutes(10));
                wall = wall.AddMinutes(10);
                shell.Tick();
                obscuredDuringChange = shell.Editor != null && shell.Editor.IsObscured;
            };

            ChangeKeyResult r = editor.ChangePasswordNow();

            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Changed), r.Message);
            Assert.That(obscuredDuringChange, Is.True, "never locked mid-change: obscured instead");
            _store.TestHook = null;
            shell.Tick();
            Assert.That(shell.Editor, Is.Null, "locked after the change");
            _notebooks.Remove(nb); // the shell disposed it on lock
        }

        [Test]
        public void Change_Cancelled_NothingWritten_NoMessage()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            byte[] main = File.ReadAllBytes(P);
            int writes = 0;
            _store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };

            Assert.That(editor.ChangePasswordNow(), Is.Null);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword" }));
            Assert.That(writes, Is.EqualTo(0));
            Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
            Assert.That(_busyBegun, Is.EqualTo(0));
        }

        [Test]
        public void Change_WrongCurrentPassword_NoStoreCall()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            byte[] main = File.ReadAllBytes(P);
            int writes = 0;
            _store.TestHook = (stage, path) => { if (stage == "after-temp-write") writes++; };
            ChangePasswordViewModel shown = null;
            _dialogs.OnShowChangePassword = vm =>
            {
                shown = vm;
                bool accepted = vm.TryAccept(S("wrong"), S(Strong), S(Strong));
                Assert.That(vm.ErrorMessage, Is.EqualTo("The current password is wrong. Try again in 1 s."));
                return accepted; // stays open; the user then cancels
            };

            Assert.That(editor.ChangePasswordNow(), Is.Null);

            Assert.That(shown, Is.Not.Null);
            Assert.That(writes, Is.EqualTo(0), "no store call");
            Assert.That(File.ReadAllBytes(P), Is.EqualTo(main));
            Assert.That(nb.CredentialsMatch(Pw("pw")), Is.True);
            Assert.That(Count("ShowError") + Count("ShowWarning") + Count("ShowInfo"), Is.EqualTo(0));
        }

        [Test]
        public void Change_SecondWriteFails_Warning()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);
            int writes = 0;
            _store.TestHook = (stage, path) =>
            {
                if (stage == "after-temp-write") writes++;
                if (stage == "before-replace" && writes == 2) throw new IOException("simulated second failure");
            };

            ChangeKeyResult r = editor.ChangePasswordNow();

            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword", "ShowWarning" }));
            Assert.That(_dialogs.LastWarningMessage, Is.EqualTo("The password was changed, but the backup file could "
                + "not be re-locked: simulated second failure. It still opens with the old password; LockPad re-locks it "
                + "the next time it saves (also when it locks or closes)."));
            Assert.That(_dialogs.LastWarningTitle, Is.EqualTo("Master password changed"));
            Assert.That(nb.CredentialsMatch(Pw(Strong)), Is.True);
            Assert.That(editor.StatusText, Is.EqualTo("Unsaved changes"), "a save is pending");
        }

        // ---- fix round 2: the re-lock is retried on save, lock and close (X3) ----

        private void FailSecondWriteOnce()
        {
            int writes = 0;
            _store.TestHook = (stage, path) =>
            {
                if (stage == "after-temp-write") writes++;
                if (stage == "before-replace" && writes == 2) throw new IOException("simulated second failure");
            };
        }

        private string[] FilesOpeningWith(string password)
        {
            return Directory.GetFiles(_dir.Path)
                .Where(f => { try { return Opens(f, password); } catch (Exception) { return false; } })
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        private ShellViewModel NewShell()
        {
            return new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
        }

        [Test]
        public void AfterBackupNotRelocked_CtrlS_RelocksTheBak()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);
            FailSecondWriteOnce();
            Assert.That(editor.ChangePasswordNow().Status, Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
            _store.TestHook = null;

            Assert.That(editor.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));

            Assert.That(editor.StatusText, Is.EqualTo("Saved"));
            Assert.That(FilesOpeningWith("pw"), Is.Empty);
            Assert.That(Opens(P + ".bak", Strong), Is.True);
        }

        /// <summary>Red team RT1b: a lock after the warning saves (the notebook needs a write) and re-locks the .bak, so
        /// the next session's saves find no old-password file.</summary>
        [Test]
        public void AfterBackupNotRelocked_ALockWritesAndRelocks_TheNextSessionFindsNoOldPasswordFile()
        {
            Notebook nb = NewNotebook();
            _notebooks.Remove(nb); // the shell disposes it on lock
            ShellViewModel shell = NewShell();
            shell.ShowEditor(nb);
            EditorViewModel editor = shell.Editor;
            editor.BeginBusy = () => new ActionDisposable(() => { });
            Answer("pw", Strong, Strong);
            FailSecondWriteOnce();
            Assert.That(editor.ChangePasswordNow().Status, Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
            _store.TestHook = null;

            shell.Lock(); // Ctrl+L, the idle lock or Windows lock

            Assert.That(shell.Editor, Is.Null);
            Assert.That(((UnlockViewModel)shell.CurrentView).HeldWarning, Is.Null, "saved, not held");
            Assert.That(FilesOpeningWith("pw"), Is.Empty, "the lock's save re-locked the .bak");

            var later = new NotebookStore();
            string notice;
            using (Notebook next = later.Open(P, Pw(Strong)))
            {
                next.AddNote("edited after unlock");
                Assert.That(later.Save(next, false).Status, Is.EqualTo(SaveStatus.Saved));
                notice = next.TakeBackupNotice();
            }
            Assert.That(notice, Is.Null);
            Assert.That(FilesOpeningWith("pw"), Is.Empty);
        }

        [Test]
        public void AfterBackupNotRelocked_ALockWhoseSaveFails_HoldsTheNotebook()
        {
            Notebook nb = NewNotebook();
            ShellViewModel shell = NewShell();
            shell.ShowEditor(nb);
            EditorViewModel editor = shell.Editor;
            editor.BeginBusy = () => new ActionDisposable(() => { });
            Answer("pw", Strong, Strong);
            FailSecondWriteOnce();
            Assert.That(editor.ChangePasswordNow().Status, Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
            _store.TestHook = (stage, path) => { if (stage == "before-replace") throw new IOException("still held"); };

            shell.Lock();

            var unlock = (UnlockViewModel)shell.CurrentView;
            Assert.That(unlock.HeldWarning, Does.StartWith("Unsaved changes are held in memory. Saving failed: "));
            Assert.That(nb.IsDirty, Is.True);
            _store.TestHook = null;
        }

        // ---- fix round 3: Save As while the .bak re-lock is pending (Y1; red team RA1 / RM1) ----

        private const string OldBackupTitle = "Old backup still uses the old password";

        private static string OldBackupMessage(string bak)
        {
            return "The backup file \"" + bak + "\" was not re-locked and still opens with the OLD password. Delete it if "
                + "you no longer need it.";
        }

        /// <summary>Red team RA1: the re-lock save fails again, the user follows the error's hint and saves elsewhere.
        /// Nothing at the old location is deleted or rewritten; the user is told which file still uses the old
        /// password.</summary>
        [Test]
        public void AfterBackupNotRelocked_SaveFailsAgain_SaveAsElsewhere_WarnsAboutTheOldBak_WithItsFullPath()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Directory.CreateDirectory(_dir.File("elsewhere"));
            Answer("pw", Strong, Strong);
            FailSecondWriteOnce();
            Assert.That(editor.ChangePasswordNow().Status, Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
            _store.TestHook = null;
            string bak = P + ".bak";

            // The same program still holds the .bak (no FILE_SHARE_DELETE): the re-lock save fails again.
            SaveResult again;
            using (new FileStream(bak, FileMode.Open, FileAccess.Read, FileShare.Read))
                again = editor.SaveNow();
            Assert.That(again.Status, Is.Not.EqualTo(SaveStatus.Saved));
            _dialogs.Calls.Clear();

            _dialogs.SaveAsPaths.Enqueue(_dir.File(Path.Combine("elsewhere", "nb.kdbx")));
            SaveResult saved = editor.SaveAsNow();

            Assert.That(saved.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(saved.UnrelockedBackupPath, Is.EqualTo(bak));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickSaveAsPath", "ShowWarning" }));
            Assert.That(_dialogs.LastWarningTitle, Is.EqualTo(OldBackupTitle));
            Assert.That(_dialogs.LastWarningMessage, Is.EqualTo(OldBackupMessage(bak)));
            Assert.That(nb.IsDirty, Is.False);
            Assert.That(File.Exists(bak), Is.True, "nothing at the old location is deleted");
            Assert.That(Opens(bak, "pw"), Is.True, "nor rewritten");
            Assert.That(Opens(P, Strong), Is.True);
        }

        [Test]
        public void SaveAs_NoPendingRelock_NoOldBackupWarning()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Assert.That(editor.SaveNow().Status, Is.EqualTo(SaveStatus.Saved)); // a .bak exists at the old location
            nb.AddNote("more");
            Assert.That(editor.SaveNow().Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(File.Exists(P + ".bak"), Is.True);
            _dialogs.Calls.Clear();

            _dialogs.SaveAsPaths.Enqueue(_dir.File("copy.kdbx"));
            SaveResult saved = editor.SaveAsNow();

            Assert.That(saved.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(saved.UnrelockedBackupPath, Is.Null);
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickSaveAsPath" }));
            Assert.That(_dialogs.LastWarningMessage, Is.Null);
        }

        [Test]
        public void SaveAs_PendingRelock_OldBakNoLongerExists_NoOldBackupWarning()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);
            FailSecondWriteOnce();
            Assert.That(editor.ChangePasswordNow().Status, Is.EqualTo(ChangeKeyStatus.ChangedButBackupNotRelocked));
            _store.TestHook = null;
            File.Delete(P + ".bak"); // the user deleted it
            _dialogs.Calls.Clear();
            _dialogs.LastWarningMessage = null;

            _dialogs.SaveAsPaths.Enqueue(_dir.File("copy.kdbx"));
            SaveResult saved = editor.SaveAsNow();

            Assert.That(saved.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(saved.UnrelockedBackupPath, Is.Null);
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickSaveAsPath" }));
            Assert.That(_dialogs.LastWarningMessage, Is.Null);
            Assert.That(nb.IsDirty, Is.False);
        }

        // ---- re-attack 2026-10-22: the outcome box is not part of the save; failed swap keeps a write pending ----

        /// <summary>Re-attack M: a session end while a failed change's error box is open saves the unsaved notes quietly,
        /// as with any other open dialog (the write phase is over; only it counts as saving). The dialog's credentials are
        /// released before the box is shown (re-attack L1).</summary>
        [Test]
        public void SessionEndWhileTheFailureBoxIsOpen_DirtyNotesAreSaved_CredentialsAlreadyReleased()
        {
            Notebook nb = NewNotebook();
            _notebooks.Remove(nb);
            var shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
            try
            {
                shell.ShowEditor(nb);
                EditorViewModel editor = shell.Editor;
                editor.BeginBusy = () => new ActionDisposable(() => { });
                editor.SelectedNote = editor.VisibleNotes.First();
                editor.EditorText = "unsaved PIN 4711";
                ChangePasswordViewModel dialog = null;
                _dialogs.OnShowChangePassword = vm => { dialog = vm; return vm.TryAccept(S("pw"), S(Strong), S(Strong)); };
                // A backup tool holds the files for a moment: the change's first swap fails, nothing changed.
                _store.ReplaceFile = (temp, target, backup) => { throw new IOException("The file is in use by another program."); };
                bool? allowed = null;
                bool savingInBox = true;
                bool credentialsGone = false;
                _dialogs.OnError = () =>
                {
                    _store.ReplaceFile = null; // the fault cleared; the user is away; Windows restarts for an update
                    savingInBox = editor.IsSaving;
                    credentialsGone = dialog.Current == null && dialog.Replacement == null;
                    _dialogs.IsShowingDialog = true;
                    allowed = shell.PrepareForSessionEnd();
                    _dialogs.IsShowingDialog = false;
                };

                ChangeKeyResult r = editor.ChangePasswordNow();

                Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Failed));
                Assert.That(savingInBox, Is.False, "the outcome box is not part of the save");
                Assert.That(credentialsGone, Is.True, "the unapplied new password is not kept while the box is open");
                Assert.That(allowed, Is.True);
                using (Notebook check = new NotebookStore().Open(P, Pw("pw")))
                    Assert.That(check.Notes.Single().Text, Is.EqualTo("unsaved PIN 4711"));
            }
            finally
            {
                nb.Dispose();
            }
        }

        /// <summary>Re-attack L2: after a half-finished first swap the notebook file is missing, so the notebook keeps a
        /// write pending: lock and close then try to save (and hold, or offer Save As) instead of dropping it silently.</summary>
        [Test]
        public void FirstReplaceFailsHalfway_NotebookKeepsAWritePending()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Assert.That(nb.IsDirty, Is.False);
            Answer("pw", Strong, Strong);
            _store.ReplaceFile = (temp, target, backup) =>
            {
                File.Delete(backup);
                File.Move(target, backup);
                throw new IOException("Unable to move the replacement file to the file to be replaced.");
            };

            ChangeKeyResult r = editor.ChangePasswordNow();

            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.FailedWhileReplacing));
            Assert.That(File.Exists(P), Is.False);
            Assert.That(nb.IsDirty, Is.True, "lock and close must not drop it without trying to save");
            Assert.That(nb.CredentialsMatch(Pw("pw")), Is.True, "the old key stays with the notebook");
        }

        // ---- fix round 2: half-finished swap in the second write (X4) ----

        /// <summary>Red team RT2 (App part).</summary>
        [Test]
        public void SecondReplaceFailsHalfway_ErrorSaysEveryFileUsesTheNewPassword_NotebookStaysDirty()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);
            int replaces = 0;
            _store.ReplaceFile = (temp, target, backup) =>
            {
                replaces++;
                if (replaces == 1) { File.Replace(temp, target, backup, true); return; }
                // ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 on the second swap: target renamed onto the .bak, temp stays.
                File.Delete(backup);
                File.Move(target, backup);
                throw new IOException("Unable to move the replacement file to the file to be replaced.");
            };

            ChangeKeyResult r = editor.ChangePasswordNow();

            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.ChangedButReplaceIncomplete));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword", "ShowError" }));
            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Password change incomplete"));
            Assert.That(_dialogs.LastErrorMessage, Does.StartWith("The new password is set, but replacing the file did not "
                + "finish: Unable to move the replacement file to the file to be replaced. The new copy was kept at "));
            Assert.That(_dialogs.LastErrorMessage, Does.EndWith(". All files now open only with the NEW password. The "
                + "notebook file is missing, so Save (Ctrl+S) cannot finish this: use File > Save As (Ctrl+Shift+S) to save "
                + "under a new name before you close LockPad."));
            Assert.That(_dialogs.LastErrorMessage, Does.Not.Contain(".."));
            Assert.That(_dialogs.LastWarningMessage, Is.Null);
            Assert.That(File.Exists(P), Is.False);
            Assert.That(nb.IsDirty, Is.True, "closing LockPad saves (and fails visibly) instead of asking nothing");
            Assert.That(FilesOpeningWith("pw"), Is.Empty);
        }

        [Test]
        public void Change_FirstWriteFails_Error()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);
            _store.TestHook = (stage, path) =>
            {
                if (stage == "after-temp-write") throw new IOException("simulated disk failure");
            };

            ChangeKeyResult r = editor.ChangePasswordNow();

            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.Failed));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword", "ShowError" }));
            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo("Changing the password failed: simulated disk failure. "
                + "Nothing was changed; the old password still works."));
            Assert.That(nb.CredentialsMatch(Pw("pw")), Is.True);
            Assert.That(_busyEnded, Is.EqualTo(_busyBegun));
            Assert.That(editor.IsSaving, Is.False);
        }

        [Test]
        public void Change_FailureReasonEndingWithAPeriod_NoDoublePeriod()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);
            _store.TestHook = (stage, path) =>
            {
                if (stage == "after-temp-write") File.WriteAllBytes(path, new byte[] { 1, 2, 3 }); // verify fails
            };

            editor.ChangePasswordNow();

            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo("Changing the password failed: The saved copy did not "
                + "match. Your file was not changed. Nothing was changed; the old password still works."));
        }

        [Test]
        public void Change_HalfFinishedSwap_SaysSomeFilesMayUseTheNewPassword()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);
            // ERROR_UNABLE_TO_MOVE_REPLACEMENT_2: the original became the .bak, the new copy stayed at the temp path.
            _store.ReplaceFile = (temp, target, backup) =>
            {
                File.Delete(backup);
                File.Move(target, backup);
                throw new IOException("Unable to move the replacement file to the file to be replaced.");
            };

            ChangeKeyResult r = editor.ChangePasswordNow();

            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.FailedWhileReplacing));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword", "ShowError" }));
            Assert.That(_dialogs.LastErrorTitle, Is.EqualTo("Password change incomplete"));
            Assert.That(_dialogs.LastErrorMessage, Does.StartWith("Changing the password failed while replacing the file: "
                + "Unable to move the replacement file to the file to be replaced. The new copy was kept at "));
            Assert.That(_dialogs.LastErrorMessage, Does.EndWith(". Some files may already open only with the new password; "
                + "keep both passwords until you have checked which files open with which."));
            Assert.That(_dialogs.LastErrorMessage, Does.Not.Contain("Nothing was changed"));
            Assert.That(_dialogs.LastErrorMessage, Does.Not.Contain(".."));
        }

        [Test]
        public void Change_ExternalChange_ErrorWithSavesMessage()
        {
            Notebook nb = NewNotebook();
            EditorViewModel editor = NewEditor(nb);
            File.AppendAllText(P, "x");
            Answer("pw", Strong, Strong);

            ChangeKeyResult r = editor.ChangePasswordNow();

            Assert.That(r.Status, Is.EqualTo(ChangeKeyStatus.ExternalChangeDetected));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowChangePassword", "ShowError" }));
            Assert.That(_dialogs.LastErrorMessage, Is.EqualTo("The file was changed by another program since it was "
                + "opened. Save or reopen the notebook, then try again."));
            Assert.That(nb.CredentialsMatch(Pw("pw")), Is.True);
        }

        [Test]
        public void Change_KeptAsideBakNotice_ShownInTheStatusBar()
        {
            Notebook nb = NewNotebook();
            File.Delete(P + ".bak");
            File.WriteAllText(P + ".bak", "the user's own copy");
            EditorViewModel editor = NewEditor(nb);
            Answer("pw", Strong, Strong);

            Assert.That(editor.ChangePasswordNow().Status, Is.EqualTo(ChangeKeyStatus.Changed));

            Assert.That(editor.StatusText, Does.StartWith("An existing file \"nb.kdbx.bak\" was not created by this notebook."));
        }

        [Test]
        public void Command_DisabledWhenReadOnlyOrObscured()
        {
            NewNotebook();
            Notebook ro = _store.Open(P + ".bak", Pw());
            _notebooks.Add(ro);
            EditorViewModel readOnly = NewEditor(ro);
            Assert.That(readOnly.ChangePasswordCommand.CanExecute(null), Is.False);
            Assert.That(readOnly.ChangePasswordNow(), Is.Null);
            Assert.That(_dialogs.Calls, Is.Empty);

            Notebook nb = _store.Open(P, Pw());
            _notebooks.Add(nb);
            EditorViewModel editor = NewEditor(nb);
            Assert.That(editor.ChangePasswordCommand.CanExecute(null), Is.True);
            int changed = 0;
            editor.ChangePasswordCommand.CanExecuteChanged += (s, e) => changed++;
            editor.Obscure();
            Assert.That(changed, Is.GreaterThan(0));
            Assert.That(editor.ChangePasswordCommand.CanExecute(null), Is.False);
        }

        // ---- the menu ----

        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Menu_FileChangeMasterPassword_DisabledWhenLockedOrReadOnly()
        {
            var shell = new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                Pump();
                var item = (MenuItem)window.FindName("ChangePasswordMenuItem");
                Assert.That(item, Is.Not.Null);
                Assert.That(item.Header, Is.EqualTo("_Change master password" + (char)0x2026));
                var file = (MenuItem)item.Parent;
                Assert.That(file.Header, Is.EqualTo("_File"));
                string[] keys = file.Items.OfType<MenuItem>().Select(m => AccessKey((string)m.Header)).ToArray();
                Assert.That(keys, Is.Unique, "File menu access keys");
                Assert.That(item.InputGestureText, Is.Empty.Or.Null, "no shortcut");
                Assert.That(item.IsEnabled, Is.False, "locked");

                Notebook nb = NewNotebook();
                shell.ShowEditor(nb);
                Pump();
                Assert.That(item.Command, Is.SameAs(shell.Editor.ChangePasswordCommand));
                Assert.That(item.Command.CanExecute(item.CommandParameter), Is.True);
                Assert.That(item.IsEnabled, Is.True);
                shell.Lock();
                _notebooks.Remove(nb);
                Pump();
                Assert.That(item.IsEnabled, Is.False, "locked again");

                Notebook ro = _store.Open(P + ".bak", Pw());
                shell.ShowEditor(ro);
                Pump();
                // WPF evaluates a submenu item's CanExecute only while its parent menu is open, which a window that
                // was never shown cannot do: check the bound command, which decides the item's state when it opens.
                Assert.That(item.Command, Is.SameAs(shell.Editor.ChangePasswordCommand));
                Assert.That(item.Command.CanExecute(item.CommandParameter), Is.False, "read-only");
                shell.Lock();
            }
            finally
            {
                window.Close();
            }
        }

        private static string AccessKey(string header)
        {
            int i = header.IndexOf('_');
            return i < 0 || i + 1 >= header.Length ? "" : header.Substring(i + 1, 1).ToUpperInvariant();
        }

        // ---- the window ----

        private static T Named<T>(Window w, string name) where T : class
        {
            var found = w.FindName(name) as T;
            Assert.That(found, Is.Not.Null, name);
            return found;
        }

        private static void Click(Button b)
        {
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_Layout_ThreeBoxes_ChangeIsDefault_CancelIsEsc()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            var win = new ChangePasswordWindow(vm);
            try
            {
                Assert.That(win.Title, Is.EqualTo("Change master password"));
                Assert.That(Named<TextBlock>(win, "CurrentLabel").Text, Is.EqualTo("Current password"));
                Assert.That(Named<TextBlock>(win, "NewLabel").Text, Is.EqualTo("New password"));
                Assert.That(Named<TextBlock>(win, "RepeatLabel").Text, Is.EqualTo("Repeat new password"));
                Named<PasswordBox>(win, "CurrentInput");
                Named<PasswordBox>(win, "NewInput");
                Named<PasswordBox>(win, "RepeatInput");
                Named<ProgressBar>(win, "StrengthBar");
                var change = Named<Button>(win, "ChangeButton");
                var cancel = Named<Button>(win, "CancelButton");
                Assert.That(change.Content, Is.EqualTo("Change password"));
                Assert.That(change.IsDefault, Is.True);
                Assert.That(cancel.Content, Is.EqualTo("Cancel"));
                Assert.That(cancel.IsCancel, Is.True);
                Assert.That(win.DataContext, Is.SameAs(vm));
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_EmptyFields_InlineError_StaysOpen()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            var win = new ChangePasswordWindow(vm);
            try
            {
                Click(Named<Button>(win, "ChangeButton"));
                Pump();
                var error = Named<TextBlock>(win, "ErrorText");
                Assert.That(error.Text, Is.EqualTo("Enter your current password."));
                Assert.That(error.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(win.DialogResult, Is.Null, "still open");

                Named<PasswordBox>(win, "CurrentInput").Password = "pw";
                Click(Named<Button>(win, "ChangeButton"));
                Pump();
                Assert.That(error.Text, Is.EqualTo("Enter a new password."));
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_WeakPasswordNo_NewBoxesCleared_CurrentKept()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            var win = new ChangePasswordWindow(vm);
            try
            {
                Named<PasswordBox>(win, "CurrentInput").Password = "pw";
                Named<PasswordBox>(win, "NewInput").Password = "abc";
                Named<PasswordBox>(win, "RepeatInput").Password = "abc";
                Click(Named<Button>(win, "ChangeButton"));
                Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo" }));
                Assert.That(Named<PasswordBox>(win, "NewInput").Password, Is.Empty);
                Assert.That(Named<PasswordBox>(win, "RepeatInput").Password, Is.Empty);
                Assert.That(Named<PasswordBox>(win, "CurrentInput").Password, Is.EqualTo("pw"));
                Assert.That(win.DialogResult, Is.Null, "still open");
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_WrongCurrent_CurrentBoxCleared_NewKept()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, BackOff(new FakeClock(), new List<TimeSpan>(), Never));
            var win = new ChangePasswordWindow(vm);
            try
            {
                Named<PasswordBox>(win, "CurrentInput").Password = "wrong";
                Named<PasswordBox>(win, "NewInput").Password = Strong;
                Named<PasswordBox>(win, "RepeatInput").Password = Strong;
                Click(Named<Button>(win, "ChangeButton"));
                Pump();
                Assert.That(Named<TextBlock>(win, "ErrorText").Text,
                    Is.EqualTo("The current password is wrong. Try again in 1 s."));
                Assert.That(Named<PasswordBox>(win, "CurrentInput").Password, Is.Empty);
                Assert.That(Named<PasswordBox>(win, "NewInput").Password, Is.EqualTo(Strong));
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_TypingTheNewPassword_UpdatesTheStrength()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            var win = new ChangePasswordWindow(vm);
            try
            {
                Named<PasswordBox>(win, "NewInput").Password = Strong;
                Assert.That(vm.Strength, Is.EqualTo(StrengthRating.Strong));
                Assert.That(vm.StrengthBits, Is.GreaterThan(0));
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        // ---- Fix round 2, X5: Caps Lock warning (state through a seam; no real keyboard) ----

        private static void Focus(UIElement box, IInputElement from, IInputElement to, RoutedEvent routedEvent)
        {
            box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, from, to)
            {
                RoutedEvent = routedEvent
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_CapsLockOn_FocusInAnyOfTheThreeBoxes_ShowsTheWarning_HiddenWhenFocusLeaves()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            var win = new ChangePasswordWindow(vm);
            try
            {
                var muted = new System.Windows.Media.SolidColorBrush();
                win.Resources["MutedForeground"] = muted; // the theme is not loaded in tests
                win.CapsLockState = () => true;
                var warning = Named<TextBlock>(win, "CapsLockWarning");
                var change = Named<Button>(win, "ChangeButton");
                Assert.That(warning.Text, Is.EqualTo("Caps Lock is on."));
                Assert.That(warning.Visibility, Is.EqualTo(Visibility.Collapsed), "nothing focused yet");

                foreach (string name in new[] { "CurrentInput", "NewInput", "RepeatInput" })
                {
                    var box = Named<PasswordBox>(win, name);
                    Focus(box, change, box, Keyboard.GotKeyboardFocusEvent);
                    Assert.That(warning.Visibility, Is.EqualTo(Visibility.Visible), name);
                    Assert.That(win.IsCapsLockWarningShown, Is.True, name);

                    Focus(box, box, change, Keyboard.LostKeyboardFocusEvent);
                    Assert.That(warning.Visibility, Is.EqualTo(Visibility.Collapsed), name + ": focus left the boxes");
                    Assert.That(win.IsCapsLockWarningShown, Is.False, name);
                }
                Assert.That(warning.Foreground, Is.SameAs(muted), "a muted line");
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_CapsLockOff_FocusInABox_NoWarning_TogglingItWhileTyping_UpdatesTheLine()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            var win = new ChangePasswordWindow(vm);
            try
            {
                bool caps = false;
                win.CapsLockState = () => caps;
                var warning = Named<TextBlock>(win, "CapsLockWarning");
                var box = Named<PasswordBox>(win, "NewInput");
                Focus(box, null, box, Keyboard.GotKeyboardFocusEvent);
                Assert.That(warning.Visibility, Is.EqualTo(Visibility.Collapsed));

                caps = true;
                win.RefreshCapsLock(box);
                Assert.That(warning.Visibility, Is.EqualTo(Visibility.Visible), "Caps Lock pressed in the box");

                caps = false;
                win.RefreshCapsLock(box);
                Assert.That(warning.Visibility, Is.EqualTo(Visibility.Collapsed), "Caps Lock released");

                caps = true;
                win.RefreshCapsLock(Named<Button>(win, "CancelButton"));
                Assert.That(warning.Visibility, Is.EqualTo(Visibility.Collapsed), "on but not in a box");
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        // ---- Fix round 2, X6: the backup note ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Window_BackupNote_UnderTheBoxes_Muted_Verbatim()
        {
            Notebook nb = NewNotebook();
            var vm = new ChangePasswordViewModel(nb, _dialogs, NewBackOff());
            var win = new ChangePasswordWindow(vm);
            try
            {
                var muted = new System.Windows.Media.SolidColorBrush();
                win.Resources["MutedForeground"] = muted; // the theme is not loaded in tests
                var note = Named<TextBlock>(win, "BackupNote");
                Assert.That(note.Text, Is.EqualTo(
                    "Changing the password saves your notebook. The backup file (.bak) will then hold your current " +
                    "notes, so notes you deleted can no longer be restored from it."));
                Assert.That(note.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(note.Foreground, Is.SameAs(muted), "a muted line");

                var panel = (Panel)note.Parent;
                Assert.That(panel.Children.IndexOf(note),
                    Is.GreaterThan(panel.Children.IndexOf(Named<PasswordBox>(win, "RepeatInput"))), "under the boxes");
            }
            finally
            {
                win.Close();
                vm.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }
    }
}

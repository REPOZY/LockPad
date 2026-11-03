using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LockPad.App;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// 2026-10-21 (user decision: on by default): every LockPad window asks Windows to leave it out of screenshots,
    /// recordings and screen sharing (SetWindowDisplayAffinity, WDA_EXCLUDEFROMCAPTURE) when its HWND is created, and
    /// again when the setting changes. Only fakes are used: the real user32 call is never made and no real HWND is
    /// created (fake handles through CaptureProtection.HandleOf; SourceInitialized raised the way Window raises it).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class CaptureProtectionTests
    {
        private sealed class FakeAffinity : IDisplayAffinityApi
        {
            public readonly List<Tuple<IntPtr, uint>> Calls = new List<Tuple<IntPtr, uint>>();
            public readonly Dictionary<uint, bool> Results = new Dictionary<uint, bool>();
            public readonly Dictionary<uint, Exception> Throws = new Dictionary<uint, Exception>();

            public bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity)
            {
                Calls.Add(Tuple.Create(hwnd, affinity));
                Exception ex;
                if (Throws.TryGetValue(affinity, out ex)) throw ex;
                bool r;
                return Results.TryGetValue(affinity, out r) ? r : true;
            }

            public uint[] For(IntPtr hwnd)
            {
                return Calls.Where(c => c.Item1 == hwnd).Select(c => c.Item2).ToArray();
            }
        }

        private FakeAffinity _api;
        private Func<Window, IntPtr> _realHandleOf;
        private Dictionary<Window, IntPtr> _handles;
        private TempDir _dir;

        [SetUp]
        public void SetUp()
        {
            _api = new FakeAffinity();
            _handles = new Dictionary<Window, IntPtr>();
            _realHandleOf = CaptureProtection.HandleOf;
            CaptureProtection.Api = _api;
            CaptureProtection.HandleOf = w =>
            {
                IntPtr h;
                return _handles.TryGetValue(w, out h) ? h : IntPtr.Zero;
            };
            CaptureProtection.SetEnabled(true, null);
            _dir = new TempDir();
        }

        [TearDown]
        public void TearDown()
        {
            CaptureProtection.Api = null;
            CaptureProtection.HandleOf = _realHandleOf;
            CaptureProtection.SetEnabled(true, null);
            _dir.Dispose();
        }

        // ---- the user32 request ----

        [Test]
        public void Affinity_Values()
        {
            Assert.That(CaptureProtection.WdaNone, Is.EqualTo(0x0u));
            Assert.That(CaptureProtection.WdaMonitor, Is.EqualTo(0x1u));
            Assert.That(CaptureProtection.WdaExcludeFromCapture, Is.EqualTo(0x11u));
        }

        [Test]
        public void Apply_On_ExcludesFromCapture()
        {
            CaptureProtection.Apply((IntPtr)42, true, _api);
            Assert.That(_api.For((IntPtr)42), Is.EqualTo(new[] { 0x11u }));
        }

        [Test]
        public void Apply_Off_RemovesTheRestriction()
        {
            CaptureProtection.Apply((IntPtr)42, false, _api);
            Assert.That(_api.For((IntPtr)42), Is.EqualTo(new[] { 0x0u }));
        }

        [Test]
        public void Apply_ExcludeRefused_FallsBackToMonitor()
        {
            _api.Results[0x11] = false;
            CaptureProtection.Apply((IntPtr)7, true, _api);
            Assert.That(_api.For((IntPtr)7), Is.EqualTo(new[] { 0x11u, 0x1u }), "a black box in captures is still protection");
        }

        [Test]
        public void Apply_ExcludeThrows_FallsBackToMonitor()
        {
            _api.Throws[0x11] = new ArgumentException("x");
            Assert.DoesNotThrow(() => CaptureProtection.Apply((IntPtr)7, true, _api));
            Assert.That(_api.For((IntPtr)7), Is.EqualTo(new[] { 0x11u, 0x1u }));
        }

        [Test]
        public void Apply_EverythingFails_IsSwallowed()
        {
            _api.Throws[0x11] = new EntryPointNotFoundException("SetWindowDisplayAffinity");
            _api.Throws[0x1] = new DllNotFoundException("user32.dll");
            _api.Throws[0x0] = new InvalidOperationException("x");
            Assert.DoesNotThrow(() => CaptureProtection.Apply((IntPtr)7, true, _api));
            Assert.DoesNotThrow(() => CaptureProtection.Apply((IntPtr)7, false, _api));
            _api.Throws.Clear();
            _api.Results[0x11] = false;
            _api.Results[0x1] = false;
            Assert.DoesNotThrow(() => CaptureProtection.Apply((IntPtr)8, true, _api), "both refused: ignored");
        }

        [Test]
        public void Apply_TurningOffIsRefused_NoMonitorFallback()
        {
            _api.Results[0x0] = false;
            CaptureProtection.Apply((IntPtr)7, false, _api);
            Assert.That(_api.For((IntPtr)7), Is.EqualTo(new[] { 0x0u }), "off never asks for a protection");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyWindow_NoApiOrNoHandleOrHandleThrows_DoesNothing()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var w = new PromptWindow("t", "l", "x");
            try
            {
                CaptureProtection.Apply(w); // no handle yet
                Assert.That(_api.Calls, Is.Empty);
                _handles[w] = (IntPtr)5;
                CaptureProtection.Api = null;
                Assert.DoesNotThrow(() => CaptureProtection.Apply(w));
                CaptureProtection.Api = _api;
                CaptureProtection.HandleOf = x => { throw new InvalidOperationException("x"); };
                Assert.DoesNotThrow(() => CaptureProtection.Apply(w));
                Assert.That(_api.Calls, Is.Empty);
                Assert.DoesNotThrow(() => CaptureProtection.Apply((Window)null));
            }
            finally
            {
                w.Close();
            }
        }

        // ---- every window ----

        private static Window[] NewWindows()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            return new Window[]
            {
                new MainWindow(),
                new ConfirmDefaultNoWindow("Weak password", "m"),
                new ConfirmWindow("Delete note", "m"),
                new CloseChoiceWindow("t", "m"),
                new PromptWindow("Rename note", "Title:", "x"),
                new SettingsWindow(new SettingsViewModel(new AppSettings())),
                new ShortcutsWindow(),
                new AboutWindow(t => { }),
                new ChangePasswordWindow(null)
            };
        }

        private static void CloseAll(IEnumerable<Window> windows)
        {
            foreach (Window w in windows) w.Close();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        }

        private IntPtr CreateFakeHwnd(Window w)
        {
            var h = (IntPtr)(2000 + _handles.Count);
            _handles[w] = h;
            MethodInfo raise = typeof(Window).GetMethod("OnSourceInitialized", BindingFlags.Instance | BindingFlags.NonPublic);
            raise.Invoke(w, new object[] { EventArgs.Empty });
            return h;
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EveryWindowType_IsCovered()
        {
            Window[] windows = NewWindows();
            try
            {
                string[] types = typeof(MainWindow).Assembly.GetTypes()
                    .Where(t => typeof(Window).IsAssignableFrom(t)).Select(t => t.Name).OrderBy(n => n).ToArray();
                Assert.That(windows.Select(w => w.GetType().Name).OrderBy(n => n).ToArray(), Is.EqualTo(types),
                    "a new window type must be added here (and call CaptureProtection.Attach)");
            }
            finally
            {
                CloseAll(windows);
            }
        }

        [TestCase(true, 0x11u)]
        [TestCase(false, 0x0u)]
        [Apartment(ApartmentState.STA)]
        public void EveryWindow_OnSourceInitialized_AppliesTheSetting(bool on, uint affinity)
        {
            CaptureProtection.SetEnabled(on, null);
            Window[] windows = NewWindows();
            try
            {
                foreach (Window w in windows)
                {
                    IntPtr hwnd = CreateFakeHwnd(w);
                    Assert.That(_api.Calls.Select(c => c.Item1).Distinct(), Is.EqualTo(new[] { hwnd }), w.GetType().Name);
                    Assert.That(_api.For(hwnd), Is.EqualTo(new[] { affinity }), w.GetType().Name);
                    _api.Calls.Clear();
                }
            }
            finally
            {
                CloseAll(windows);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SetEnabled_ReappliesToOpenWindows_AndLaterWindowsUseTheNewValue()
        {
            Window[] windows = NewWindows();
            try
            {
                Window[] open = windows.Take(3).ToArray();
                Window late = windows[3];
                IntPtr[] handles = open.Select(CreateFakeHwnd).ToArray();
                _api.Calls.Clear();

                CaptureProtection.SetEnabled(false, windows); // a window without an HWND yet is skipped
                Assert.That(CaptureProtection.IsOn, Is.False);
                foreach (IntPtr h in handles) Assert.That(_api.For(h), Is.EqualTo(new[] { 0x0u }));
                Assert.That(_api.Calls.Count, Is.EqualTo(handles.Length));

                IntPtr lateHwnd = CreateFakeHwnd(late);
                Assert.That(_api.For(lateHwnd), Is.EqualTo(new[] { 0x0u }), "a window created after the change");

                _api.Calls.Clear();
                CaptureProtection.SetEnabled(true, open);
                foreach (IntPtr h in handles) Assert.That(_api.For(h), Is.EqualTo(new[] { 0x11u }));
            }
            finally
            {
                CloseAll(windows);
            }
        }

        [Test]
        public void SetEnabled_EnumerationThrows_StillRecordsTheValue()
        {
            Assert.DoesNotThrow(() => CaptureProtection.SetEnabled(false, new ThrowingEnumerable()));
            Assert.That(CaptureProtection.IsOn, Is.False);
        }

        private sealed class ThrowingEnumerable : IEnumerable
        {
            public IEnumerator GetEnumerator() { throw new InvalidOperationException("x"); }
        }

        // ---- the setting ----

        [Test]
        public void AppSettings_DefaultsToOn_AndNormalizedKeepsTheValue()
        {
            Assert.That(new AppSettings().HideFromScreenCapture, Is.True);
            Assert.That(new AppSettings { HideFromScreenCapture = false }.Normalized().HideFromScreenCapture, Is.False);
            Assert.That(new AppSettings().Normalized().HideFromScreenCapture, Is.True);
        }

        [Test]
        public void AppSettings_OldFileWithoutTheValue_LoadsAsOn()
        {
            string json = "{\"ClipboardClearSeconds\":20,\"FontSize\":14,\"IdleLockMinutes\":5,\"LastNotebookPath\":null,"
                + "\"Theme\":\"Dark\",\"WordWrap\":true}";
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var s = (AppSettings)ser.ReadObject(ms);
                Assert.That(s.HideFromScreenCapture, Is.True);
            }
        }

        [Test]
        public void SettingsStore_RoundTripsOff()
        {
            var store = new SettingsStore(_dir.File("settings"));
            store.Save(new AppSettings { HideFromScreenCapture = false });
            Assert.That(store.Load().HideFromScreenCapture, Is.False);
            store.Save(new AppSettings { HideFromScreenCapture = true });
            Assert.That(store.Load().HideFromScreenCapture, Is.True);
        }

        [Test]
        public void SettingsViewModel_ReadsAndAppliesTheValue_KeepsOthers()
        {
            var target = new AppSettings { HideFromScreenCapture = true, IdleLockMinutes = 9, Theme = "Dark" };
            var vm = new SettingsViewModel(target);
            Assert.That(vm.HideFromScreenCapture, Is.True);
            vm.HideFromScreenCapture = false;
            AppSettings result = vm.ApplyTo(target);
            Assert.That(result.HideFromScreenCapture, Is.False);
            Assert.That(result.IdleLockMinutes, Is.EqualTo(9));
            Assert.That(target.HideFromScreenCapture, Is.True, "the target itself is unchanged");
            Assert.That(new SettingsViewModel(new AppSettings { HideFromScreenCapture = false }).HideFromScreenCapture, Is.False);
        }

        private ShellViewModel NewShell(FakeDialogService dialogs, SettingsStore store, AppSettings settings)
        {
            return new ShellViewModel(new NotebookStore(), store, settings, dialogs, new FakeClipboardService(),
                new FakeClock(), TestKdf.Fast);
        }

        [Test]
        public void OpenSettings_Ok_SavesAndAppliesTheValueToTheApp()
        {
            var dialogs = new FakeDialogService();
            var store = new SettingsStore(_dir.File("settings"));
            var settings = new AppSettings();
            ShellViewModel shell = NewShell(dialogs, store, settings);
            var applied = new List<bool>();
            shell.ApplyCaptureProtection = applied.Add;
            dialogs.SettingsResult = true;
            dialogs.OnShowSettings = vm => vm.HideFromScreenCapture = false;

            shell.OpenSettings();

            Assert.That(settings.HideFromScreenCapture, Is.False, "the shared instance");
            Assert.That(store.Load().HideFromScreenCapture, Is.False, "saved");
            Assert.That(applied, Is.EqualTo(new[] { false }));

            dialogs.OnShowSettings = vm => vm.HideFromScreenCapture = true;
            shell.OpenSettings();
            Assert.That(applied, Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public void OpenSettings_Cancel_AppliesNothing()
        {
            var dialogs = new FakeDialogService();
            var settings = new AppSettings();
            ShellViewModel shell = NewShell(dialogs, new SettingsStore(_dir.File("settings")), settings);
            var applied = new List<bool>();
            shell.ApplyCaptureProtection = applied.Add;
            dialogs.SettingsResult = false;
            dialogs.OnShowSettings = vm => vm.HideFromScreenCapture = false;

            shell.OpenSettings();

            Assert.That(settings.HideFromScreenCapture, Is.True);
            Assert.That(applied, Is.Empty);
        }

        [Test]
        public void ShellDefault_ApplyCaptureProtection_IsTheAppWideOne()
        {
            ShellViewModel shell = NewShell(new FakeDialogService(), new SettingsStore(_dir.File("settings")), new AppSettings());
            Assert.That(shell.ApplyCaptureProtection.Method, Is.EqualTo(typeof(CaptureProtection).GetMethod("ApplyToApp",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)));
            Assert.DoesNotThrow(() => shell.ApplyCaptureProtection(false), "no Application in tests: records the value only");
            Assert.That(CaptureProtection.IsOn, Is.False);
        }

        [Test]
        public void OpenSettings_ThemeThrows_CaptureProtectionIsStillApplied()
        {
            var dialogs = new FakeDialogService();
            var settings = new AppSettings { HideFromScreenCapture = false };
            ShellViewModel shell = NewShell(dialogs, new SettingsStore(_dir.File("settings")), settings);
            var applied = new List<bool>();
            shell.ApplyCaptureProtection = applied.Add;
            shell.ApplyTheme = t => { throw new InvalidOperationException("theme"); };
            dialogs.SettingsResult = true;
            dialogs.OnShowSettings = vm => { vm.HideFromScreenCapture = true; vm.Theme = AppTheme.Dark; };

            Assert.Throws<InvalidOperationException>(() => shell.OpenSettings());

            Assert.That(applied, Is.EqualTo(new[] { true }), "the security setting is applied before the theme");
        }

        // ---- confirmations are LockPad windows (protected), not Win32 message boxes ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ConfirmWindow_YesIsDefault_NoIsCancel_ClosingIsNo()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var win = new ConfirmWindow("Delete note", "Delete \"Ledger\"?");
            try
            {
                Assert.That(win.Title, Is.EqualTo("Delete note"));
                Assert.That(((TextBlock)win.FindName("MessageText")).Text, Is.EqualTo("Delete \"Ledger\"?"));
                var yes = (Button)win.FindName("YesButton");
                var no = (Button)win.FindName("NoButton");
                Assert.That(yes.Content, Is.EqualTo("_Yes"));
                Assert.That(no.Content, Is.EqualTo("_No"));
                Assert.That(yes.IsDefault, Is.True, "Enter = Yes, as the Win32 Yes/No box it replaces");
                Assert.That(no.IsCancel, Is.True, "Esc = No");
                Assert.That(yes.IsCancel || no.IsDefault, Is.False);
                Assert.That(win.Confirmed, Is.False, "closing the window = No");
                yes.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.That(win.Confirmed, Is.True);
            }
            finally
            {
                win.Close();
            }
        }

        [Test]
        public void DialogService_Confirm_UsesTheProtectedWindow_NoWin32YesNoBoxLeft()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LockPad.sln"))) dir = Path.GetDirectoryName(dir);
            string src = File.ReadAllText(Path.Combine(dir, "src", "LockPad.App", "Services", "DialogService.cs"));
            Assert.That(src, Does.Contain("new ConfirmWindow(title, message)"));
            Assert.That(src, Does.Not.Contain("MessageBoxButton.YesNo,"), "the Delete confirmation shows a note title");
        }

        // ---- the Settings dialog ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SettingsWindow_HasTheCheckBox_BoundToTheValue_WithTheHonestNote()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var vm = new SettingsViewModel(new AppSettings());
            var win = new SettingsWindow(vm);
            try
            {
                var box = (CheckBox)win.FindName("CaptureProtectionBox");
                Assert.That(box, Is.Not.Null);
                Assert.That(box.Content, Is.EqualTo("_Hide LockPad from screenshots and screen sharing"));
                BindingExpression b = box.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty);
                Assert.That(b, Is.Not.Null);
                Assert.That(b.ParentBinding.Path.Path, Is.EqualTo("HideFromScreenCapture"));
                var note = (TextBlock)win.FindName("CaptureProtectionNote");
                Assert.That(note.Text, Does.Contain("Turn off to take screenshots of LockPad yourself"));
                Assert.That(note.Text, Does.Contain("phone camera"));
            }
            finally
            {
                win.Close();
            }
        }

        // ---- startup wiring (WPF startup cannot be unit-tested: the source is checked) ----
        // A heuristic: it checks text order against the first "new MainWindow", not every way a window could be made.

        [Test]
        public void AppStartup_SetsTheRealApiAndTheSavedValue_BeforeTheMainWindowExists()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LockPad.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            string app = File.ReadAllText(Path.Combine(dir, "src", "LockPad.App", "App.xaml.cs"));
            int api = app.IndexOf("CaptureProtection.Api = new NativeDisplayAffinityApi();", StringComparison.Ordinal);
            int value = app.IndexOf("CaptureProtection.SetEnabled(Settings.HideFromScreenCapture, null);", StringComparison.Ordinal);
            int window = app.IndexOf("new MainWindow", StringComparison.Ordinal);
            int load = app.IndexOf("Settings = SettingsStore.Load();", StringComparison.Ordinal);
            Assert.That(api, Is.GreaterThan(0));
            Assert.That(value, Is.GreaterThan(load), "after the settings are read");
            Assert.That(api, Is.LessThan(window));
            Assert.That(value, Is.LessThan(window), "before any window can be created");
        }
    }
}

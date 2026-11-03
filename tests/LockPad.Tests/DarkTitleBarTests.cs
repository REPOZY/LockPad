using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using LockPad.App;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// Dark title bars in the Dark theme: every LockPad window asks DWM for DWMWA_USE_IMMERSIVE_DARK_MODE (attribute 20,
    /// 19 on older Windows 10 builds) when its HWND is created and again on a theme change. Only fakes are used: the real
    /// DWM is never called, and no real HWND is created (windows get fake handles through DarkTitleBar.HandleOf, and
    /// SourceInitialized is raised the way Window raises it when it creates its HWND).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class DarkTitleBarTests
    {
        private const int EInvalidArg = unchecked((int)0x80070057);

        private sealed class FakeDwm : IDwmApi
        {
            public readonly List<Tuple<IntPtr, int, int>> Calls = new List<Tuple<IntPtr, int, int>>();
            public readonly Dictionary<int, int> Results = new Dictionary<int, int>();
            public readonly Dictionary<int, Exception> Throws = new Dictionary<int, Exception>();

            public int SetWindowAttribute(IntPtr hwnd, int attribute, int value)
            {
                Calls.Add(Tuple.Create(hwnd, attribute, value));
                Exception ex;
                if (Throws.TryGetValue(attribute, out ex)) throw ex;
                int r;
                return Results.TryGetValue(attribute, out r) ? r : 0;
            }

            /// <summary>"attribute=value" per call for one window.</summary>
            public string[] For(IntPtr hwnd)
            {
                return Calls.Where(c => c.Item1 == hwnd).Select(c => c.Item2 + "=" + c.Item3).ToArray();
            }
        }

        private FakeDwm _dwm;
        private Func<Window, IntPtr> _realHandleOf;
        private Dictionary<Window, IntPtr> _handles;

        [SetUp]
        public void SetUp()
        {
            _dwm = new FakeDwm();
            _handles = new Dictionary<Window, IntPtr>();
            _realHandleOf = DarkTitleBar.HandleOf;
            DarkTitleBar.Api = _dwm;
            DarkTitleBar.HandleOf = w =>
            {
                IntPtr h;
                return _handles.TryGetValue(w, out h) ? h : IntPtr.Zero;
            };
            DarkTitleBar.SetTheme(false, null);
        }

        [TearDown]
        public void TearDown()
        {
            DarkTitleBar.Api = null;
            DarkTitleBar.HandleOf = _realHandleOf;
            DarkTitleBar.SetTheme(false, null);
        }

        // ---- the DWM request ----

        [Test]
        public void Apply_Dark_RequestsAttribute20On()
        {
            DarkTitleBar.Apply((IntPtr)42, true, _dwm);
            Assert.That(_dwm.For((IntPtr)42), Is.EqualTo(new[] { "20=1" }));
        }

        [Test]
        public void Apply_Light_RequestsAttribute20Off()
        {
            DarkTitleBar.Apply((IntPtr)42, false, _dwm);
            Assert.That(_dwm.For((IntPtr)42), Is.EqualTo(new[] { "20=0" }));
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        public void Apply_Attribute20Refused_FallsBackTo19(bool dark, int value)
        {
            _dwm.Results[20] = EInvalidArg;
            DarkTitleBar.Apply((IntPtr)7, dark, _dwm);
            Assert.That(_dwm.For((IntPtr)7), Is.EqualTo(new[] { "20=" + value, "19=" + value }));
        }

        [Test]
        public void Apply_Attribute20Throws_FallsBackTo19()
        {
            _dwm.Throws[20] = new ArgumentException("x");
            Assert.DoesNotThrow(() => DarkTitleBar.Apply((IntPtr)7, true, _dwm));
            Assert.That(_dwm.For((IntPtr)7), Is.EqualTo(new[] { "20=1", "19=1" }));
        }

        [Test]
        public void Apply_MissingExportOrDll_IsSwallowed()
        {
            _dwm.Throws[20] = new EntryPointNotFoundException("DwmSetWindowAttribute");
            _dwm.Throws[19] = new DllNotFoundException("dwmapi.dll");
            Assert.DoesNotThrow(() => DarkTitleBar.Apply((IntPtr)7, true, _dwm));
            _dwm.Throws.Clear();
            _dwm.Results[20] = EInvalidArg;
            _dwm.Results[19] = EInvalidArg;
            Assert.DoesNotThrow(() => DarkTitleBar.Apply((IntPtr)8, true, _dwm), "both refused: ignored");
        }

        [Test]
        public void Attribute_Values()
        {
            Assert.That(DarkTitleBar.UseImmersiveDarkMode, Is.EqualTo(20));
            Assert.That(DarkTitleBar.UseImmersiveDarkModeBefore20H1, Is.EqualTo(19));
        }

        // ---- the effective theme ----

        [TestCase(AppTheme.Dark, true)]
        [TestCase(AppTheme.Light, false)]
        public void ResolveDark_ExplicitTheme_IgnoresTheSystemSetting(AppTheme theme, bool expected)
        {
            bool read = false;
            Assert.That(ThemeService.ResolveDark(theme, () => { read = true; return 0; }), Is.EqualTo(expected));
            Assert.That(read, Is.False, "the system setting is only read for System");
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(null, false)]
        public void ResolveDark_System_FollowsAppsUseLightTheme(object appsUseLightTheme, bool expected)
        {
            Assert.That(ThemeService.ResolveDark(AppTheme.System, () => appsUseLightTheme), Is.EqualTo(expected));
        }

        // ---- every window, on creation and on a theme change ----

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

        /// <summary>What Window does right after creating its HWND (Window.CreateSourceWindow calls OnSourceInitialized,
        /// which raises SourceInitialized), given a fake handle instead of a real one.</summary>
        private IntPtr CreateFakeHwnd(Window w)
        {
            var h = (IntPtr)(1000 + _handles.Count);
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
                Assert.That(windows.Select(w => w.GetType().Name).OrderBy(n => n).ToArray(), Is.EqualTo(types));
            }
            finally
            {
                CloseAll(windows);
            }
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        [Apartment(ApartmentState.STA)]
        public void EveryWindow_OnSourceInitialized_RequestsTheThemeValue(bool dark, int value)
        {
            DarkTitleBar.SetTheme(dark, null);
            Window[] windows = NewWindows();
            try
            {
                foreach (Window w in windows)
                {
                    IntPtr hwnd = CreateFakeHwnd(w);
                    Assert.That(_dwm.Calls.Select(c => c.Item1).Distinct(), Is.EqualTo(new[] { hwnd }), w.GetType().Name);
                    Assert.That(_dwm.For(hwnd), Is.EqualTo(new[] { "20=" + value }), w.GetType().Name);
                    _dwm.Calls.Clear();
                }
            }
            finally
            {
                CloseAll(windows);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ThemeChange_ReappliesToOpenWindows_AndLaterWindowsUseTheNewTheme()
        {
            Window[] windows = NewWindows();
            Func<bool> savedHighContrast = ThemeService.HighContrast;
            ThemeService.HighContrast = () => false;
            try
            {
                Window[] open = windows.Take(3).ToArray();     // MainWindow and two dialogs
                Window late = windows[3];
                IntPtr[] handles = open.Select(CreateFakeHwnd).ToArray();
                Assert.That(handles.Select(h => _dwm.For(h)), Has.All.EqualTo(new[] { "20=0" }), "created in Light");
                _dwm.Calls.Clear();
                var merged = new ResourceDictionary().MergedDictionaries;

                // What ThemeService.Apply does after Settings: the theme dictionary, then every window of the app. A
                // window without an HWND yet is skipped; it asks on SourceInitialized.
                ThemeService.ApplyResolved(merged, windows, true);
                Assert.That(merged.Count, Is.EqualTo(2));
                Assert.That(merged[0].Source.OriginalString, Does.EndWith("Themes/Dark.xaml"), "the colours, then the control styles");
                Assert.That(merged[1].Source.OriginalString, Does.EndWith("Themes/Controls.xaml"));
                foreach (IntPtr h in handles) Assert.That(_dwm.For(h), Is.EqualTo(new[] { "20=1" }));
                Assert.That(_dwm.Calls.Count, Is.EqualTo(handles.Length));

                IntPtr lateHwnd = CreateFakeHwnd(late);
                Assert.That(_dwm.For(lateHwnd), Is.EqualTo(new[] { "20=1" }), "a window created after the change");

                _dwm.Calls.Clear();
                ThemeService.ApplyResolved(merged, open, false);
                Assert.That(merged.Count, Is.EqualTo(2));
                Assert.That(merged[0].Source.OriginalString, Does.EndWith("Themes/Light.xaml"), "the colours, then the control styles");
                Assert.That(merged[1].Source.OriginalString, Does.EndWith("Themes/Controls.xaml"));
                foreach (IntPtr h in handles) Assert.That(_dwm.For(h), Is.EqualTo(new[] { "20=0" }));
            }
            finally
            {
                ThemeService.HighContrast = savedHighContrast;
                CloseAll(windows);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DwmFailure_NeverBlocksAWindow()
        {
            _dwm.Throws[20] = new EntryPointNotFoundException("DwmSetWindowAttribute");
            _dwm.Throws[19] = new DllNotFoundException("dwmapi.dll");
            DarkTitleBar.SetTheme(true, null);
            Window[] windows = NewWindows();
            try
            {
                foreach (Window w in windows)
                    Assert.DoesNotThrow(() => CreateFakeHwnd(w), w.GetType().Name);
                Assert.That(_dwm.Calls.Count, Is.EqualTo(2 * windows.Length), "20, then 19, per window");
                Assert.DoesNotThrow(() => DarkTitleBar.SetTheme(false, windows));

                DarkTitleBar.HandleOf = w => { throw new InvalidOperationException("x"); };
                Assert.DoesNotThrow(() => DarkTitleBar.SetTheme(true, windows), "a failing handle lookup is ignored");
            }
            finally
            {
                CloseAll(windows);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void NoApi_OrNoHwnd_DoesNothing()
        {
            Window[] windows = NewWindows();
            try
            {
                DarkTitleBar.SetTheme(true, windows);
                Assert.That(_dwm.Calls, Is.Empty, "no window has an HWND yet");

                DarkTitleBar.Api = null;
                Assert.DoesNotThrow(() => CreateFakeHwnd(windows[1]));
                DarkTitleBar.SetTheme(false, windows);
                Assert.That(_dwm.Calls, Is.Empty);
            }
            finally
            {
                CloseAll(windows);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void RealHandleLookup_IsZeroBeforeTheHwndExists()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var w = new PromptWindow("t", "l", "x");
            try
            {
                Assert.That(_realHandleOf(w), Is.EqualTo(IntPtr.Zero));
            }
            finally
            {
                CloseAll(new Window[] { w });
            }
        }
    }
}

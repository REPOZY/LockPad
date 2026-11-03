using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using LockPad.App.Services;
using Microsoft.Win32;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// Fix round 1, X8 (review R4/R9, red team L3): while Windows high contrast is on, a change of Windows' colours (e.g.
    /// from one high-contrast scheme to another) re-applies the system colours; the "HighContrast" setting change
    /// re-applies the theme. Both arrive through SystemThemeWatcher, which marshals to the UI thread, never throws and
    /// unsubscribes on exit.
    /// </summary>
    [TestFixture]
    public class SystemThemeWatcherTests
    {
        private PropertyChangedEventHandler _parameterHandler;
        private UserPreferenceChangedEventHandler _preferenceHandler;
        private int _parameterRemoved, _preferenceRemoved;
        private readonly List<Action> _posted = new List<Action>();
        private int _highContrastReapplied, _coloursReapplied;

        [SetUp]
        public void SetUp()
        {
            _parameterHandler = null;
            _preferenceHandler = null;
            _parameterRemoved = _preferenceRemoved = 0;
            _posted.Clear();
            _highContrastReapplied = _coloursReapplied = 0;
        }

        private SystemThemeWatcher NewWatcher(Action onHighContrast = null, Action onColours = null, Action<Action> post = null)
        {
            return new SystemThemeWatcher(
                h => _parameterHandler += h,
                h => { _parameterHandler -= h; _parameterRemoved++; },
                h => _preferenceHandler += h,
                h => { _preferenceHandler -= h; _preferenceRemoved++; },
                post ?? (a => _posted.Add(a)),
                onHighContrast ?? (() => _highContrastReapplied++),
                onColours ?? (() => _coloursReapplied++));
        }

        private void RunPosted()
        {
            Action[] posted = _posted.ToArray();
            _posted.Clear();
            foreach (Action a in posted) a();
        }

        [Test]
        public void Subscribes_ToBothNotifications_AndUnsubscribesOnDispose()
        {
            SystemThemeWatcher watcher = NewWatcher();
            Assert.That(_parameterHandler, Is.Not.Null, "SystemParameters.StaticPropertyChanged");
            Assert.That(_preferenceHandler, Is.Not.Null, "SystemEvents.UserPreferenceChanged");

            watcher.Dispose();
            Assert.That(_parameterHandler, Is.Null, "the static events hold no reference after exit");
            Assert.That(_preferenceHandler, Is.Null);

            watcher.Dispose();
            Assert.That(_parameterRemoved, Is.EqualTo(1), "a second Dispose does nothing");
            Assert.That(_preferenceRemoved, Is.EqualTo(1));
        }

        [Test]
        public void HighContrastSetting_IsMarshalled_ThenReappliesTheTheme_OtherSettingsIgnored()
        {
            NewWatcher();
            _parameterHandler(null, new PropertyChangedEventArgs("FocusVisualStyleKey"));
            _parameterHandler(null, new PropertyChangedEventArgs("HighContrastKey"));
            _parameterHandler(null, new PropertyChangedEventArgs(null));
            Assert.That(_posted, Is.Empty, "only \"HighContrast\" counts");

            _parameterHandler(null, new PropertyChangedEventArgs("HighContrast"));
            Assert.That(_highContrastReapplied, Is.EqualTo(0), "not inside the notification: queued to the UI thread");
            Assert.That(_posted.Count, Is.EqualTo(1));
            RunPosted();
            Assert.That(_highContrastReapplied, Is.EqualTo(1));
            Assert.That(_coloursReapplied, Is.EqualTo(0));
        }

        [TestCase(UserPreferenceCategory.Color, true)]
        [TestCase(UserPreferenceCategory.Accessibility, true)]
        [TestCase(UserPreferenceCategory.VisualStyle, true)]
        [TestCase(UserPreferenceCategory.General, true)]
        [TestCase(UserPreferenceCategory.Keyboard, false)]
        [TestCase(UserPreferenceCategory.Mouse, false)]
        [TestCase(UserPreferenceCategory.Desktop, false)]
        [TestCase(UserPreferenceCategory.Window, false)]
        [TestCase(UserPreferenceCategory.Locale, false)]
        [TestCase(UserPreferenceCategory.Power, false)]
        public void ColourPreferenceChange_IsMarshalled_ThenReappliesTheColours(UserPreferenceCategory category, bool reapplies)
        {
            NewWatcher();
            _preferenceHandler(null, new UserPreferenceChangedEventArgs(category));
            Assert.That(_coloursReapplied, Is.EqualTo(0), "never inside the notification (it may come from another thread)");
            RunPosted();
            Assert.That(_coloursReapplied, Is.EqualTo(reapplies ? 1 : 0), category.ToString());
            Assert.That(_highContrastReapplied, Is.EqualTo(0));
        }

        [Test]
        public void NeverThrows_NotWhenQueueingFails_NorWhenTheReapplyFails()
        {
            NewWatcher(post: a => { throw new InvalidOperationException("dispatcher shut down"); });
            Assert.DoesNotThrow(() => _parameterHandler(null, new PropertyChangedEventArgs("HighContrast")));
            Assert.DoesNotThrow(() => _preferenceHandler(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color)));

            SetUp();
            NewWatcher(() => { throw new InvalidOperationException("theme"); }, () => { throw new IOException("colours"); });
            _parameterHandler(null, new PropertyChangedEventArgs("HighContrast"));
            _preferenceHandler(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color));
            Assert.That(_posted.Count, Is.EqualTo(2));
            Assert.DoesNotThrow(RunPosted);
        }

        [Test]
        public void AfterDispose_ANotificationAlreadyInFlight_DoesNothing()
        {
            SystemThemeWatcher watcher = NewWatcher();
            PropertyChangedEventHandler parameter = _parameterHandler;
            UserPreferenceChangedEventHandler preference = _preferenceHandler;
            parameter(null, new PropertyChangedEventArgs("HighContrast")); // queued before exit

            watcher.Dispose();
            parameter(null, new PropertyChangedEventArgs("HighContrast"));
            preference(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color));
            RunPosted();

            Assert.That(_highContrastReapplied, Is.EqualTo(0), "nothing re-applies after exit");
            Assert.That(_coloursReapplied, Is.EqualTo(0));
        }

        // ---- ThemeService: the colours are re-applied only while high contrast is on ----

        private static bool IsSource(ResourceDictionary d, string name)
        {
            return d.Source != null && d.Source.OriginalString.EndsWith("Themes/" + name + ".xaml", StringComparison.Ordinal);
        }

        private static readonly Color[] SchemeA = { Colors.Black, Colors.White, Colors.Cyan };
        private static readonly Color[] SchemeB = { Colors.White, Colors.Black, Colors.Purple };

        /// <summary>Runs <paramref name="body"/> with the high-contrast and system-colour seams set, then restores them.</summary>
        private static void WithSeams(Func<bool> highContrast, Func<Color[]> colours, Action body)
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            Func<bool> savedHc = ThemeService.HighContrast;
            Func<Color[]> savedColours = ThemeService.ReadSystemColours;
            try
            {
                ThemeService.HighContrast = highContrast;
                ThemeService.ReadSystemColours = colours;
                body();
            }
            finally
            {
                ThemeService.HighContrast = savedHc;
                ThemeService.ReadSystemColours = savedColours;
            }
        }

        private static ResourceDictionary HcColours(System.Collections.ObjectModel.Collection<ResourceDictionary> merged)
        {
            return merged.Single(d => d.Contains(ThemeService.HighContrastMarkerKey));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ReapplyIfHighContrast_On_RebuildsTheSystemColoursWhenTheyChanged_Off_ChangesNothing()
        {
            bool hc = true;
            Color[] colours = SchemeA;
            WithSeams(() => hc, () => colours, () =>
            {
                var merged = new ResourceDictionary().MergedDictionaries;
                var other = new ResourceDictionary();
                merged.Add(other);

                ThemeService.ApplyResolved(merged, null, true);
                ResourceDictionary first = HcColours(merged);

                colours = SchemeB; // e.g. High Contrast Black -> High Contrast White
                Assert.That(ThemeService.ReapplyIfHighContrast(merged, null), Is.True);
                ResourceDictionary second = HcColours(merged);
                Assert.That(second, Is.Not.SameAs(first), "rebuilt from the current system colours");
                Assert.That(merged.Count, Is.EqualTo(2), "other, high-contrast colours");
                Assert.That(merged.Contains(other), Is.True);
                Assert.That(((SolidColorBrush)second["WindowBackground"]).Color, Is.EqualTo(SystemColors.WindowColor));

                hc = false;
                ThemeService.ApplyResolved(merged, null, true);
                ResourceDictionary[] before = merged.ToArray();
                Assert.That(before.Any(d => IsSource(d, "Dark")), Is.True);
                colours = SchemeA;
                Assert.That(ThemeService.ReapplyIfHighContrast(merged, null), Is.False);
                Assert.That(merged.ToArray(), Is.EqualTo(before), "normal contrast: the theme files stay, untouched");
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ReapplyIfHighContrast_UnchangedColours_NoSwap()
        {
            // Fix round 2, Y4 (red team L4a): any General / VisualStyle notification used to swap the dictionaries.
            Color[] colours = SchemeA;
            WithSeams(() => true, () => colours, () =>
            {
                var merged = new ResourceDictionary().MergedDictionaries;
                ThemeService.ApplyResolved(merged, null, true);
                ResourceDictionary first = HcColours(merged);

                colours = (Color[])SchemeA.Clone(); // equal values, another array
                Assert.That(ThemeService.ReapplyIfHighContrast(merged, null), Is.False, "nothing changed");
                Assert.That(HcColours(merged), Is.SameAs(first), "no swap");

                colours = SchemeB;
                Assert.That(ThemeService.ReapplyIfHighContrast(merged, null), Is.True, "changed");
                Assert.That(HcColours(merged), Is.Not.SameAs(first));
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SystemColoursChanged_QueuesOneMoreCheck_ThatPicksUpALateChange()
        {
            // Fix round 2, Y4 (red team L4b): the notification may arrive before every system colour is updated.
            Color[] colours = SchemeA;
            WithSeams(() => true, () => colours, () =>
            {
                var merged = new ResourceDictionary().MergedDictionaries;
                var posted = new List<Action>();
                ThemeService.ApplyResolved(merged, null, true);
                ResourceDictionary first = HcColours(merged);

                colours = SchemeB;
                ThemeService.OnSystemColoursChanged(merged, null, a => posted.Add(a));
                ResourceDictionary second = HcColours(merged);
                Assert.That(second, Is.Not.SameAs(first), "re-applied at once");
                Assert.That(posted.Count, Is.EqualTo(1), "one more check, queued");

                colours = new[] { Colors.Yellow, Colors.Black, Colors.Lime }; // the rest of the scheme arrives late
                Action check = posted[0];
                posted.Clear();
                check();
                Assert.That(HcColours(merged), Is.Not.SameAs(second), "the queued check picks up the late change");
                Assert.That(posted, Is.Empty, "only ONE more check");

                // Nothing changed meanwhile: the queued check does nothing.
                ResourceDictionary third = HcColours(merged);
                colours = SchemeA;
                ThemeService.OnSystemColoursChanged(merged, null, a => posted.Add(a));
                ResourceDictionary fourth = HcColours(merged);
                Assert.That(fourth, Is.Not.SameAs(third));
                posted.Single()();
                Assert.That(HcColours(merged), Is.SameAs(fourth), "unchanged: no swap");
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SystemColoursChanged_NormalContrast_NoSwap_NoQueuedCheck()
        {
            WithSeams(() => false, () => SchemeA, () =>
            {
                var merged = new ResourceDictionary().MergedDictionaries;
                var posted = new List<Action>();
                ThemeService.ApplyResolved(merged, null, false);
                ResourceDictionary[] before = merged.ToArray();
                ThemeService.OnSystemColoursChanged(merged, null, a => posted.Add(a));
                Assert.That(merged.ToArray(), Is.EqualTo(before));
                Assert.That(posted, Is.Empty);
            });
        }

        [Test]
        public void OnSystemColoursChanged_WithoutAnApplication_DoesNothing()
        {
            Assert.That(Application.Current, Is.Null, "the test host has no WPF Application");
            Assert.DoesNotThrow(ThemeService.OnSystemColoursChanged);
        }

        // ---- the application wires the watcher (App cannot be constructed in a test: one Application per domain) ----

        [Test]
        public void App_StartsTheWatcherOnStartup_AndDisposesItOnExit()
        {
            DirectoryInfo d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "LockPad.sln"))) d = d.Parent;
            Assert.That(d, Is.Not.Null);
            string app = File.ReadAllText(Path.Combine(d.FullName, "src", "LockPad.App", "App.xaml.cs"));

            int startup = app.IndexOf("protected override void OnStartup", StringComparison.Ordinal);
            int exit = app.IndexOf("protected override void OnExit", StringComparison.Ordinal);
            Assert.That(startup, Is.GreaterThan(0));
            Assert.That(exit, Is.GreaterThan(0));
            string startupBody = app.Substring(startup, app.IndexOf("\n        }", startup, StringComparison.Ordinal) - startup);
            string exitBody = app.Substring(exit, app.IndexOf("\n        }", exit, StringComparison.Ordinal) - exit);

            Assert.That(startupBody, Does.Contain("SystemThemeWatcher.ForApplication(Dispatcher)"));
            Assert.That(exitBody, Does.Contain("_themeWatcher.Dispose()"));
            Assert.That(app, Does.Not.Contain("SystemParameters.StaticPropertyChanged"),
                "one place subscribes (the watcher), so one place unsubscribes");
            Assert.That(app, Does.Not.Contain("SystemEvents."));
        }
    }
}

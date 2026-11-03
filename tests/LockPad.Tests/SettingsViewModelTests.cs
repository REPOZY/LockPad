using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LockPad.App;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class SettingsViewModelTests
    {
        private TempDir _dir;
        private FakeDialogService _dialogs;
        private SettingsStore _settingsStore;
        private AppSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
            _settingsStore = new SettingsStore(_dir.File("settings"));
            _settings = new AppSettings();
        }

        [TearDown]
        public void TearDown()
        {
            _dir.Dispose();
        }

        private ShellViewModel NewShell()
        {
            return new ShellViewModel(new NotebookStore(), _settingsStore, _settings, _dialogs,
                new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
        }

        [Test]
        public void Constructor_ReadsCurrentValues()
        {
            var vm = new SettingsViewModel(new AppSettings { IdleLockMinutes = 7, ClipboardClearSeconds = 33, Theme = "Dark" });
            Assert.That(vm.IdleLockMinutes, Is.EqualTo(7));
            Assert.That(vm.ClipboardClearSeconds, Is.EqualTo(33));
            Assert.That(vm.Theme, Is.EqualTo(AppTheme.Dark));
        }

        [Test]
        public void ApplyTo_CopiesValuesAndClamps()
        {
            var target = new AppSettings { LastNotebookPath = "p.kdbx", FontSize = 20, WordWrap = false };
            var vm = new SettingsViewModel(target)
            {
                IdleLockMinutes = 0,
                ClipboardClearSeconds = 500,
                Theme = AppTheme.Dark
            };

            AppSettings result = vm.ApplyTo(target);

            Assert.That(result, Is.Not.SameAs(target));
            Assert.That(result.IdleLockMinutes, Is.EqualTo(1));
            Assert.That(result.ClipboardClearSeconds, Is.EqualTo(120));
            Assert.That(result.Theme, Is.EqualTo("Dark"));
            Assert.That(result.LastNotebookPath, Is.EqualTo("p.kdbx"));
            Assert.That(result.FontSize, Is.EqualTo(20));
            Assert.That(result.WordWrap, Is.False);
            // The target instance itself is unchanged.
            Assert.That(target.IdleLockMinutes, Is.EqualTo(5));
            Assert.That(target.ClipboardClearSeconds, Is.EqualTo(20));
            Assert.That(target.Theme, Is.EqualTo("System"));
        }

        [Test]
        public void OpenSettings_Ok_PersistsAndRaisesEvent()
        {
            ShellViewModel shell = NewShell();
            int raised = 0;
            shell.SettingsChanged += (s, e) => raised++;
            _dialogs.SettingsResult = true;
            _dialogs.OnShowSettings = vm =>
            {
                vm.IdleLockMinutes = 2;
                vm.ClipboardClearSeconds = 45;
                vm.Theme = AppTheme.Light;
            };

            shell.OpenSettings();

            AppSettings saved = _settingsStore.Load();
            Assert.That(saved.IdleLockMinutes, Is.EqualTo(2));
            Assert.That(saved.ClipboardClearSeconds, Is.EqualTo(45));
            Assert.That(saved.Theme, Is.EqualTo("Light"));
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(_dialogs.Calls, Does.Contain("ShowSettings"));
        }

        [Test]
        public void OpenSettings_Ok_MutatesSharedInstanceInPlace()
        {
            ShellViewModel shell = NewShell();
            AppSettings original = shell.Settings;
            Func<int> clipboardProvider = () => shell.Settings.ClipboardClearSeconds;
            Assert.That(clipboardProvider(), Is.EqualTo(20));
            _dialogs.SettingsResult = true;
            _dialogs.OnShowSettings = vm => vm.ClipboardClearSeconds = 60;

            shell.OpenSettings();

            Assert.That(ReferenceEquals(shell.Settings, original), Is.True);
            Assert.That(ReferenceEquals(_settings, original), Is.True);
            Assert.That(clipboardProvider(), Is.EqualTo(60));
            Assert.That(_settings.ClipboardClearSeconds, Is.EqualTo(60));
        }

        [Test]
        public void OpenSettings_Ok_ClampsOutOfRangeValues()
        {
            ShellViewModel shell = NewShell();
            _dialogs.SettingsResult = true;
            _dialogs.OnShowSettings = vm => { vm.IdleLockMinutes = 999; vm.ClipboardClearSeconds = 1; };

            shell.OpenSettings();

            Assert.That(shell.Settings.IdleLockMinutes, Is.EqualTo(60));
            Assert.That(shell.Settings.ClipboardClearSeconds, Is.EqualTo(5));
        }

        [Test]
        public void OpenSettings_Ok_KeepsOtherSettings()
        {
            _settings.LastNotebookPath = "keep.kdbx";
            _settings.FontSize = 22;
            _settings.WordWrap = false;
            ShellViewModel shell = NewShell();
            _dialogs.SettingsResult = true;
            _dialogs.OnShowSettings = vm => vm.IdleLockMinutes = 9;

            shell.OpenSettings();

            AppSettings saved = _settingsStore.Load();
            Assert.That(saved.LastNotebookPath, Is.EqualTo("keep.kdbx"));
            Assert.That(saved.FontSize, Is.EqualTo(22));
            Assert.That(saved.WordWrap, Is.False);
        }

        [Test]
        public void OpenSettings_Cancel_ChangesNothing()
        {
            _settings.IdleLockMinutes = 8;
            _settings.ClipboardClearSeconds = 30;
            _settings.Theme = "Dark";
            _settingsStore.Save(_settings);
            ShellViewModel shell = NewShell();
            int raised = 0;
            shell.SettingsChanged += (s, e) => raised++;
            _dialogs.SettingsResult = false;
            _dialogs.OnShowSettings = vm => { vm.IdleLockMinutes = 2; vm.Theme = AppTheme.Light; };

            shell.OpenSettings();

            AppSettings saved = _settingsStore.Load();
            Assert.That(saved.IdleLockMinutes, Is.EqualTo(8));
            Assert.That(saved.ClipboardClearSeconds, Is.EqualTo(30));
            Assert.That(saved.Theme, Is.EqualTo("Dark"));
            Assert.That(shell.Settings.IdleLockMinutes, Is.EqualTo(8));
            Assert.That(shell.Settings.Theme, Is.EqualTo("Dark"));
            Assert.That(raised, Is.EqualTo(0));
        }

        // ---- U1 (user decision 2026-10-19): the theme is set only in Tools > Settings... (View menu removed) ----

        [TestCase(AppTheme.System)]
        [TestCase(AppTheme.Light)]
        [TestCase(AppTheme.Dark)]
        public void OpenSettings_Ok_AppliesAndPersistsTheChosenTheme(AppTheme theme)
        {
            _settings.Theme = theme == AppTheme.Dark ? "Light" : "Dark"; // start from a different theme
            ShellViewModel shell = NewShell();
            var applied = new List<AppTheme>();
            shell.ApplyTheme = t => applied.Add(t);
            int raised = 0;
            shell.SettingsChanged += (s, e) => raised++;
            _dialogs.SettingsResult = true;
            _dialogs.OnShowSettings = vm => vm.Theme = theme;

            shell.OpenSettings();

            Assert.That(applied, Is.EqualTo(new[] { theme }), "applied when OK is pressed");
            Assert.That(_settingsStore.Load().Theme, Is.EqualTo(theme.ToString()));
            Assert.That(shell.Settings.Theme, Is.EqualTo(theme.ToString()));
            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void OpenSettings_Cancel_AppliesNoTheme()
        {
            ShellViewModel shell = NewShell();
            var applied = new List<AppTheme>();
            shell.ApplyTheme = t => applied.Add(t);
            _dialogs.SettingsResult = false;
            _dialogs.OnShowSettings = vm => vm.Theme = AppTheme.Dark;

            shell.OpenSettings();

            Assert.That(applied, Is.Empty);
        }

        [Test]
        public void Shell_HasNoThemeMenuMembers()
        {
            foreach (string name in new[] { "SetThemeCommand", "IsSystemTheme", "IsLightTheme", "IsDarkTheme", "SetTheme" })
                Assert.That(typeof(ShellViewModel).GetMember(name), Is.Empty, name + " only served the removed View menu");
        }

        [Test]
        public void OpenSettingsCommand_ShowsDialog()
        {
            ShellViewModel shell = NewShell();
            shell.OpenSettingsCommand.Execute(null);
            Assert.That(_dialogs.Calls, Does.Contain("ShowSettings"));
        }

        [TestCase("123", true)]
        [TestCase("", true)]
        [TestCase("12a", false)]
        [TestCase("-1", false)]
        [TestCase("1.5", false)]
        [TestCase(" 1", false)]
        public void IsDigitsOnly(string text, bool expected)
        {
            Assert.That(SettingsWindow.IsDigitsOnly(text), Is.EqualTo(expected));
        }

        /// <summary>Runs queued dispatcher work (deferred binding attachment) as the app's message loop would.</summary>
        private static void PumpDispatcher()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        private static IEnumerable<MenuItem> Items(ItemsControl parent)
        {
            return parent.Items.OfType<MenuItem>();
        }

        // ---- UX round 3, V1: the menu bar is File | Settings | Help (Help since 2026-10-21; it was Shortcuts) ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_MenuBar_IsFileSettingsHelp_SettingsIsADirectItem()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                PumpDispatcher();
                var menu = (Menu)window.FindName("MainMenu");
                Assert.That(menu, Is.Not.Null);
                Assert.That(Items(menu).Select(m => (string)m.Header).ToArray(),
                    Is.EqualTo(new[] { "_File", "_Settings", "_Help" }), "no Tools drop-down");

                MenuItem settings = Items(menu).Single(m => (string)m.Header == "_Settings");
                Assert.That(settings.Items, Is.Empty, "a direct item: no empty drop-down");
                Assert.That(settings.Command, Is.SameAs(shell.OpenSettingsCommand));

                // Help's items are checked by HelpAndAboutTests.
                MenuItem help = Items(menu).Single(m => (string)m.Header == "_Help");
                Assert.That(help.Command, Is.Null, "a drop-down");

                // Access keys F, S, H: distinct at the top level.
                char[] keys = Items(menu).Select(m => char.ToUpperInvariant(((string)m.Header)[((string)m.Header).IndexOf('_') + 1]))
                    .ToArray();
                Assert.That(keys, Is.EqualTo(new[] { 'F', 'S', 'H' }));
                Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Length));

                MenuItem file = Items(menu).Single(m => (string)m.Header == "_File");
                Assert.That(Items(file).Select(m => (string)m.Header),
                    Is.EqualTo(new[]
                    {
                        "_New notebook" + (char)0x2026, "_Open notebook" + (char)0x2026, "_Save", "Save _As" + (char)0x2026,
                        "_Change master password" + (char)0x2026, "_Lock", "E_xit",
                    }), "the full File menu (2026-10-21; NotebookSwitchTests checks order, separators and gestures)");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void OpenShortcutsCommand_ShowsTheShortcutsWindow()
        {
            ShellViewModel shell = NewShell();
            shell.OpenShortcutsCommand.Execute(null);
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowShortcuts" }));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShortcutsWindow_ListsEveryTableEntry_GroupedInTwoColumns_EscCloses()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var win = new ShortcutsWindow();
            try
            {
                PumpDispatcher();
                var texts = new List<string>();
                CollectTexts(win, texts);
                foreach (string group in new[] { "Notes", "Editing", "File", "Security", "Help" })
                    Assert.That(texts, Does.Contain(group), "group " + group);
                foreach (ShortcutEntry e in ShortcutTable.Entries)
                {
                    Assert.That(texts, Does.Contain(e.Keys), e.Keys);
                    Assert.That(texts, Does.Contain(e.Action), e.Action);
                }
                Assert.That(texts, Does.Contain(ShortcutTable.LockNote));
                var close = (Button)win.FindName("CloseButton");
                Assert.That(close.IsCancel, Is.True, "Esc closes");
                Assert.That(win.Title, Is.EqualTo("Keyboard shortcuts"));
            }
            finally
            {
                win.Close();
            }
        }

        [Test]
        public void ShortcutTable_HasTheRequiredRows()
        {
            string[] rows = ShortcutTable.Entries.Select(e => e.Group + "|" + e.Keys + "|" + e.Action).ToArray();
            Assert.That(rows, Is.EqualTo(new[]
            {
                "Notes|Ctrl+N|New note",
                "Notes|F2|Rename note",
                "Notes|Del (in the notes list)|Delete note",
                "Notes|Ctrl+Z (in the notes list)|Undo delete",
                "Editing|Ctrl+F|Find in the note",
                "Editing|Enter (in the find box)|Next match",
                "Editing|Esc (in the find box)|Close the find bar",
                "Editing|Alt+Z|Word wrap on or off",
                "Editing|Ctrl+Plus / Ctrl+Minus / Ctrl+0|Zoom in / out / reset",
                "Editing|Ctrl+mouse wheel|Zoom",
                "Editing|Ctrl+Z (in the editor)|Undo typing",
                "File|Ctrl+O|Open another notebook",
                "File|Ctrl+S|Save",
                "File|Ctrl+Shift+S|Save As",
                "Security|Ctrl+L|Lock now",
                "Help|F1|This list of shortcuts",
            }));
            Assert.That(ShortcutTable.LockNote, Is.EqualTo(
                "LockPad also locks when Windows locks, when the PC sleeps, and after the idle time set in Settings."));
        }

        private static void CollectTexts(DependencyObject root, List<string> texts)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var tb = child as TextBlock;
                if (tb != null && tb.Text != null) texts.Add(tb.Text);
                var d = child as DependencyObject;
                if (d != null) CollectTexts(d, texts);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SettingsWindow_ThemeBox_OffersSystemLightDark_AndWritesTheChoice()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var vm = new SettingsViewModel(new AppSettings());
            var win = new SettingsWindow(vm);
            try
            {
                PumpDispatcher();
                var combo = (ComboBox)win.FindName("ThemeBox");
                Assert.That(combo.Items.Cast<object>().ToArray(),
                    Is.EqualTo(new object[] { AppTheme.System, AppTheme.Light, AppTheme.Dark }));
                Assert.That(combo.SelectedItem, Is.EqualTo(AppTheme.System));
                combo.SelectedItem = AppTheme.Dark;
                Assert.That(vm.Theme, Is.EqualTo(AppTheme.Dark));
            }
            finally
            {
                win.Close();
            }
        }

        [TestCase("5", true)]
        [TestCase("120", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("99999999999", false)]
        [TestCase("1a", false)]
        public void IsAcceptable(string text, bool expected)
        {
            Assert.That(SettingsWindow.IsAcceptable(text), Is.EqualTo(expected));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_MenuItems_UseTheThemeInNormalContrast_SystemMenuTextInHighContrast()
        {
            // 2026-10-21: the drop-down is themed now, so nested items use the theme like the top level; in high
            // contrast neither the templates nor the theme colours apply and every item uses the system menu text.
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                foreach (bool highContrast in new[] { false, true })
                {
                    ThemeService.ApplyTo(window.Resources.MergedDictionaries, true, highContrast);
                    PumpDispatcher();
                    var menu = (Menu)window.FindName("MainMenu");
                    // In high contrast: whatever Windows' own menu style gives, i.e. a menu with no LockPad
                    // resources (under a real high-contrast theme that is the system menu text).
                    Color enabled, disabled, topColour;
                    if (highContrast)
                    {
                        var plainTop = new MenuItem { Header = "x" };
                        var plainEnabled = new MenuItem { Header = "y" };
                        var plainDisabled = new MenuItem { Header = "z", IsEnabled = false };
                        plainTop.Items.Add(plainEnabled);
                        plainTop.Items.Add(plainDisabled);
                        var plainMenu = new Menu();
                        plainMenu.Items.Add(plainTop);
                        var plainWindow = new Window { Content = plainMenu };
                        try
                        {
                            plainWindow.ApplyTemplate();
                            plainMenu.ApplyTemplate();
                            PumpDispatcher();
                            topColour = ((SolidColorBrush)plainTop.Foreground).Color;
                            enabled = ((SolidColorBrush)plainEnabled.Foreground).Color;
                            disabled = ((SolidColorBrush)plainDisabled.Foreground).Color;
                        }
                        finally
                        {
                            plainWindow.Close();
                        }
                        Assert.That(menu.Style, Is.Null, "no custom menu style in high contrast");
                    }
                    else
                    {
                        enabled = topColour = ((SolidColorBrush)window.FindResource("EditorForeground")).Color;
                        disabled = ((SolidColorBrush)window.FindResource("DisabledForeground")).Color;
                    }

                    // Walks the whole menu, so an item added later is covered without editing this test.
                    List<MenuItem> topLevel = Items(menu).ToList();
                    Assert.That(topLevel.Count, Is.EqualTo(3), "File, Settings, Help: all styled as top-level items");
                    int nested = 0;
                    var pending = new Stack<MenuItem>();
                    foreach (MenuItem top in topLevel)
                    {
                        Assert.That(((SolidColorBrush)top.Foreground).Color, Is.EqualTo(topColour), "top-level " + top.Header);
                        if (highContrast) Assert.That(top.Style, Is.Null, "no custom item style in high contrast");
                        foreach (MenuItem child in Items(top)) pending.Push(child);
                    }
                    while (pending.Count > 0)
                    {
                        MenuItem item = pending.Pop();
                        nested++;
                        Assert.That(((SolidColorBrush)item.Foreground).Color, Is.EqualTo(item.IsEnabled ? enabled : disabled),
                            "nested " + item.Header + (highContrast ? " (high contrast)" : ""));
                        foreach (MenuItem child in Items(item)) pending.Push(child);
                    }
                    Assert.That(nested, Is.GreaterThanOrEqualTo(7), "the File menu");
                }
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SettingsWindow_Ok_WithEmptyBox_IsRefused()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var win = new SettingsWindow(new SettingsViewModel(new AppSettings()));
            try
            {
                PumpDispatcher();
                var idle = (TextBox)win.FindName("IdleLockBox");
                var error = (TextBlock)win.FindName("ErrorText");
                Assert.That(error.Visibility, Is.EqualTo(Visibility.Collapsed));
                idle.Text = "";

                var ok = (Button)win.FindName("OkButton");
                Assert.DoesNotThrow(() => ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

                Assert.That(error.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(error.Text, Is.Not.Empty);
            }
            finally
            {
                win.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SettingsWindow_HasLabelsFieldsAndButtons()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var vm = new SettingsViewModel(new AppSettings { IdleLockMinutes = 7, ClipboardClearSeconds = 33, Theme = "Dark" });
            var win = new SettingsWindow(vm);
            try
            {
                PumpDispatcher();
                string dash = ((char)0x2013).ToString();
                var idleLabel = (TextBlock)win.FindName("IdleLockLabel");
                var clipLabel = (TextBlock)win.FindName("ClipboardLabel");
                Assert.That(idleLabel.Text, Is.EqualTo("Lock after idle (minutes, 1" + dash + "60)"));
                Assert.That(clipLabel.Text, Is.EqualTo("Clear clipboard after (seconds, 5" + dash + "120)"));
                var idle = (TextBox)win.FindName("IdleLockBox");
                var clip = (TextBox)win.FindName("ClipboardBox");
                Assert.That(idle.Text, Is.EqualTo("7"));
                Assert.That(clip.Text, Is.EqualTo("33"));
                var combo = (ComboBox)win.FindName("ThemeBox");
                Assert.That(combo.Items.Count, Is.EqualTo(3));
                Assert.That(combo.SelectedItem, Is.EqualTo(AppTheme.Dark));
                Assert.That(win.FindName("OkButton"), Is.InstanceOf<Button>());
                Assert.That(((Button)win.FindName("CancelButton")).IsCancel, Is.True);
            }
            finally
            {
                win.Close();
            }
        }
    }
}

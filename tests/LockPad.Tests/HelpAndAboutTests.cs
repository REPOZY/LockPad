using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
    /// 2026-10-21 (user decisions): the menu bar is File | Settings | Help, Help holds Keyboard shortcuts (F1) and About
    /// LockPad; About shows the GitHub address as text with a Copy link button (LockPad never opens a browser); the
    /// Shortcuts window always fits inside the main window, and the main window opens at 1000 x 700 (less on a small
    /// screen).
    /// </summary>
    [TestFixture]
    public class HelpAndAboutTests
    {
        private TempDir _dir;
        private FakeDialogService _dialogs;

        [SetUp]
        public void SetUp()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
        }

        [TearDown]
        public void TearDown()
        {
            _dir.Dispose();
        }

        private ShellViewModel NewShell()
        {
            return new ShellViewModel(new NotebookStore(), new SettingsStore(_dir.File("settings")),
                new AppSettings(), _dialogs, new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
        }

        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
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

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LockPad.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "repository root");
            return dir;
        }

        // ---- the address ----

        [Test]
        public void RepositoryUrl_IsTheProjectsGitHubPage()
        {
            Assert.That(ProjectInfo.RepositoryUrl, Is.EqualTo("https://github.com/REPOZY/LockPad"));
        }

        [Test]
        public void Docs_UseTheSameAddress_CloneAndReleases()
        {
            string contributing = File.ReadAllText(Path.Combine(RepoRoot(), "CONTRIBUTING.md"));
            Assert.That(contributing, Does.Contain("git clone " + ProjectInfo.RepositoryUrl + ".git"));
            string readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
            Assert.That(readme, Does.Contain("(" + ProjectInfo.RepositoryUrl + "/releases)"));
            foreach (string text in new[] { contributing, readme })
                Assert.That(text, Does.Not.Contain("<this repository's URL>"));
        }

        // ---- the Help menu ----

        [Test]
        public void OpenAboutCommand_ShowsTheAboutWindow()
        {
            ShellViewModel shell = NewShell();
            shell.OpenAboutCommand.Execute(null);
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowAbout" }));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void HelpMenu_HoldsKeyboardShortcutsAndAbout_BothWorkWhileLocked()
        {
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                Pump();
                var menu = (Menu)window.FindName("MainMenu");
                MenuItem help = menu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "_Help");
                MenuItem[] items = help.Items.OfType<MenuItem>().ToArray();
                Assert.That(items.Select(m => (string)m.Header),
                    Is.EqualTo(new[] { "_Keyboard shortcuts", "_About LockPad" + (char)0x2026 }));
                Assert.That(help.Items.Count, Is.EqualTo(2), "no separators or other items");

                Assert.That(items[0].Command, Is.SameAs(shell.OpenShortcutsCommand));
                Assert.That(items[0].InputGestureText, Is.EqualTo("F1"));
                Assert.That(items[1].Command, Is.SameAs(shell.OpenAboutCommand));
                Assert.That(items[1].InputGestureText, Is.Null.Or.Empty, "About has no shortcut");

                foreach (MenuItem item in items)
                {
                    Assert.That(item.Command.CanExecute(null), Is.True, item.Header + " on the unlock page");
                    item.Command.Execute(null);
                }
                Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowShortcuts", "ShowAbout" }));
            }
            finally
            {
                window.Close();
            }
        }

        // ---- the About window ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void AboutWindow_ShowsTheAddressAsText_CloseIsDefaultAndCancel()
        {
            var win = new AboutWindow(t => { });
            try
            {
                Pump();
                Assert.That(win.Title, Is.EqualTo("About LockPad"));
                var texts = new List<string>();
                CollectTexts(win, texts);
                Assert.That(texts, Does.Contain(ProjectInfo.RepositoryUrl), "the address, as plain text");
                Assert.That(texts.Any(t => t.Contains("never connects to the internet")), Is.True);
                Assert.That(texts.Any(t => t.Contains("GNU General Public License")), Is.True);
                Assert.That(texts.Any(t => t.Contains("KeePassLib 2.61.1")), Is.True);

                var close = (Button)win.FindName("CloseButton");
                Assert.That(close.IsCancel, Is.True, "Esc closes");
                Assert.That(close.IsDefault, Is.True, "Enter closes");
                var copy = (Button)win.FindName("CopyLinkButton");
                Assert.That(copy.IsDefault, Is.False);
                Assert.That(copy.Content, Is.EqualTo("_Copy link"));

                // No hyperlink, no browser launch: nothing in the window can navigate.
                Assert.That(FindAll<System.Windows.Documents.Hyperlink>(win), Is.Empty);
            }
            finally
            {
                win.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void AboutWindow_CopyLink_CopiesTheAddressAndSaysSo()
        {
            var copied = new List<string>();
            var win = new AboutWindow(copied.Add);
            try
            {
                var status = (TextBlock)win.FindName("CopyStatusText");
                Assert.That(status.Text, Is.Empty);
                ((Button)win.FindName("CopyLinkButton")).RaiseEvent(new RoutedEventArgs(ButtonBase_Click));
                Assert.That(copied, Is.EqualTo(new[] { ProjectInfo.RepositoryUrl }));
                Assert.That(status.Text, Is.EqualTo(AboutWindow.CopiedText));
            }
            finally
            {
                win.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void AboutWindow_CopyLink_ClipboardBusy_SaysSoAndDoesNotThrow()
        {
            var win = new AboutWindow(t => { throw new ExternalException("clipboard busy"); });
            try
            {
                var status = (TextBlock)win.FindName("CopyStatusText");
                Assert.DoesNotThrow(() =>
                    ((Button)win.FindName("CopyLinkButton")).RaiseEvent(new RoutedEventArgs(ButtonBase_Click)));
                Assert.That(status.Text, Is.EqualTo(AboutWindow.CopyFailedText));
            }
            finally
            {
                win.Close();
            }
        }

        private static readonly RoutedEvent ButtonBase_Click = System.Windows.Controls.Primitives.ButtonBase.ClickEvent;

        private static List<T> FindAll<T>(DependencyObject root) where T : class
        {
            var found = new List<T>();
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var t = child as T;
                if (t != null) found.Add(t);
                var d = child as DependencyObject;
                if (d != null) found.AddRange(FindAll<T>(d));
            }
            return found;
        }

        // ---- the Shortcuts window fits inside the main window ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShortcutsWindow_ListScrolls_CloseStaysOutsideTheScrolledPart()
        {
            var win = new ShortcutsWindow();
            try
            {
                var groups = (StackPanel)win.FindName("GroupsPanel");
                var scroller = groups.Parent as ScrollViewer;
                Assert.That(scroller, Is.Not.Null, "the list is inside a ScrollViewer");
                Assert.That(scroller.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto));
                Assert.That(scroller.HorizontalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Disabled));
                var close = (Button)win.FindName("CloseButton");
                for (DependencyObject p = close; p != null; p = LogicalTreeHelper.GetParent(p))
                    Assert.That(p, Is.Not.SameAs(scroller), "Close is always visible");
            }
            finally
            {
                win.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShortcutsWindow_FitInside_CapsItsHeightAtTheOwnersHeight()
        {
            var win = new ShortcutsWindow();
            try
            {
                Assert.That(double.IsPositiveInfinity(win.MaxHeight), Is.True, "no cap before FitInside");
                win.FitInside(450);
                Assert.That(win.MaxHeight, Is.EqualTo(450));
                win.FitInside(0);
                Assert.That(double.IsPositiveInfinity(win.MaxHeight), Is.True, "no usable owner height: no cap");
                win.FitInside(double.NaN);
                Assert.That(double.IsPositiveInfinity(win.MaxHeight), Is.True);
            }
            finally
            {
                win.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShortcutsWindow_AtNaturalSize_FitsTheDefaultMainWindowHeight()
        {
            var win = new ShortcutsWindow();
            try
            {
                var content = (FrameworkElement)win.Content;
                content.Measure(new Size(win.Width - 2, double.PositiveInfinity));
                double chrome = SystemParameters.WindowCaptionHeight + 2 * SystemParameters.ResizeFrameHorizontalBorderHeight
                    + 2 * SystemParameters.FixedFrameHorizontalBorderHeight + 2;
                double natural = content.DesiredSize.Height + chrome;
                Assert.That(natural, Is.LessThan(MainWindow.DefaultHeight - 40),
                    "the whole list shows without scrolling in a default-size main window");
            }
            finally
            {
                win.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShortcutsWindow_IsWiderThanTheUnlockAndCreateForms_SoItCoversThem()
        {
            // The pages behind it centre a fixed-width form; the window (centred on the main window) must cover it with
            // at least 32 px to spare on each side.
            string views = Path.Combine(RepoRoot(), "src", "LockPad.App", "Views");
            var win = new ShortcutsWindow();
            try
            {
                foreach (string page in new[] { "UnlockView.xaml", "CreateNotebookView.xaml" })
                {
                    System.Xml.Linq.XElement form = System.Xml.Linq.XDocument.Load(Path.Combine(views, page)).Descendants()
                        .First(e => e.Name.LocalName == "StackPanel" && e.Attribute("Width") != null);
                    double formWidth = double.Parse((string)form.Attribute("Width"), System.Globalization.CultureInfo.InvariantCulture);
                    Assert.That(win.Width, Is.GreaterThanOrEqualTo(formWidth + 2 * 32), page);
                }
                Assert.That(win.Width, Is.EqualTo(560));
            }
            finally
            {
                win.Close();
            }
        }

        [TestCase(Key.Down, true)]
        [TestCase(Key.Up, true)]
        [TestCase(Key.PageDown, true)]
        [TestCase(Key.PageUp, true)]
        [TestCase(Key.Home, true)]
        [TestCase(Key.End, true)]
        [TestCase(Key.Tab, false)]
        [TestCase(Key.Enter, false)]
        [TestCase(Key.Escape, false)]
        [TestCase(Key.Space, false)]
        [Apartment(ApartmentState.STA)]
        public void ShortcutsWindow_ScrollKeys_ScrollTheListWithoutATabStop(Key key, bool handled)
        {
            var scroller = new ScrollViewer();
            Assert.That(ShortcutsWindow.ScrollWithKey(scroller, key), Is.EqualTo(handled));
        }

        // ---- the main window's starting size ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_StartsAt1000x700_CentredOnScreen()
        {
            Assert.That(MainWindow.DefaultWidth, Is.EqualTo(1000));
            Assert.That(MainWindow.DefaultHeight, Is.EqualTo(700));
            var window = new MainWindow();
            try
            {
                Rect area = SystemParameters.WorkArea;
                Assert.That(window.Width, Is.EqualTo(MainWindow.FitLength(1000, area.Width, window.MinWidth)));
                Assert.That(window.Height, Is.EqualTo(MainWindow.FitLength(700, area.Height, window.MinHeight)));
                Assert.That(window.WindowStartupLocation, Is.EqualTo(WindowStartupLocation.CenterScreen));
                Assert.That(window.MinWidth, Is.EqualTo(640));
                Assert.That(window.MinHeight, Is.EqualTo(400));
            }
            finally
            {
                window.Close();
            }
        }

        [TestCase(700, 1040, 400, 700)]   // room: the default
        [TestCase(700, 690, 400, 690)]    // 1920 x 1080 at 150 %: the work area is about 690 high
        [TestCase(700, 300, 400, 400)]    // never below the minimum
        [TestCase(700, double.NaN, 400, 700)]
        [TestCase(700, 0, 400, 700)]      // no usable work area: the default
        public void FitLength_ClampsToTheWorkArea(double preferred, double available, double min, double expected)
        {
            Assert.That(MainWindow.FitLength(preferred, available, min), Is.EqualTo(expected));
        }
    }
}

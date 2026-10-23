using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// UX round 3, V2: the Shortcuts window opens with F1 from anywhere in the main window, the shortcut table matches
    /// the real bindings, and the note buttons and File > Save As carry their shortcut in a tooltip.
    /// </summary>
    [TestFixture]
    public class ShortcutsTests
    {
        private TempDir _dir;
        private FakeDialogService _dialogs;
        private NotebookStore _store;
        private readonly List<Notebook> _notebooks = new List<Notebook>();

        [SetUp]
        public void SetUp()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
            _store = new NotebookStore();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _notebooks) nb.Dispose();
            _notebooks.Clear();
            _dir.Dispose();
        }

        private ShellViewModel NewShell()
        {
            return new ShellViewModel(_store, new SettingsStore(_dir.File("settings")), new AppSettings(), _dialogs,
                new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
        }

        private Notebook NewNotebook()
        {
            string path = _dir.File("nb.kdbx");
            using (_store.Create(path, new Credentials(Encoding.UTF8.GetBytes("pw"), null), TestKdf.Fast())) { }
            Notebook nb = _store.Open(path, new Credentials(Encoding.UTF8.GetBytes("pw"), null));
            nb.AddNote("A");
            return nb;
        }

        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        private static string Name(Key key, ModifierKeys modifiers)
        {
            return modifiers + "+" + key;
        }

        private static IEnumerable<KeyBinding> KeyBindingsIn(DependencyObject root)
        {
            var element = root as UIElement;
            if (element != null)
                foreach (KeyBinding b in element.InputBindings.OfType<KeyBinding>()) yield return b;
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var d = child as DependencyObject;
                if (d == null) continue;
                foreach (KeyBinding b in KeyBindingsIn(d)) yield return b;
            }
        }

        private static IEnumerable<MenuItem> MenuItemsIn(ItemsControl parent)
        {
            foreach (MenuItem m in parent.Items.OfType<MenuItem>())
            {
                yield return m;
                foreach (MenuItem c in MenuItemsIn(m)) yield return c;
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShortcutTable_MatchesEveryBindingInMainWindowAndEditorView()
        {
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                Notebook nb = NewNotebook();
                shell.ShowEditor(nb);
                var editorView = new EditorView { DataContext = shell.Editor };
                Pump();

                var bound = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyBinding b in KeyBindingsIn(window).Concat(KeyBindingsIn(editorView)))
                    bound.Add(Name(b.Key, b.Modifiers));
                Assert.That(bound.Count, Is.GreaterThan(10), "the bindings were found");

                var tableKeyBindings = new HashSet<string>(StringComparer.Ordinal);
                foreach (ShortcutEntry e in ShortcutTable.Entries.Where(e => e.Kind == ShortcutKind.KeyBinding))
                    foreach (ShortcutGesture g in e.Gestures) tableKeyBindings.Add(Name(g.Key, g.Modifiers));

                Assert.That(bound.Except(tableKeyBindings), Is.Empty, "bound in XAML but missing from the table");
                Assert.That(tableKeyBindings.Except(bound), Is.Empty, "in the table but bound nowhere");

                // Command shortcuts: the routed command is handled and its own gesture is the table's.
                foreach (ShortcutEntry e in ShortcutTable.Entries.Where(e => e.Kind == ShortcutKind.Command))
                {
                    Assert.That(window.CommandBindings.OfType<CommandBinding>().Any(c => c.Command == ApplicationCommands.Find),
                        e.Keys);
                    ShortcutGesture g = e.Gestures.Single();
                    Assert.That(ApplicationCommands.Find.InputGestures.OfType<KeyGesture>()
                        .Any(k => k.Key == g.Key && k.Modifiers == g.Modifiers), Is.True, e.Keys);
                }

                // Code shortcuts with a window-level handler.
                ShortcutEntry lockEntry = ShortcutTable.Entries.Single(e => e.Action == "Lock now");
                ShortcutGesture lockGesture = lockEntry.Gestures.Single();
                Assert.That(MainWindow.HandleLockShortcut(null, lockGesture.Key, lockGesture.Modifiers), Is.True);

                // Gesture texts shown in the menu are table entries too.
                var menu = (Menu)window.FindName("MainMenu");
                foreach (MenuItem m in MenuItemsIn(menu).Where(m => !string.IsNullOrEmpty(m.InputGestureText)))
                    Assert.That(ShortcutTable.Entries.Any(e => e.Keys == m.InputGestureText), Is.True, m.InputGestureText);

                foreach (ShortcutEntry e in ShortcutTable.Entries)
                    if (e.Kind != ShortcutKind.BuiltIn) Assert.That(e.Gestures, Is.Not.Empty, e.Keys + " has a real gesture");
                shell.Lock();
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void F1_OpensTheShortcutsWindow_OnTheUnlockPageAndInTheEditor()
        {
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                Pump();
                KeyBinding f1 = window.InputBindings.OfType<KeyBinding>()
                    .SingleOrDefault(b => b.Key == Key.F1 && b.Modifiers == ModifierKeys.None);
                Assert.That(f1, Is.Not.Null, "F1 on the main window");
                Assert.That(f1.Command, Is.SameAs(shell.OpenShortcutsCommand));
                f1.Command.Execute(null);
                Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowShortcuts" }), "unlock page");

                shell.ShowEditor(NewNotebook());
                Pump();
                f1.Command.Execute(null);
                Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowShortcuts", "ShowShortcuts" }), "editor");
                shell.Lock();
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShortcutsMenuItem_OpensTheWindow()
        {
            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                Pump();
                var menu = (Menu)window.FindName("MainMenu");
                MenuItem help = menu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "_Help");
                MenuItem item = help.Items.OfType<MenuItem>().Single(m => (string)m.Header == "_Keyboard shortcuts");
                item.Command.Execute(item.CommandParameter);
                Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowShortcuts" }));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Tooltips_NameTheShortcuts()
        {
            Notebook nb = NewNotebook();
            _notebooks.Add(nb);
            var vm = new EditorViewModel(nb, _store, _dialogs, new SettingsStore(_dir.File("settings")), new AppSettings());
            var view = new EditorView { DataContext = vm };
            Pump();
            var buttons = new List<Button>();
            foreach (KeyValuePair<string, string> expected in new Dictionary<string, string>
            {
                { "New", "New note (Ctrl+N)" },
                { "Rename", "Rename note (F2)" },
                { "Delete", "Delete note (Del)" },
                { "Undo delete", "Undo delete (Ctrl+Z in the notes list)" },
            })
            {
                Button b = FindButton(view, expected.Key);
                Assert.That(b, Is.Not.Null, expected.Key);
                Assert.That(b.ToolTip, Is.EqualTo(expected.Value), expected.Key);
            }

            ShellViewModel shell = NewShell();
            var window = new MainWindow { DataContext = shell };
            try
            {
                var saveAs = (MenuItem)window.FindName("SaveAsMenuItem");
                Assert.That(saveAs.ToolTip, Is.EqualTo("Save As (Ctrl+Shift+S)"));
                Assert.That(saveAs.InputGestureText, Is.EqualTo("Ctrl+Shift+S"));
            }
            finally
            {
                window.Close();
            }
        }

        private static Button FindButton(DependencyObject root, string content)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var b = child as Button;
                if (b != null && (b.Content as string) == content) return b;
                var d = child as DependencyObject;
                if (d == null) continue;
                Button found = FindButton(d, content);
                if (found != null) return found;
            }
            return null;
        }
    }
}

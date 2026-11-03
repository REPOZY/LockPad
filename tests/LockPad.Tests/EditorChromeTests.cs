using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LockPad.App;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>Task 12 follow-ups: delete notification, window-level Ctrl+F, theme brushes in views.</summary>
    [TestFixture]
    public class EditorChromeTests
    {
        private TempDir _dir;
        private FakeDialogService _dialogs;
        private FakeClipboardService _clipboard;
        private SettingsStore _settingsStore;
        private AppSettings _settings;
        private NotebookStore _store;
        private readonly List<Notebook> _notebooks = new List<Notebook>();

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
            _clipboard = new FakeClipboardService();
            _settingsStore = new SettingsStore(_dir.File("settings"));
            _settings = new AppSettings();
            _store = new NotebookStore();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _notebooks) nb.Dispose();
            _notebooks.Clear();
            _dir.Dispose();
        }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        private Notebook NewNotebook(params string[] titleTextPairs)
        {
            string path = _dir.File("nb.kdbx");
            using (new NotebookStore().Create(path, Pw(), TestKdf.Fast())) { }
            Notebook nb = _store.Open(path, Pw());
            _notebooks.Add(nb);
            for (int i = 0; i + 1 < titleTextPairs.Length; i += 2)
            {
                Note n = nb.AddNote(titleTextPairs[i]);
                nb.UpdateNote(n.Id, titleTextPairs[i], titleTextPairs[i + 1]);
            }
            return nb;
        }

        private EditorViewModel NewVm(Notebook nb)
        {
            return new EditorViewModel(nb, _store, _dialogs, _settingsStore, _settings);
        }

        [Test]
        public void DeleteConfirmed_RaisesNoteDeleted_AfterSelectingNeighbour()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a", "B", "b"));
            NoteItemViewModel selectedAtEvent = null;
            int raised = 0;
            vm.NoteDeleted += (s, e) => { raised++; selectedAtEvent = vm.SelectedNote; };
            _dialogs.Confirms.Enqueue(true);

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(raised, Is.EqualTo(1));
            Assert.That(selectedAtEvent, Is.Not.Null);
            Assert.That(selectedAtEvent, Is.SameAs(vm.SelectedNote));
        }

        [Test]
        public void DeleteDeclined_DoesNotRaiseNoteDeleted()
        {
            EditorViewModel vm = NewVm(NewNotebook("A", "a"));
            int raised = 0;
            vm.NoteDeleted += (s, e) => raised++;
            _dialogs.Confirms.Enqueue(false);

            vm.DeleteNoteCommand.Execute(null);

            Assert.That(raised, Is.EqualTo(0));
        }

        private static void PumpDispatcher()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        private static void Layout(FrameworkElement e)
        {
            // A window that was never shown does not lay out its own subtree; drive the chrome root directly.
            if (e is Window)
            {
                e.ApplyTemplate();
                e = (FrameworkElement)VisualTreeHelper.GetChild(e, 0);
            }
            e.Measure(new Size(900, 600));
            e.Arrange(new Rect(0, 0, 900, 600));
            e.UpdateLayout();
            PumpDispatcher();
        }

        private static FrameworkElement FindByName(DependencyObject root, string name)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                var fe = child as FrameworkElement;
                if (fe != null && fe.Name == name) return fe;
                FrameworkElement found = FindByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EditorView_ShowFind_OpensFindBar()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var view = new EditorView { DataContext = NewVm(NewNotebook("A", "a")) };
            Layout(view);
            var bar = (FrameworkElement)view.FindName("FindBar");
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Collapsed));

            view.ShowFind();

            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EditorView_Obscured_HidesTextListAndFindBar_ShowsLockedPanel()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            EditorViewModel vm = NewVm(NewNotebook("A", "secret text"));
            var view = new EditorView { DataContext = vm };
            Layout(view);
            view.ShowFind();
            var content = (FrameworkElement)view.FindName("EditorContent");
            var panel = (FrameworkElement)view.FindName("ObscuredPanel");
            Assert.That(content.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(panel.Visibility, Is.EqualTo(Visibility.Collapsed));

            vm.Obscure();
            Layout(view);

            Assert.That(content.Visibility, Is.EqualTo(Visibility.Collapsed), "text box, notes list and find bar");
            // (IsVisible is always false in a window that was never shown, so check what the collapsed panel holds.)
            foreach (string name in new[] { "Editor", "NotesList", "FindBar", "FindBox" })
                Assert.That(content.IsAncestorOf((DependencyObject)view.FindName(name)), Is.True, name + " is hidden with it");
            Assert.That(panel.Visibility, Is.EqualTo(Visibility.Visible));
            var text = (System.Windows.Controls.TextBlock)view.FindName("ObscuredText");
            Assert.That(text.Text, Is.EqualTo("Locking. If a LockPad dialog is open, close it to finish."));
            Assert.That(vm.EditorText, Is.EqualTo("secret text"), "the view model keeps its content");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_FindCommand_OpensEditorFindBar_AndIsInertWithoutEditor()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var shell = new ShellViewModel(_store, _settingsStore, _settings, _dialogs, _clipboard, new FakeClock(),
                TestKdf.Fast);
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                Layout(window);
                Assert.That(ApplicationCommands.Find.CanExecute(null, window), Is.False, "no editor shown");

                shell.ShowEditor(NewNotebook("A", "a"));
                Layout(window);
                FrameworkElement bar = FindByName(window, "FindBar");
                Assert.That(bar, Is.Not.Null, "EditorView is in the window's visual tree");
                Assert.That(bar.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(ApplicationCommands.Find.CanExecute(null, window), Is.True);

                ApplicationCommands.Find.Execute(null, window);

                Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>RT-M3: Save As is reachable from a visible File menu, bound to the editor's existing command.</summary>
        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_FileMenu_SaveAs_BoundToEditorSaveAsCommand()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var shell = new ShellViewModel(_store, _settingsStore, _settings, _dialogs, _clipboard, new FakeClock(),
                TestKdf.Fast);
            var window = new MainWindow { DataContext = shell };
            try
            {
                shell.ShowUnlock();
                var item = window.FindName("SaveAsMenuItem") as System.Windows.Controls.MenuItem;
                Assert.That(item, Is.Not.Null);
                Assert.That(item.Header as string, Does.StartWith("Save _As"));
                Assert.That(item.InputGestureText, Is.EqualTo("Ctrl+Shift+S"));
                var file = item.Parent as System.Windows.Controls.MenuItem;
                Assert.That(file, Is.Not.Null);
                Assert.That(file.Header, Is.EqualTo("_File"));
                Assert.That(item.Command, Is.Null, "inert without an editor");
                Assert.That(item.IsEnabled, Is.False);

                Notebook nb = NewNotebook("A", "a");
                shell.ShowEditor(nb);
                PumpDispatcher(); // bindings update at DataBind priority
                Assert.That(item.Command, Is.SameAs(shell.Editor.SaveAsCommand));
                Assert.That(item.IsEnabled, Is.True);
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>Fix round 2, C: the discard confirmations default to No, and Esc (or closing the window) is No.
        /// A Win32 Yes/No MessageBox has no Esc, so DialogService.ConfirmDefaultNo uses this window.</summary>
        [Test]
        [Apartment(ApartmentState.STA)]
        public void ConfirmDefaultNoWindow_NoIsDefaultAndCancel()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var win = new ConfirmDefaultNoWindow("Close without saving", "Discard all unsaved changes? This cannot be undone.");
            try
            {
                Assert.That(win.Title, Is.EqualTo("Close without saving"));
                Assert.That(((System.Windows.Controls.TextBlock)win.FindName("MessageText")).Text,
                    Is.EqualTo("Discard all unsaved changes? This cannot be undone."));
                var yes = (System.Windows.Controls.Button)win.FindName("YesButton");
                var no = (System.Windows.Controls.Button)win.FindName("NoButton");
                Assert.That(yes.Content, Is.EqualTo("_Yes"));
                Assert.That(no.Content, Is.EqualTo("_No"));
                Assert.That(no.IsDefault, Is.True, "Enter = No");
                Assert.That(no.IsCancel, Is.True, "Esc = No");
                Assert.That(yes.IsDefault || yes.IsCancel, Is.False);
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
        [Apartment(ApartmentState.STA)]
        public void CloseChoiceWindow_ButtonsAndDefaults()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var win = new CloseChoiceWindow("Could not save", "File not found.");
            try
            {
                Assert.That(win.Title, Is.EqualTo("Could not save"));
                Assert.That(((System.Windows.Controls.TextBlock)win.FindName("MessageText")).Text, Is.EqualTo("File not found."));
                var saveAs = (System.Windows.Controls.Button)win.FindName("SaveAsButton");
                var discard = (System.Windows.Controls.Button)win.FindName("CloseWithoutSavingButton");
                var cancel = (System.Windows.Controls.Button)win.FindName("CancelButton");
                Assert.That(saveAs.Content, Is.EqualTo("Save As..."));
                Assert.That(discard.Content, Is.EqualTo("Close without saving"));
                Assert.That(cancel.Content, Is.EqualTo("Cancel"));
                Assert.That(cancel.IsDefault, Is.True, "Enter = Cancel");
                Assert.That(cancel.IsCancel, Is.True, "Esc = Cancel");
                Assert.That(saveAs.IsDefault || saveAs.IsCancel || discard.IsDefault || discard.IsCancel, Is.False);
                Assert.That(win.Choice, Is.EqualTo(CloseSaveFailureChoice.Cancel), "closing the window = Cancel");

                saveAs.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.That(win.Choice, Is.EqualTo(CloseSaveFailureChoice.SaveAs));
            }
            finally
            {
                win.Close();
            }
        }

        private static string ViewsDir()
        {
            DirectoryInfo d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "LockPad.sln"))) d = d.Parent;
            Assert.That(d, Is.Not.Null);
            return Path.Combine(d.FullName, "src", "LockPad.App");
        }

        [Test]
        public void ViewXaml_HasNoHardCodedColours()
        {
            var hits = new List<string>();
            var files = new List<string>(Directory.GetFiles(Path.Combine(ViewsDir(), "Views"), "*.xaml"));
            files.Add(Path.Combine(ViewsDir(), "MainWindow.xaml"));
            // The control templates use only theme brushes too (2026-10-21).
            files.Add(Path.Combine(ViewsDir(), "Themes", "Controls.xaml"));
            Assert.That(files.Count, Is.GreaterThanOrEqualTo(6));
            var colour = new Regex("\"#[0-9A-Fa-f]{3,8}\"");
            // Brush/colour attributes and Setters must be {...} markup extensions (theme brushes), never a literal
            // such as Red or White. The colour dictionaries (Light.xaml, Dark.xaml) are not scanned.
            const string props = "(Foreground|Background|BorderBrush|Fill|Stroke|Color)";
            var literalAttr = new Regex("\\b" + props + "=\"(?!\\{)[^\"]*\"");
            var literalSetter = new Regex("Property=\"" + props + "\"\\s+Value=\"(?!\\{)[^\"]*\"");
            var codeRefs = new Regex("\\b(Brushes|Colors)\\.");
            foreach (string f in files)
            {
                string text = File.ReadAllText(f);
                foreach (Regex r in new[] { colour, literalAttr, literalSetter, codeRefs })
                    foreach (Match m in r.Matches(text))
                        hits.Add(Path.GetFileName(f) + ": " + m.Value);
            }
            Assert.That(hits, Is.Empty);
        }
    }
}

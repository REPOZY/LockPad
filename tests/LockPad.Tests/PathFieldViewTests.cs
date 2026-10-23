using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// UX fix (manual test): on the create and unlock pages every Browse button names what it browses for, a path-box
    /// error outlines that box, and the view moves focus into the box the error belongs to. Views are not shown: layout is
    /// driven directly, and the focus move is checked through the FocusRequest seam (an unshown view cannot take focus).
    /// </summary>
    [TestFixture]
    public class PathFieldViewTests
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

        private static void Layout(FrameworkElement e)
        {
            e.Measure(new Size(900, 700));
            e.Arrange(new Rect(0, 0, 900, 700));
            e.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        private CreateNotebookViewModel NewCreateVm()
        {
            return new CreateNotebookViewModel(new NotebookStore(), _dialogs, TestKdf.Fast, nb => nb.Dispose(), () => { });
        }

        private UnlockViewModel NewUnlockVm()
        {
            return new UnlockViewModel(new NotebookStore(), new SettingsStore(_dir.File("settings")), new AppSettings(),
                _dialogs, nb => nb.Dispose(), () => { });
        }

        private static void Create(CreateNotebookViewModel vm, string password)
        {
            using (SecureString a = UnlockViewModelTests.Secure(password))
            using (SecureString b = UnlockViewModelTests.Secure(password))
                vm.CreateAsync(a, b).GetAwaiter().GetResult(); // validation errors complete synchronously
        }

        private static void Unlock(UnlockViewModel vm, string password)
        {
            using (SecureString a = UnlockViewModelTests.Secure(password))
                vm.UnlockAsync(a).GetAwaiter().GetResult();
        }

        private static T Named<T>(FrameworkElement view, string name) where T : class
        {
            var found = view.FindName(name) as T;
            Assert.That(found, Is.Not.Null, name);
            return found;
        }

        private static void AssertBoxesAndButtons(FrameworkElement view)
        {
            string ellipsis = ((char)0x2026).ToString();
            Assert.That(Named<Button>(view, "BrowseNotebookButton").Content, Is.EqualTo("Browse notebook" + ellipsis));
            Assert.That(Named<Button>(view, "BrowseKeyFileButton").Content, Is.EqualTo("Browse key file" + ellipsis));
            Assert.That(Named<TextBlock>(view, "NotebookPathLabel").Text, Is.EqualTo("Notebook file"));
            Assert.That(Named<TextBlock>(view, "KeyFilePathLabel").Text, Is.EqualTo("Key file (optional)"));
            // The messages quote these exact labels and button texts.
            Assert.That(UnlockViewModelTests.NotebookNotFull,
                Does.Contain("\"Notebook file\"").And.EndsWith("Browse notebook" + ellipsis));
            Assert.That(UnlockViewModelTests.KeyFileNotFull,
                Does.Contain("\"Key file (optional)\"").And.EndsWith("Browse key file" + ellipsis));

            Layout(view);
            foreach (string name in new[] { "BrowseNotebookButton", "BrowseKeyFileButton" })
            {
                var button = Named<Button>(view, name);
                var text = (string)button.Content;
                var probe = new TextBlock { Text = text, FontFamily = button.FontFamily, FontSize = button.FontSize };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.That(button.ActualWidth, Is.GreaterThanOrEqualTo(probe.DesiredSize.Width + 8), name + " is not clipped");
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void CreateView_BrowseButtonsNameWhatTheyBrowse()
        {
            AssertBoxesAndButtons(new CreateNotebookView { DataContext = NewCreateVm() });
        }

        [Test, Apartment(ApartmentState.STA)]
        public void UnlockView_BrowseButtonsNameWhatTheyBrowse()
        {
            AssertBoxesAndButtons(new UnlockView { DataContext = NewUnlockVm() });
        }

        [Test, Apartment(ApartmentState.STA)]
        public void CreateView_PathError_OutlinesThatBox_ClearsOnEdit()
        {
            CreateNotebookViewModel vm = NewCreateVm();
            var view = new CreateNotebookView { DataContext = vm };
            var error = new SolidColorBrush(Color.FromRgb(1, 2, 3));
            view.Resources["ErrorForeground"] = error;
            Layout(view);
            var notebook = Named<Border>(view, "NotebookPathBorder");
            var keyFile = Named<Border>(view, "KeyFilePathBorder");
            Assert.That(notebook.BorderBrush, Is.Null, "no outline without an error");
            Assert.That(keyFile.BorderBrush, Is.Null);
            Assert.That(notebook.BorderThickness, Is.EqualTo(new Thickness(2)), "space is reserved: no layout jump");

            Create(vm, "q7#Vt9!mZ2@xLp4$Rw8&"); // empty notebook path
            Layout(view);
            Assert.That(notebook.BorderBrush, Is.SameAs(error));
            Assert.That(keyFile.BorderBrush, Is.Null);

            vm.NotebookPath = "C";
            Layout(view);
            Assert.That(notebook.BorderBrush, Is.Null, "cleared once the user edits the box");

            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = "k.keyx";
            Create(vm, "q7#Vt9!mZ2@xLp4$Rw8&");
            Layout(view);
            Assert.That(keyFile.BorderBrush, Is.SameAs(error));
            Assert.That(notebook.BorderBrush, Is.Null);

            _dialogs.KeyFiles.Enqueue(_dir.File("k.keyx"));
            vm.BrowseKeyFileCommand.Execute(null);
            Layout(view);
            Assert.That(keyFile.BorderBrush, Is.Null, "cleared with the error");
        }

        [Test, Apartment(ApartmentState.STA)]
        public void UnlockView_PathError_OutlinesThatBox_ClearsOnEdit()
        {
            UnlockViewModel vm = NewUnlockVm();
            var view = new UnlockView { DataContext = vm };
            var error = new SolidColorBrush(Color.FromRgb(1, 2, 3));
            view.Resources["ErrorForeground"] = error;
            Layout(view);
            var notebook = Named<Border>(view, "NotebookPathBorder");
            var keyFile = Named<Border>(view, "KeyFilePathBorder");
            Assert.That(notebook.BorderBrush, Is.Null);

            vm.NotebookPath = "a.kdbx";
            Unlock(vm, "pw");
            Layout(view);
            Assert.That(notebook.BorderBrush, Is.SameAs(error));
            Assert.That(keyFile.BorderBrush, Is.Null);

            vm.NotebookPath = "a.kdbx2";
            Layout(view);
            Assert.That(notebook.BorderBrush, Is.Null);

            Unlock(vm, ""); // password error: no path box outlined
            Layout(view);
            Assert.That(notebook.BorderBrush, Is.Null);
            Assert.That(keyFile.BorderBrush, Is.Null);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void CreateView_FocusFollowsTheError()
        {
            CreateNotebookViewModel vm = NewCreateVm();
            var view = new CreateNotebookView { DataContext = vm };
            var focused = new List<Control>();
            view.FocusRequest = c => focused.Add(c);

            view.FocusErrorField();
            Assert.That(focused, Is.Empty, "no error: focus stays where it is");

            _dialogs.KeyFiles.Enqueue(_dir.File("k.keyx"));
            vm.BrowseKeyFileCommand.Execute(null);
            Create(vm, "q7#Vt9!mZ2@xLp4$Rw8&"); // only the key-file box filled
            view.FocusErrorField();
            Assert.That(focused, Is.EqualTo(new Control[] { Named<TextBox>(view, "NotebookPathBox") }));

            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = "k.keyx";
            Create(vm, "q7#Vt9!mZ2@xLp4$Rw8&");
            view.FocusErrorField();
            Assert.That(focused[1], Is.SameAs(Named<TextBox>(view, "KeyFilePathBox")));

            Create(vm, "");
            view.FocusErrorField();
            Assert.That(focused[2], Is.SameAs(Named<PasswordBox>(view, "PasswordInput")));
        }

        /// <summary>Bug fix 2026-10-21: when the command line pre-filled the notebook, the cursor starts in "Master
        /// password" (as it always did on load); a rejected argument keeps that existing behaviour.</summary>
        [Test, Apartment(ApartmentState.STA)]
        public void UnlockView_OnLoad_FocusesThePasswordBox_WithAStartupPath()
        {
            string arg = _dir.File("arg.kdbx");
            System.IO.File.WriteAllBytes(arg, new byte[] { 1 }); // the pre-fill only checks that the file exists
            foreach (string startup in new[] { arg, _dir.File("missing.kdbx") })
            {
                var vm = new UnlockViewModel(new NotebookStore(), new SettingsStore(_dir.File("settings")), new AppSettings(),
                    _dialogs, nb => nb.Dispose(), () => { }, null, null, startup);
                Assert.That(vm.NotebookPath, Is.EqualTo(startup == arg ? arg : ""));
                var view = new UnlockView { DataContext = vm };
                var focused = new List<Control>();
                view.FocusRequest = c => focused.Add(c);

                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                Assert.That(focused, Is.EqualTo(new Control[] { Named<PasswordBox>(view, "PasswordInput") }), startup);
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void UnlockView_FocusFollowsTheError_OtherwiseThePasswordBox()
        {
            UnlockViewModel vm = NewUnlockVm();
            var view = new UnlockView { DataContext = vm };
            var focused = new List<Control>();
            view.FocusRequest = c => focused.Add(c);

            vm.NotebookPath = "a.kdbx";
            Unlock(vm, "pw");
            view.FocusErrorField();
            Assert.That(focused, Is.EqualTo(new Control[] { Named<TextBox>(view, "NotebookPathBox") }));

            vm.NotebookPath = _dir.File("missing.kdbx");
            vm.KeyFilePath = "k.keyx";
            Unlock(vm, "pw");
            view.FocusErrorField();
            Assert.That(focused[1], Is.SameAs(Named<TextBox>(view, "KeyFilePathBox")));

            Unlock(vm, "");
            view.FocusErrorField();
            Assert.That(focused[2], Is.SameAs(Named<PasswordBox>(view, "PasswordInput")));

            vm.KeyFilePath = null;
            vm.NotebookPath = null;
            vm.BrowseKeyFileCommand.Execute(null); // cancelled picker: nothing changes
            _dialogs.KeyFiles.Enqueue(_dir.File("k.keyx"));
            vm.BrowseKeyFileCommand.Execute(null); // clears the error
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));
            view.FocusErrorField();
            Assert.That(focused[3], Is.SameAs(Named<PasswordBox>(view, "PasswordInput")), "the existing default");
        }
    }
}

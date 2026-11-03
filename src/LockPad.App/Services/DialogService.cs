using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using LockPad.App.Views;

namespace LockPad.App.Services
{
    public sealed class DialogService : IDialogService
    {
        private const string NotebookFilter = "LockPad notebook (*.kdbx)|*.kdbx|All files (*.*)|*.*";
        private const string KeyFilter = "Key files (*.keyx;*.key)|*.keyx;*.key|All files (*.*)|*.*";

        private int _openDialogs;

        /// <inheritdoc />
        public bool IsShowingDialog
        {
            get { return _openDialogs > 0; }
        }

        /// <summary>Runs <paramref name="show"/> with the open-dialog counter raised; always lowered again. UI thread only.</summary>
        internal T Track<T>(Func<T> show)
        {
            _openDialogs++;
            try
            {
                return show();
            }
            finally
            {
                _openDialogs--;
            }
        }

        private void Track(Action show)
        {
            Track(() => { show(); return 0; });
        }

        private static Window Owner
        {
            get { return Application.Current == null ? null : Application.Current.MainWindow; }
        }

        private static void Show(string title, string message, MessageBoxImage icon)
        {
            Window owner = Owner;
            if (owner != null) MessageBox.Show(owner, message, title, MessageBoxButton.OK, icon);
            else MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }

        public void ShowError(string title, string message)
        {
            Track(() => Show(title, message, MessageBoxImage.Error));
        }

        public void ShowWarning(string title, string message)
        {
            Track(() => Show(title, message, MessageBoxImage.Warning));
        }

        public void ShowInfo(string title, string message)
        {
            Track(() => Show(title, message, MessageBoxImage.Information));
        }

        /// <summary>A LockPad window, not a Win32 MessageBox: it is hidden from screen captures (CaptureProtection), and
        /// the Delete confirmation shows the note title.</summary>
        public bool Confirm(string title, string message)
        {
            var win = new ConfirmWindow(title, message);
            Window owner = Owner;
            if (owner != null && owner.IsVisible) win.Owner = owner;
            return Track(() =>
            {
                win.ShowDialog();
                return win.Confirmed;
            });
        }

        /// <summary>An owned WPF window, not a MessageBox: a Win32 Yes/No box has no Esc, and here Esc must mean No.</summary>
        public bool ConfirmDefaultNo(string title, string message)
        {
            var win = new ConfirmDefaultNoWindow(title, message);
            Window owner = Owner;
            if (owner != null && owner.IsVisible) win.Owner = owner;
            return Track(() =>
            {
                win.ShowDialog();
                return win.Confirmed;
            });
        }

        public CloseSaveFailureChoice AskCloseAfterSaveFailure(string title, string message)
        {
            var win = new CloseChoiceWindow(title, message);
            Window owner = Owner;
            if (owner != null && owner.IsVisible) win.Owner = owner;
            return Track(() =>
            {
                win.ShowDialog();
                return win.Choice;
            });
        }

        public ExternalChangeChoice AskExternalChange(string path)
        {
            return Track(() => AskExternalChangeCore());
        }

        private static ExternalChangeChoice AskExternalChangeCore()
        {
            const string text = "The file was changed by another program since it was opened.\n\n"
                + "Yes = overwrite it with your version\n"
                + "No = save your version under a new name\n"
                + "Cancel = do nothing";
            Window owner = Owner;
            MessageBoxResult r = owner != null
                ? MessageBox.Show(owner, text, "File changed", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
                : MessageBox.Show(text, "File changed", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (r == MessageBoxResult.Yes) return ExternalChangeChoice.Overwrite;
            if (r == MessageBoxResult.No) return ExternalChangeChoice.SaveAs;
            return ExternalChangeChoice.Cancel;
        }

        public string PickOpenNotebookPath()
        {
            OpenFileDialog dlg = NewOpenDialog(NotebookFilter, null);
            return Track(() => Run(dlg));
        }

        public string PickNewNotebookPath()
        {
            return Track(() => Run(NewSaveDialog("Create notebook")));
        }

        public string PickSaveAsPath(string currentPath)
        {
            SaveFileDialog dlg = NewSaveDialog("Save notebook as");
            if (!string.IsNullOrEmpty(currentPath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrEmpty(dir)) dlg.InitialDirectory = dir;
                    dlg.FileName = Path.GetFileName(currentPath);
                }
                catch (ArgumentException) { }
            }
            return Track(() => Run(dlg));
        }

        public string PickKeyFile(bool forCreate)
        {
            if (forCreate)
            {
                SaveFileDialog save = NewKeyFileSaveDialog();
                return Track(() => Run(save));
            }
            OpenFileDialog open = NewOpenDialog(KeyFilter, "Select key file");
            return Track(() => Run(open));
        }

        public bool ShowSettings(LockPad.App.ViewModels.SettingsViewModel vm)
        {
            var win = new SettingsWindow(vm);
            Window owner = Owner;
            if (owner != null && owner.IsVisible) win.Owner = owner;
            return Track(() => win.ShowDialog() == true);
        }

        /// <summary>Owned by the main window, so input in it counts as activity (C12).</summary>
        public void ShowShortcuts()
        {
            var win = new ShortcutsWindow();
            Window owner = Owner;
            if (owner != null && owner.IsVisible)
            {
                win.Owner = owner;
                win.FitInside(owner.WindowState == WindowState.Minimized ? 0 : owner.ActualHeight);
            }
            Track(() => win.ShowDialog());
        }

        /// <summary>Owned by the main window, so input in it counts as activity (C12).</summary>
        public void ShowAbout()
        {
            var win = new AboutWindow();
            Window owner = Owner;
            if (owner != null && owner.IsVisible) win.Owner = owner;
            Track(() => win.ShowDialog());
        }

        /// <summary>Owned by the main window, so input in it counts as activity (C12).</summary>
        public bool ShowChangePassword(LockPad.App.ViewModels.ChangePasswordViewModel vm)
        {
            var win = new ChangePasswordWindow(vm);
            Window owner = Owner;
            if (owner != null && owner.IsVisible) win.Owner = owner;
            return Track(() => win.ShowDialog() == true);
        }

        public string Prompt(string title, string label, string initialValue)
        {
            var win = new PromptWindow(title, label, initialValue);
            Window owner = Owner;
            if (owner != null && owner.IsVisible) win.Owner = owner;
            return Track(() => win.ShowDialog() == true ? win.Value : null);
        }

        // Every file dialog sets RestoreDirectory: by default the common dialogs change the process current directory
        // to the chosen file's folder. WPF maps RestoreDirectory to OFN_NOCHANGEDIR, and to FOS_NOCHANGEDIR for the
        // Vista dialog (FileDialog.c_VistaFileDialogMask includes NOCHANGEDIR). As defence in depth, App startup also
        // removes the current directory from the DLL search order (SetDllDirectory("")), so a DLL planted next to a
        // notebook is never loaded by name even if some dialog does change the current directory.

        internal static OpenFileDialog NewOpenDialog(string filter, string title)
        {
            var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true, RestoreDirectory = true };
            if (title != null) dlg.Title = title;
            return dlg;
        }

        internal static SaveFileDialog NewKeyFileSaveDialog()
        {
            return new SaveFileDialog
            {
                Filter = KeyFilter,
                Title = "Create key file",
                OverwritePrompt = false,
                CheckFileExists = false,
                RestoreDirectory = true
            };
        }

        // Core refuses existing targets, so the dialog must not prompt to overwrite.
        internal static SaveFileDialog NewSaveDialog(string title)
        {
            return new SaveFileDialog
            {
                Filter = NotebookFilter,
                Title = title,
                DefaultExt = ".kdbx",
                AddExtension = true,
                OverwritePrompt = false,
                CheckFileExists = false,
                RestoreDirectory = true
            };
        }

        private static string Run(FileDialog dlg)
        {
            Window owner = Owner;
            bool? ok = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            return ok == true ? dlg.FileName : null;
        }
    }
}

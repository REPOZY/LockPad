namespace LockPad.App.Services
{
    public enum ExternalChangeChoice { Overwrite, SaveAs, Cancel }

    /// <summary>The user's answer when saving failed while closing the window.</summary>
    public enum CloseSaveFailureChoice { SaveAs, CloseWithoutSaving, Cancel }

    /// <summary>User-facing dialogs. Pickers and Prompt return null on cancel.</summary>
    public interface IDialogService
    {
        void ShowError(string title, string message);
        void ShowWarning(string title, string message);
        void ShowInfo(string title, string message);
        bool Confirm(string title, string message);
        /// <summary>Yes/No confirmation that answers No unless Yes is clicked: Enter, Esc and closing the dialog all
        /// answer No. For irreversible choices (discarding unsaved changes).</summary>
        bool ConfirmDefaultNo(string title, string message);
        ExternalChangeChoice AskExternalChange(string path);
        /// <summary>
        /// Saving failed while closing: "Save As...", "Close without saving" or "Cancel". The default button and Esc
        /// (and closing the dialog) answer Cancel.
        /// </summary>
        CloseSaveFailureChoice AskCloseAfterSaveFailure(string title, string message);
        string PickOpenNotebookPath();
        string PickNewNotebookPath();
        string PickSaveAsPath(string currentPath);
        string PickKeyFile(bool forCreate);
        /// <summary>Shows the modal settings dialog; true when the user pressed OK.</summary>
        bool ShowSettings(LockPad.App.ViewModels.SettingsViewModel vm);
        /// <summary>Shows the modal, read-only Shortcuts window (owned by the main window).</summary>
        void ShowShortcuts();
        /// <summary>Shows the modal About window (owned by the main window): the GitHub address as text, Copy link.</summary>
        void ShowAbout();
        string Prompt(string title, string label, string initialValue);

        /// <summary>Shows the modal Change master password dialog (owned by the main window). True when the user
        /// confirmed and the view model accepted the input (it then holds the credentials); false for Cancel.</summary>
        bool ShowChangePassword(LockPad.App.ViewModels.ChangePasswordViewModel vm);

        /// <summary>
        /// True while any dialog shown through this service is open. The UI thread keeps processing timers inside a
        /// modal loop, so the shell defers the idle lock's teardown while this is true (never tear down a view a dialog
        /// callback uses); it obscures the editor and clears the clipboard instead, and locks once the dialog closes.
        /// </summary>
        bool IsShowingDialog { get; }
    }
}

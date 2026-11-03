using System;
using System.Collections.Generic;
using LockPad.App.Services;
using LockPad.Core;

namespace LockPad.Tests
{
    internal sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan t)
        {
            UtcNow = UtcNow + t;
        }
    }

    /// <summary>
    /// Scripted dialogs: each method dequeues its next answer, or returns the default
    /// (null / false / Cancel) when none is queued. Every call is recorded by method name.
    /// </summary>
    internal sealed class FakeDialogService : IDialogService
    {
        public readonly List<string> Calls = new List<string>();
        public readonly Queue<bool> Confirms = new Queue<bool>();
        public readonly Queue<ExternalChangeChoice> ExternalChanges = new Queue<ExternalChangeChoice>();
        public readonly Queue<string> OpenPaths = new Queue<string>();
        public readonly Queue<string> NewPaths = new Queue<string>();
        public readonly Queue<string> SaveAsPaths = new Queue<string>();
        public readonly Queue<string> KeyFiles = new Queue<string>();
        public readonly Queue<string> Prompts = new Queue<string>();

        public string LastErrorTitle, LastErrorMessage, LastWarningTitle, LastWarningMessage;
        public string LastConfirmTitle, LastConfirmMessage, LastPromptTitle, LastPromptLabel, LastPromptInitial;

        /// <summary>Scriptable: simulates a modal dialog being open (the shell defers locks meanwhile).</summary>
        public bool IsShowingDialog { get; set; }

        private static T Next<T>(Queue<T> q, T fallback)
        {
            return q.Count > 0 ? q.Dequeue() : fallback;
        }

        public void ShowError(string title, string message)
        {
            Calls.Add("ShowError");
            LastErrorTitle = title;
            LastErrorMessage = message;
            if (OnError != null) OnError();
        }

        /// <summary>Runs "inside" the (modal) error box, e.g. to simulate a session end while it is on screen.</summary>
        public Action OnError;

        /// <summary>Runs "inside" the Rename prompt, before its scripted answer is returned.</summary>
        public Action OnPrompt;

        /// <summary>Runs "inside" the (modal) warning, e.g. to simulate a Windows lock while it is on screen.</summary>
        public Action OnWarning;

        public void ShowWarning(string title, string message)
        {
            Calls.Add("ShowWarning");
            LastWarningTitle = title;
            LastWarningMessage = message;
            if (OnWarning != null) OnWarning();
        }

        /// <summary>Runs "inside" the Confirm dialog, e.g. to simulate messages arriving in its modal loop.</summary>
        public Action OnConfirm;

        public bool Confirm(string title, string message)
        {
            Calls.Add("Confirm");
            LastConfirmTitle = title;
            LastConfirmMessage = message;
            if (OnConfirm != null) OnConfirm();
            return Next(Confirms, false);
        }

        /// <summary>Same Confirms queue and OnConfirm hook as Confirm (default false = No).</summary>
        public bool ConfirmDefaultNo(string title, string message)
        {
            Calls.Add("ConfirmDefaultNo");
            LastConfirmTitle = title;
            LastConfirmMessage = message;
            if (OnConfirm != null) OnConfirm();
            return Next(Confirms, false);
        }

        public readonly Queue<CloseSaveFailureChoice> CloseChoices = new Queue<CloseSaveFailureChoice>();
        public string LastCloseChoiceTitle, LastCloseChoiceMessage;

        /// <summary>Runs "inside" the "Could not save" choice, e.g. an idle expiry or a session end while it is open.</summary>
        public Action OnCloseChoice;

        public CloseSaveFailureChoice AskCloseAfterSaveFailure(string title, string message)
        {
            Calls.Add("AskCloseAfterSaveFailure");
            LastCloseChoiceTitle = title;
            LastCloseChoiceMessage = message;
            if (OnCloseChoice != null) OnCloseChoice();
            return Next(CloseChoices, CloseSaveFailureChoice.Cancel);
        }

        /// <summary>Runs "inside" the external-change question, e.g. to simulate an idle expiry while it is open.</summary>
        public Action OnExternalChange;

        public ExternalChangeChoice AskExternalChange(string path)
        {
            Calls.Add("AskExternalChange");
            if (OnExternalChange != null) OnExternalChange();
            return Next(ExternalChanges, ExternalChangeChoice.Cancel);
        }

        /// <summary>Scripted answer for ShowSettings; OnShowSettings can edit the view model "inside" the dialog.</summary>
        public bool SettingsResult;
        public Action<LockPad.App.ViewModels.SettingsViewModel> OnShowSettings;

        public bool ShowSettings(LockPad.App.ViewModels.SettingsViewModel vm)
        {
            Calls.Add("ShowSettings");
            if (OnShowSettings != null) OnShowSettings(vm);
            return SettingsResult;
        }

        public void ShowShortcuts() { Calls.Add("ShowShortcuts"); }
        public void ShowAbout() { Calls.Add("ShowAbout"); }

        public string LastInfoTitle, LastInfoMessage;

        public void ShowInfo(string title, string message)
        {
            Calls.Add("ShowInfo");
            LastInfoTitle = title;
            LastInfoMessage = message;
        }

        /// <summary>Runs "inside" the Change master password dialog: returns true when the user changed it (the
        /// dialog closes), false for Cancel. Without a script the user cancels.</summary>
        public Func<LockPad.App.ViewModels.ChangePasswordViewModel, bool> OnShowChangePassword;

        public bool ShowChangePassword(LockPad.App.ViewModels.ChangePasswordViewModel vm)
        {
            Calls.Add("ShowChangePassword");
            return OnShowChangePassword != null && OnShowChangePassword(vm);
        }

        /// <summary>Runs "inside" the open-notebook picker, e.g. to simulate an idle expiry while it is open.</summary>
        public Action OnPickOpen;

        public string PickOpenNotebookPath()
        {
            Calls.Add("PickOpenNotebookPath");
            if (OnPickOpen != null) OnPickOpen();
            return Next(OpenPaths, null);
        }
        public string PickNewNotebookPath() { Calls.Add("PickNewNotebookPath"); return Next(NewPaths, null); }
        public string PickSaveAsPath(string currentPath) { Calls.Add("PickSaveAsPath"); return Next(SaveAsPaths, null); }
        public string PickKeyFile(bool forCreate) { Calls.Add("PickKeyFile"); return Next(KeyFiles, null); }
        public string Prompt(string title, string label, string initialValue)
        {
            Calls.Add("Prompt");
            LastPromptTitle = title;
            LastPromptLabel = label;
            LastPromptInitial = initialValue;
            if (OnPrompt != null) OnPrompt();
            return Next(Prompts, null);
        }
    }

    /// <summary>Records protected copies instead of touching the real clipboard.</summary>
    internal sealed class FakeClipboardService : IClipboardService
    {
        public string Copied;
        public int CopyCount;
        public int ClearCount;
        public bool ThrowOnCopy;

        /// <summary>While owned: this many ClearIfOwned calls fail (ownership kept) before one succeeds.</summary>
        public int ClearsBeforeRelease;
        /// <summary>While owned: every ClearIfOwned fails (e.g. another process keeps the clipboard open).</summary>
        public bool NeverRelease;
        private int _failedClears;

        /// <summary>Scriptable; a successful copy sets it, a successful clear resets it.</summary>
        public bool OwnsClipboardContent { get; set; }

        public void CopyProtected(string text)
        {
            if (ThrowOnCopy) throw new InvalidOperationException("The clipboard is in use by another program.");
            if (string.IsNullOrEmpty(text)) return;
            Copied = text;
            CopyCount++;
            OwnsClipboardContent = true;
        }

        public void ClearIfOwned()
        {
            ClearCount++;
            if (!OwnsClipboardContent) return;
            if (NeverRelease || _failedClears < ClearsBeforeRelease)
            {
                _failedClears++;
                return;
            }
            OwnsClipboardContent = false;
        }
    }
}

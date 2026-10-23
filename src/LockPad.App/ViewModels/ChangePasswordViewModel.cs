using System;
using System.Security;
using LockPad.App.Infrastructure;
using LockPad.App.Services;
using LockPad.Core;

namespace LockPad.App.ViewModels
{
    /// <summary>The boxes of the Change master password dialog an error belongs to: the view focuses them.</summary>
    public enum ChangePasswordField
    {
        None,
        /// <summary>The "Current password" box.</summary>
        Current,
        /// <summary>The first of the two new-password boxes.</summary>
        New,
    }

    /// <summary>
    /// The Change master password dialog (user decisions 2026-10-19): the current password is required, only the
    /// password changes (the notebook's key file, if any, is kept). Passwords arrive only as <see cref="SecureString"/>
    /// and go straight to <see cref="Credentials.FromSecureString"/>; no password is ever held in a string. The check
    /// of the current password compares raw keys in memory (no key derivation, no file access to the notebook), so a
    /// wrong current password never reaches the store. Owns the credentials it builds until they are handed over.
    /// </summary>
    public sealed class ChangePasswordViewModel : ObservableObject, IDisposable
    {
        internal const string EnterCurrentMessage = "Enter your current password.";
        internal const string EnterNewMessage = "Enter a new password.";
        internal const string MismatchMessage = "The new passwords do not match.";
        internal const string WrongCurrentMessage = "The current password is wrong.";
        internal const string SameAsCurrentMessage = "The new password is the same as the current one.";

        private readonly Notebook _notebook;
        private readonly IDialogService _dialogs;

        private uint _strengthBits;
        private StrengthRating _strength = StrengthRating.Weak;
        private bool _hasPassword;
        private string _strengthWarning;
        private string _errorMessage;
        private ChangePasswordField _errorField;
        private bool _clearErrorField;
        private readonly PasswordBackOff _backOff;
        private bool _isWaiting;
        private int _waitId;
        private bool _isAbandoned;

        /// <summary>
        /// <paramref name="backOff"/> is the editor's (one per notebook session), so reopening the dialog keeps a wait in
        /// progress: the new dialog then starts disabled, with the "Try again in N s." message. There is deliberately no
        /// constructor without one (review B5): a dialog must never start with a fresh back-off.
        /// </summary>
        internal ChangePasswordViewModel(Notebook notebook, IDialogService dialogs, PasswordBackOff backOff)
        {
            if (notebook == null) throw new ArgumentNullException(nameof(notebook));
            if (dialogs == null) throw new ArgumentNullException(nameof(dialogs));
            if (backOff == null) throw new ArgumentNullException(nameof(backOff));
            _notebook = notebook;
            _dialogs = dialogs;
            _backOff = backOff;
            TimeSpan remaining = backOff.Remaining;
            if (remaining > TimeSpan.Zero) Wait(remaining);
        }

        /// <summary>True once the editor behind the dialog was obscured or left (an idle lock, Windows lock or sleep
        /// while the dialog was open; fix round 2, X1). From then on every attempt is refused without any check,
        /// comparison or back-off count, "Change password" is disabled, and the view empties the boxes.</summary>
        public bool IsAbandoned
        {
            get { return _isAbandoned; }
            private set
            {
                if (SetProperty(ref _isAbandoned, value)) OnPropertyChanged(nameof(CanChange));
            }
        }

        /// <summary>"Change password" is enabled: not during a back-off wait and not once abandoned.</summary>
        public bool CanChange
        {
            get { return !_isWaiting && !_isAbandoned; }
        }

        /// <summary>Raised once by <see cref="Abandon"/>: the view clears its PasswordBoxes.</summary>
        public event EventHandler Abandoned;

        /// <summary>
        /// The editor was obscured or left while the dialog is open: drops any accepted credentials and refuses every
        /// later attempt. Called by the editor; never throws to it (a failing listener is ignored).
        /// </summary>
        internal void Abandon()
        {
            if (_isAbandoned) return;
            DisposeCredentials();
            _waitId++; // a running back-off wait no longer updates the message
            try
            {
                IsAbandoned = true;
                IsWaiting = false;
                SetError(null, ChangePasswordField.None, false);
                EventHandler handler = Abandoned;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // The state above is what refuses the change; a failing view listener must not stop the obscure.
            }
        }

        /// <summary>True while a wrong current password's back-off runs: "Change password" is disabled meanwhile, and
        /// an attempt (Enter) is refused without any comparison.</summary>
        public bool IsWaiting
        {
            get { return _isWaiting; }
            private set
            {
                if (SetProperty(ref _isWaiting, value)) OnPropertyChanged(nameof(CanChange));
            }
        }

        public uint StrengthBits
        {
            get { return _strengthBits; }
            private set { SetProperty(ref _strengthBits, value); }
        }

        public StrengthRating Strength
        {
            get { return _strength; }
            private set { SetProperty(ref _strength, value); }
        }

        public string StrengthWarning
        {
            get { return _strengthWarning; }
            private set { SetProperty(ref _strengthWarning, value); }
        }

        /// <summary>False until a character is typed in the first new-password box (and again once it is emptied): the
        /// strength meter's track is then empty and no rating is shown.</summary>
        public bool HasPassword
        {
            get { return _hasPassword; }
            private set { SetProperty(ref _hasPassword, value); }
        }

        /// <summary>The inline validation message; null when there is none.</summary>
        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set { SetProperty(ref _errorMessage, value); }
        }

        /// <summary>The box the last refusal belongs to; the view focuses it.</summary>
        public ChangePasswordField ErrorField
        {
            get { return _errorField; }
            private set { SetProperty(ref _errorField, value); }
        }

        /// <summary>True when the view should also empty the boxes of <see cref="ErrorField"/> (a wrong or repeated
        /// password, a mismatch, a weak password the user declined); false when a box was only empty.</summary>
        public bool ClearErrorField
        {
            get { return _clearErrorField; }
            private set { SetProperty(ref _clearErrorField, value); }
        }

        /// <summary>After an accepted <see cref="TryAccept"/>: the verified current credentials. Owned by this view
        /// model (disposed with it).</summary>
        internal Credentials Current { get; private set; }

        /// <summary>After an accepted <see cref="TryAccept"/>: the new credentials (new password, the same key file).
        /// Owned by this view model until <see cref="ReleaseReplacement"/>.</summary>
        internal Credentials Replacement { get; private set; }

        /// <summary>Called per keystroke in the first new-password box; the Create page's estimator and wording.</summary>
        public void UpdateStrength(SecureString password)
        {
            if (password == null || password.Length == 0)
            {
                StrengthBits = 0;
                Strength = StrengthRating.Weak;
                StrengthWarning = null; // no warning before typing starts
                HasPassword = false;
                return;
            }
            SetStrength(PasswordStrength.EstimateBits(password));
        }

        /// <summary>
        /// Validates the dialog's input. True when the change may run: <see cref="Current"/> and
        /// <see cref="Replacement"/> are then set. False keeps the dialog open, with <see cref="ErrorMessage"/> (none
        /// after a declined weak password) and <see cref="ErrorField"/> set. Checks, in order: empty boxes, the repeat,
        /// the current password, a new password equal to the current one, then (default No, as on the Create page)
        /// a weak new password. The caller keeps ownership of the <see cref="SecureString"/>s.
        /// <para>While a wrong-password back-off runs, every attempt is refused before any check or comparison. A wrong
        /// current password starts the next, doubled wait (1, 2, 4, then 8 s).</para>
        /// </summary>
        public bool TryAccept(SecureString current, SecureString newPassword, SecureString repeat)
        {
            DisposeCredentials();
            if (_isAbandoned) return false; // the screen locked meanwhile: no check, no comparison, nothing counted
            TimeSpan remaining = _backOff.Remaining;
            if (remaining > TimeSpan.Zero)
                return Refuse(WaitMessage(remaining), ChangePasswordField.Current, false);
            SetError(null, ChangePasswordField.None, false);

            if (current == null || current.Length == 0)
                return Refuse(EnterCurrentMessage, ChangePasswordField.Current, false);
            if (newPassword == null || newPassword.Length == 0)
                return Refuse(EnterNewMessage, ChangePasswordField.New, false);
            if (!CreateNotebookViewModel.SecureStringsEqual(newPassword, repeat))
                return Refuse(MismatchMessage, ChangePasswordField.New, true);

            string keyFile = _notebook.KeyFilePath;
            Credentials currentCredentials = null;
            Credentials replacement = null;
            try
            {
                try
                {
                    currentCredentials = Credentials.FromSecureString(current, keyFile);
                    replacement = Credentials.FromSecureString(newPassword, keyFile);
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    // The kept key file can no longer be read (moved, deleted, locked): nothing can be changed.
                    return Refuse("The key file could not be read: " + ex.Message, ChangePasswordField.None, false);
                }

                if (!_notebook.CredentialsMatch(currentCredentials))
                {
                    SetError(null, ChangePasswordField.Current, true);
                    Wait(_backOff.RecordFailure());
                    return false;
                }
                if (_notebook.CredentialsMatch(replacement))
                    return Refuse(SameAsCurrentMessage, ChangePasswordField.New, true);

                uint bits = PasswordStrength.EstimateBits(newPassword);
                SetStrength(bits);
                // Default No: Enter, Esc and closing the question never accept a password under 60 bits.
                if (Strength == StrengthRating.Weak && !_dialogs.ConfirmDefaultNo(
                    CreateNotebookViewModel.WeakPasswordTitle, CreateNotebookViewModel.WeakPasswordQuestion(bits)))
                    return Refuse(null, ChangePasswordField.New, true);

                Current = currentCredentials;
                Replacement = replacement;
                currentCredentials = null;
                replacement = null;
                return true;
            }
            finally
            {
                if (currentCredentials != null) currentCredentials.Dispose();
                if (replacement != null) replacement.Dispose();
            }
        }

        /// <summary>The notebook took <see cref="Replacement"/> (the change wrote the notebook file): this view model
        /// no longer owns or disposes it.</summary>
        internal void ReleaseReplacement()
        {
            Replacement = null;
        }

        /// <summary>Disposes the credentials this view model still owns.</summary>
        public void Dispose()
        {
            DisposeCredentials();
        }

        private void DisposeCredentials()
        {
            Credentials current = Current, replacement = Replacement;
            Current = null;
            Replacement = null;
            if (current != null) current.Dispose();
            if (replacement != null) replacement.Dispose();
        }

        /// <summary>"The current password is wrong. Try again in N s." (whole seconds, rounded up), as on the
        /// held-notebook unlock.</summary>
        private static string WaitMessage(TimeSpan remaining)
        {
            int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            return WrongCurrentMessage + " Try again in " + seconds + " s.";
        }

        /// <summary>
        /// Shows the wait and disables "Change password" until it is over, then leaves the plain wrong-password message.
        /// Never throws (async void): a failed delay simply ends the wait early in the UI, while the back-off's own
        /// deadline still refuses attempts until it has passed. In the dialog the await resumes on the UI thread.
        /// </summary>
        private async void Wait(TimeSpan wait)
        {
            int id = ++_waitId;
            ErrorMessage = WaitMessage(wait);
            IsWaiting = true;
            try
            {
                await _backOff.DelayAsync(wait);
            }
            catch (Exception)
            {
                // Ends the visible wait only; TryAccept still checks the deadline.
            }
            if (id != _waitId) return; // a later wait took over
            IsWaiting = false;
            string shown = ErrorMessage;
            if (shown != null && shown.StartsWith(WrongCurrentMessage + " Try again in ", StringComparison.Ordinal))
                ErrorMessage = WrongCurrentMessage;
        }

        private bool Refuse(string message, ChangePasswordField field, bool clear)
        {
            SetError(message, field, clear);
            return false;
        }

        private void SetError(string message, ChangePasswordField field, bool clear)
        {
            ErrorMessage = message;
            ClearErrorField = clear;
            ErrorField = field;
        }

        private void SetStrength(uint bits)
        {
            StrengthRating rating = PasswordStrength.Rate(bits);
            StrengthBits = bits;
            Strength = rating;
            StrengthWarning = CreateNotebookViewModel.StrengthWarningFor(rating);
            HasPassword = true; // only ever rated for a non-empty password
        }
    }
}

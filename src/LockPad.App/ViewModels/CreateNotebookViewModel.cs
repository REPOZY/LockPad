using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Input;
using LockPad.App.Infrastructure;
using LockPad.App.Services;
using LockPad.Core;
using KeePassLib.Cryptography.KeyDerivation;

namespace LockPad.App.ViewModels
{
    /// <summary>
    /// Create-notebook page. Passwords arrive only as <see cref="SecureString"/>; every plaintext copy is a
    /// <c>char[]</c> that is zeroed in <c>finally</c>, never a string.
    /// </summary>
    public sealed class CreateNotebookViewModel : ObservableObject
    {
        internal const string FairWarning = "Consider a longer password (aim for 80+ bits).";
        internal const string WeakWarning = "Weak password.";
        internal const string WeakPasswordTitle = "Weak password";
        private const string KeyFileIsNotebookMessage =
            "The key file cannot be the notebook file itself. Choose a different key file.";
        internal const string ChooseNotebookMessage =
            "Choose where to save the notebook in \"" + PathInput.NotebookBoxLabel + "\".";
        internal const string ChooseNewKeyFileMessage =
            "Choose where to save the new key file in \"" + PathInput.KeyFileBoxLabel + "\".";

        private readonly NotebookStore _store;
        private readonly IDialogService _dialogs;
        private readonly Func<KdfParameters> _kdfFactory;
        private readonly Action<Notebook> _onCreated;
        private readonly Action _cancel;
        private readonly RelayCommand _browseNotebook;
        private readonly RelayCommand _browseKeyFile;
        private readonly RelayCommand _cancelCommand;

        private string _notebookPath;
        private string _keyFilePath;
        private bool _createNewKeyFile;
        private uint _strengthBits;
        private StrengthRating _strength = StrengthRating.Weak;
        private bool _hasPassword;
        private string _strengthWarning;
        private string _errorMessage;
        private ErrorField _errorField;
        private bool _isBusy;
        private string _keptNewKeyFile;

        public CreateNotebookViewModel(NotebookStore store, IDialogService dialogs, Func<KdfParameters> kdfFactory,
            Action<Notebook> onCreated, Action cancel)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (dialogs == null) throw new ArgumentNullException(nameof(dialogs));
            if (kdfFactory == null) throw new ArgumentNullException(nameof(kdfFactory));
            if (onCreated == null) throw new ArgumentNullException(nameof(onCreated));
            if (cancel == null) throw new ArgumentNullException(nameof(cancel));

            _store = store;
            _dialogs = dialogs;
            _kdfFactory = kdfFactory;
            _onCreated = onCreated;
            _cancel = cancel;

            _browseNotebook = new RelayCommand(p => BrowseNotebook(), p => !IsBusy);
            _browseKeyFile = new RelayCommand(p => BrowseKeyFile(), p => !IsBusy);
            _cancelCommand = new RelayCommand(p => _cancel(), p => !IsBusy);
        }

        public string NotebookPath
        {
            get { return _notebookPath; }
            set { if (SetProperty(ref _notebookPath, value)) ClearErrorFieldIf(ErrorField.NotebookPath); }
        }

        public string KeyFilePath
        {
            get { return _keyFilePath; }
            set { if (SetProperty(ref _keyFilePath, value)) ClearErrorFieldIf(ErrorField.KeyFilePath); }
        }

        public bool CreateNewKeyFile
        {
            get { return _createNewKeyFile; }
            set { SetProperty(ref _createNewKeyFile, value); }
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

        /// <summary>False until a character is typed (and again once the box is emptied): the strength meter's track is
        /// then empty and no rating is shown.</summary>
        public bool HasPassword
        {
            get { return _hasPassword; }
            private set { SetProperty(ref _hasPassword, value); }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set { SetProperty(ref _errorMessage, value); }
        }

        /// <summary>The box <see cref="ErrorMessage"/> belongs to: the view focuses it and outlines a path box. Editing
        /// that box resets it to None (the message stays until the next attempt).</summary>
        public ErrorField ErrorField
        {
            get { return _errorField; }
            private set { SetProperty(ref _errorField, value); }
        }

        private void SetError(string message, ErrorField field)
        {
            ErrorMessage = message;
            ErrorField = field;
        }

        private void ClearErrorFieldIf(ErrorField field)
        {
            if (_errorField == field) ErrorField = ErrorField.None;
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                _browseNotebook.RaiseCanExecuteChanged();
                _browseKeyFile.RaiseCanExecuteChanged();
                _cancelCommand.RaiseCanExecuteChanged();
            }
        }

        public ICommand BrowseNotebookCommand { get { return _browseNotebook; } }
        public ICommand BrowseKeyFileCommand { get { return _browseKeyFile; } }
        public ICommand CancelCommand { get { return _cancelCommand; } }

        /// <summary>Called per keystroke on the UI thread; the estimator is bounded, so no debounce is needed.</summary>
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
        /// Never throws: every failure is reported through <see cref="ErrorMessage"/>. A call made while another is
        /// running returns immediately. The caller keeps ownership of both <see cref="SecureString"/>s.
        /// </summary>
        public async Task CreateAsync(SecureString password, SecureString confirm)
        {
            if (IsBusy) return;
            IsBusy = true;
            string createdKeyFile = null;
            bool creatingKeyFile = false;
            bool creatingNotebook = false;
            string path = null;
            Notebook created = null;
            bool handedOff = false;
            try
            {
                SetError(null, ErrorField.None);
                if (password == null || password.Length == 0)
                {
                    SetError("Enter a password.", ErrorField.Password);
                    return;
                }
                if (!SecureStringsEqual(password, confirm))
                {
                    SetError("Passwords do not match.", ErrorField.Password);
                    return;
                }
                if (string.IsNullOrWhiteSpace(NotebookPath))
                {
                    SetError(ChooseNotebookMessage, ErrorField.NotebookPath);
                    return;
                }
                string pathError = PathInput.CheckLocalFullPath(NotebookPath, PathInput.NotebookNotFullPathMessage, out path);
                if (pathError != null)
                {
                    SetError(pathError, ErrorField.NotebookPath);
                    return;
                }
                if (!PathInput.FolderExists(path))
                {
                    SetError(PathInput.NotebookFolderMissingMessage, ErrorField.NotebookPath);
                    return;
                }
                if (File.Exists(path) || Directory.Exists(path))
                {
                    SetError("File already exists.", ErrorField.NotebookPath);
                    return;
                }
                if (NotebookStore.IsBackupCopyName(path))
                {
                    // Checked before a new key file is created; NotebookStore.Create refuses it too.
                    SetError(NotebookStore.BackupNameReservedMessage, ErrorField.NotebookPath);
                    return;
                }

                bool newKeyFile = CreateNewKeyFile;
                string keyFile = null;
                if (!string.IsNullOrWhiteSpace(KeyFilePath))
                {
                    string keyError = PathInput.CheckLocalFullPath(KeyFilePath, PathInput.KeyFileNotFullPathMessage, out keyFile);
                    if (keyError != null)
                    {
                        SetError(keyError, ErrorField.KeyFilePath);
                        return;
                    }
                }
                if (newKeyFile && keyFile == null)
                {
                    SetError(ChooseNewKeyFileMessage, ErrorField.KeyFilePath);
                    return;
                }
                if (newKeyFile && !PathInput.FolderExists(keyFile))
                {
                    SetError(PathInput.KeyFileFolderMissingMessage, ErrorField.KeyFilePath);
                    return;
                }
                if (keyFile != null && string.Equals(keyFile, path, StringComparison.OrdinalIgnoreCase))
                {
                    SetError(KeyFileIsNotebookMessage, ErrorField.KeyFilePath);
                    return;
                }
                if (!newKeyFile && keyFile != null && !File.Exists(keyFile))
                {
                    SetError(PathInput.KeyFileNotFoundMessage, ErrorField.KeyFilePath);
                    return;
                }

                uint bits = PasswordStrength.EstimateBits(password);
                SetStrength(bits);
                // Default No: Enter, Esc and closing the dialog never accept a password under 60 bits.
                if (Strength == StrengthRating.Weak && !_dialogs.ConfirmDefaultNo(WeakPasswordTitle, WeakPasswordQuestion(bits)))
                {
                    SetError(null, ErrorField.Password); // back to the (cleared) password box; no message needed
                    return;
                }

                if (newKeyFile)
                {
                    creatingKeyFile = true;
                    Credentials.CreateKeyFile(keyFile); // refuses an existing file ("File already exists.")
                    creatingKeyFile = false;
                    createdKeyFile = keyFile;
                }

                // Argon2 tuning (about 1 s) and the key derivations of create + verify never run on the UI thread.
                NotebookStore store = _store;
                Func<KdfParameters> kdfFactory = _kdfFactory;
                string notebookPath = path;
                creatingNotebook = true;
                created = await Task.Run(() => CreateWithNewCredentials(store, notebookPath, password, keyFile, kdfFactory));
                creatingNotebook = false;

                // Also warn when a retry used the key file kept from a failed attempt: the user has not been
                // warned about that new file yet.
                bool usedKeptKeyFile = keyFile != null && _keptNewKeyFile != null
                    && string.Equals(keyFile, _keptNewKeyFile, StringComparison.OrdinalIgnoreCase);
                _keptNewKeyFile = null;
                if (newKeyFile || usedKeptKeyFile)
                    _dialogs.ShowWarning("Back up your key file", "Keep a copy of the key file (" + keyFile
                        + ") somewhere safe. Without it, this notebook can never be opened again, even with the right password.");

                handedOff = true;
                _onCreated(created);
            }
            catch (Exception ex)
            {
                string message = NotebookErrorText.Describe(ex, "Could not create the notebook: ");
                if (createdKeyFile != null && created == null)
                {
                    message += " The new key file was kept at " + createdKeyFile + ".";
                    // A retry must use the kept file; creating it again would fail with "File already exists.".
                    _keptNewKeyFile = createdKeyFile;
                    CreateNewKeyFile = false;
                }
                // Creating the new key file failed (e.g. it already exists): that is the key-file box's error. The
                // notebook's folder disappeared during the KDF (the store's IOException message is kept): the notebook
                // box's error.
                ErrorField field = creatingKeyFile ? ErrorField.KeyFilePath
                    : creatingNotebook && ex is IOException && !PathInput.FolderExists(path) ? ErrorField.NotebookPath
                    : ErrorField.None;
                SetError(message, field);
                if (!handedOff && created != null) created.Dispose();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static Notebook CreateWithNewCredentials(NotebookStore store, string path, SecureString password,
            string keyFile, Func<KdfParameters> kdfFactory)
        {
            KdfParameters kdf = kdfFactory();
            Credentials credentials = Credentials.FromSecureString(password, keyFile);
            try
            {
                return store.Create(path, credentials, kdf); // the notebook owns the credentials on success
            }
            catch
            {
                credentials.Dispose();
                throw;
            }
        }

        private void SetStrength(uint bits)
        {
            StrengthRating rating = PasswordStrength.Rate(bits);
            StrengthBits = bits;
            Strength = rating;
            StrengthWarning = StrengthWarningFor(rating);
            HasPassword = true; // only ever rated for a non-empty password
        }

        /// <summary>The hint under the strength meter; also used by the Change master password dialog.</summary>
        internal static string StrengthWarningFor(StrengthRating rating)
        {
            return rating == StrengthRating.Fair ? FairWarning
                : rating == StrengthRating.Weak ? WeakWarning : null;
        }

        /// <summary>The default-No question for a password under 60 bits (Weak); also asked by Change master password.</summary>
        internal static string WeakPasswordQuestion(uint bits)
        {
            return "This password is weak (" + bits + " bits). Anyone who gets this file could guess it. Use it anyway?";
        }

        private void BrowseNotebook()
        {
            string path = _dialogs.PickNewNotebookPath();
            if (path == null) return;
            NotebookPath = path;
            SetError(null, ErrorField.None);
        }

        private void BrowseKeyFile()
        {
            string path = _dialogs.PickKeyFile(CreateNewKeyFile);
            if (path == null) return;
            KeyFilePath = path;
            SetError(null, ErrorField.None);
        }

        /// <summary>Length check plus a constant-time loop over zeroed char[] copies; no string is ever created.</summary>
        internal static bool SecureStringsEqual(SecureString a, SecureString b)
        {
            if (a == null || b == null) return false;
            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            char[] ca = null, cb = null;
            try
            {
                pa = Marshal.SecureStringToGlobalAllocUnicode(a);
                ca = new char[a.Length];
                Marshal.Copy(pa, ca, 0, ca.Length);
                pb = Marshal.SecureStringToGlobalAllocUnicode(b);
                cb = new char[b.Length];
                Marshal.Copy(pb, cb, 0, cb.Length);

                if (ca.Length != cb.Length) return false;
                int diff = 0;
                for (int i = 0; i < ca.Length; i++) diff |= ca[i] ^ cb[i];
                return diff == 0;
            }
            finally
            {
                if (ca != null) Array.Clear(ca, 0, ca.Length);
                if (cb != null) Array.Clear(cb, 0, cb.Length);
                if (pa != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pa);
                if (pb != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pb);
            }
        }
    }
}

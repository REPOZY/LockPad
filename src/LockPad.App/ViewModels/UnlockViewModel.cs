using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Input;
using LockPad.App.Infrastructure;
using LockPad.App.Services;
using LockPad.Core;

namespace LockPad.App.ViewModels
{
    /// <summary>
    /// Unlock page: opens a notebook file, or re-unlocks a notebook held in memory after an auto-lock whose save
    /// failed. Passwords arrive only as <see cref="SecureString"/> and are never copied into a string.
    /// </summary>
    public sealed class UnlockViewModel : ObservableObject
    {
        internal const string WrongCredentialsMessage = "Wrong password or key file.";
        internal static readonly string ReadOnlyWarning = "This file was not created by LockPad " + (char)0x2014
            + " it may be your password database. It is opened read-only.";
        internal const string BackupCopyWarning =
            "This is a backup copy. It is opened read-only. To restore it, close LockPad and rename the file.";
        internal const string SymbolicLinkWarning =
            "This notebook is a link to another file. It is opened read-only. Open the real file instead.";
        internal const string ChooseNotebookMessage = "Choose the notebook to open in \"" + PathInput.NotebookBoxLabel + "\".";

        /// <summary>The "Read-only" warning shown when a notebook opens read-only.</summary>
        internal static string ReadOnlyWarningFor(ReadOnlyReason reason)
        {
            switch (reason)
            {
                case ReadOnlyReason.BackupCopy: return BackupCopyWarning;
                case ReadOnlyReason.SymbolicLink: return SymbolicLinkWarning;
                default: return ReadOnlyWarning;
            }
        }

        private readonly NotebookStore _store;
        private readonly SettingsStore _settingsStore;
        private readonly AppSettings _settings;
        private readonly IDialogService _dialogs;
        private readonly Action<Notebook> _onOpened;
        private readonly Action _showCreate;
        private readonly Notebook _held;
        private readonly RelayCommand _browseNotebook;
        private readonly RelayCommand _browseKeyFile;
        private readonly RelayCommand _newNotebook;

        private string _notebookPath;
        private string _keyFilePath;
        private string _errorMessage;
        private ErrorField _errorField;
        private bool _isBusy;
        private bool _isWaiting;
        private int _heldFailures;

        /// <param name="startupNotebookPath">The notebook path LockPad was started with (see StartupArguments), or the file
        /// chosen with File > Open notebook in the editor, or null.
        /// Untrusted: pre-filled only when it is an existing file on a local drive, instead of the remembered path.
        /// Otherwise the box stays empty (no fallback to the remembered path) and the error text names it. It never
        /// opens anything and is never remembered by itself. Ignored for a held notebook.</param>
        public UnlockViewModel(NotebookStore store, SettingsStore settingsStore, AppSettings settings,
            IDialogService dialogs, Action<Notebook> onOpened, Action showCreate,
            Notebook heldNotebook = null, string heldWarning = null, string startupNotebookPath = null)
            : this(store, settingsStore, settings, dialogs, onOpened, showCreate, heldNotebook, heldWarning,
                startupNotebookPath, File.Exists, LocalPath.RequireLocalFile)
        {
        }

        /// <summary>As above, with the existence probe and the local-path check of the pre-fill injected (test seams).
        /// </summary>
        internal UnlockViewModel(NotebookStore store, SettingsStore settingsStore, AppSettings settings,
            IDialogService dialogs, Action<Notebook> onOpened, Action showCreate,
            Notebook heldNotebook, string heldWarning, string startupNotebookPath,
            Func<string, bool> fileExists, Func<string, string> requireLocalFile)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (settingsStore == null) throw new ArgumentNullException(nameof(settingsStore));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (dialogs == null) throw new ArgumentNullException(nameof(dialogs));
            if (onOpened == null) throw new ArgumentNullException(nameof(onOpened));
            if (showCreate == null) throw new ArgumentNullException(nameof(showCreate));
            if (fileExists == null) throw new ArgumentNullException(nameof(fileExists));
            if (requireLocalFile == null) throw new ArgumentNullException(nameof(requireLocalFile));

            _store = store;
            _settingsStore = settingsStore;
            _settings = settings;
            _dialogs = dialogs;
            _onOpened = onOpened;
            _showCreate = showCreate;
            _held = heldNotebook;
            HeldWarning = heldWarning;

            if (heldNotebook != null)
            {
                _notebookPath = heldNotebook.FilePath;
            }
            else if (!string.IsNullOrWhiteSpace(startupNotebookPath))
            {
                _notebookPath = ExistingLocalPathOrEmpty(startupNotebookPath, fileExists, requireLocalFile);
                if (_notebookPath.Length == 0)
                    SetError(RejectedStartupPathMessage(startupNotebookPath), ErrorField.NotebookPath);
            }
            else
            {
                _notebookPath = ExistingLocalPathOrEmpty(settings.LastNotebookPath, fileExists, requireLocalFile);
            }

            // A held notebook may carry unsaved changes: never navigate away from it or switch files here.
            _browseNotebook = new RelayCommand(p => BrowseNotebook(), p => !IsBusy && _held == null);
            _browseKeyFile = new RelayCommand(p => BrowseKeyFile(), p => !IsBusy);
            _newNotebook = new RelayCommand(p => _showCreate(), p => !IsBusy && _held == null);
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
                _newNotebook.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// True only while the held-notebook back-off delay after a wrong attempt is running (IsBusy is also true then).
        /// The page shows the countdown instead of "Unlocking..." during that time.
        /// </summary>
        public bool IsWaiting
        {
            get { return _isWaiting; }
            private set { SetProperty(ref _isWaiting, value); }
        }

        /// <summary>Shown as a banner when a notebook is held in memory (e.g. its save failed at auto-lock).</summary>
        public string HeldWarning { get; }

        /// <summary>True when re-unlocking a notebook held in memory: the file cannot be changed here.</summary>
        public bool HasHeldNotebook { get { return _held != null; } }

        public ICommand BrowseNotebookCommand { get { return _browseNotebook; } }
        public ICommand BrowseKeyFileCommand { get { return _browseKeyFile; } }
        public ICommand NewNotebookCommand { get { return _newNotebook; } }

        /// <summary>Test seam for the held-notebook back-off delay.</summary>
        internal Func<TimeSpan, Task> DelayAsync { get; set; } = Task.Delay;

        /// <summary>Test seam: runs the open (KDF) or the held-notebook match off the UI thread.</summary>
        internal Func<Action, Task> RunInBackground { get; set; } = Task.Run;

        /// <summary>
        /// Never throws: every failure is reported through <see cref="ErrorMessage"/>. A call made while another is
        /// running returns immediately. The caller keeps ownership of <paramref name="password"/>.
        /// </summary>
        public async Task UnlockAsync(SecureString password)
        {
            if (IsBusy) return;
            IsBusy = true;
            Notebook opened = null;
            bool handedOff = false;
            try
            {
                SetError(null, ErrorField.None);
                if (password == null || password.Length == 0)
                {
                    SetError("Enter a password.", ErrorField.Password);
                    return;
                }
                Notebook held = _held;
                string path = null;
                if (held == null)
                {
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
                }

                string keyFile;
                string keyError = PathInput.CheckExistingKeyFile(KeyFilePath, out keyFile);
                if (keyError != null)
                {
                    SetError(keyError, ErrorField.KeyFilePath);
                    return;
                }

                if (held != null)
                {
                    // CredentialsMatch compares SHA-256 raw keys (no KDF), so a guess costs microseconds; the
                    // escalating delay restores a work factor. IsBusy stays true throughout.
                    bool match = false;
                    await RunInBackground(() => match = HeldMatches(held, password, keyFile));
                    if (!match)
                    {
                        _heldFailures++;
                        TimeSpan wait = HeldBackOff(_heldFailures);
                        SetError(WrongCredentialsMessage + " Try again in " + (int)wait.TotalSeconds + " s.",
                            ErrorField.Password);
                        IsWaiting = true;
                        try
                        {
                            await DelayAsync(wait);
                        }
                        finally
                        {
                            IsWaiting = false;
                        }
                        SetError(WrongCredentialsMessage, ErrorField.Password);
                        return;
                    }
                    _heldFailures = 0;
                    handedOff = true;
                    _onOpened(held);
                    return;
                }

                NotebookStore store = _store;
                Notebook result = null;
                await RunInBackground(() => result = OpenWithNewCredentials(store, path, password, keyFile));
                opened = result;

                // Only the notebook path is remembered: never the key file, never anything about the password.
                _settings.LastNotebookPath = opened.FilePath;
                _settingsStore.Save(_settings);

                if (opened.IsReadOnly) _dialogs.ShowWarning("Read-only", ReadOnlyWarningFor(opened.ReadOnlyReason));

                handedOff = true;
                _onOpened(opened);
            }
            catch (Exception ex)
            {
                SetError(NotebookErrorText.Describe(ex, "Could not open the file: "),
                    ErrorFieldForOpenFailure(ex, _held != null));
                if (!handedOff && opened != null) opened.Dispose();
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// The box an open failure belongs to: a missing or unreadable notebook file to the notebook box (never for a
        /// held notebook, whose box is read-only and which is matched in memory), a wrong password or key file to the
        /// password box, anything else to none.
        /// </summary>
        internal static ErrorField ErrorFieldForOpenFailure(Exception ex, bool held)
        {
            if (ex is WrongCredentialsException) return ErrorField.Password;
            if (!held && (ex is FileNotFoundException || ex is NotebookFormatException)) return ErrorField.NotebookPath;
            return ErrorField.None;
        }

        /// <summary>1 s, 2 s, 4 s, then 8 s for every further consecutive wrong attempt.</summary>
        internal static TimeSpan HeldBackOff(int consecutiveFailures)
        {
            int exponent = Math.Min(Math.Max(consecutiveFailures, 1) - 1, 3);
            return TimeSpan.FromSeconds(1 << exponent);
        }

        private static bool HeldMatches(Notebook held, SecureString password, string keyFile)
        {
            // The attempt's credentials are always disposed; the held notebook keeps its own.
            using (Credentials attempt = Credentials.FromSecureString(password, keyFile))
                return held.CredentialsMatch(attempt);
        }

        private static Notebook OpenWithNewCredentials(NotebookStore store, string path, SecureString password, string keyFile)
        {
            Credentials credentials = Credentials.FromSecureString(password, keyFile);
            try
            {
                return store.Open(path, credentials); // the notebook owns the credentials on success
            }
            catch
            {
                credentials.Dispose();
                throw;
            }
        }

        private void BrowseNotebook()
        {
            string path = _dialogs.PickOpenNotebookPath();
            if (path == null) return;
            NotebookPath = path;
            SetError(null, ErrorField.None);
        }

        private void BrowseKeyFile()
        {
            string path = _dialogs.PickKeyFile(false);
            if (path == null) return;
            KeyFilePath = path;
            SetError(null, ErrorField.None);
        }

        /// <summary>The longest part of a rejected startup path that the error text shows.</summary>
        internal const int MaxShownStartupPathLength = 260;

        /// <summary>The error for a rejected startup path. The path is shown as given, cut to
        /// <see cref="MaxShownStartupPathLength"/> characters plus "..." (never between the halves of a surrogate pair), so
        /// a crafted huge argument cannot flood the window.</summary>
        internal static string RejectedStartupPathMessage(string path)
        {
            string shown = path ?? "";
            if (shown.Length > MaxShownStartupPathLength)
            {
                int cut = MaxShownStartupPathLength;
                if (char.IsHighSurrogate(shown[cut - 1])) cut--;
                shown = shown.Substring(0, cut) + "...";
            }
            return "The file \"" + shown + "\" cannot be opened. Notebooks must be existing files on a local drive.";
        }

        /// <summary>settings.json and the command line are untrusted: pre-fill only an existing local file, otherwise "".
        /// </summary>
        internal static string ExistingLocalPathOrEmpty(string path, Func<string, bool> fileExists)
        {
            return ExistingLocalPathOrEmpty(path, fileExists, LocalPath.RequireLocalFile);
        }

        /// <summary>As above, with the local-path check injected (test seam for LocalPath's drive-type lookup). The check
        /// runs before any file access: a non-local path never reaches <paramref name="fileExists"/>.</summary>
        internal static string ExistingLocalPathOrEmpty(string path, Func<string, bool> fileExists,
            Func<string, string> requireLocalFile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return "";
                string full = requireLocalFile(path);
                return PathInput.IsFullPath(path) && fileExists(full) ? full : "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }

    /// <summary>Validation of typed paths, shared by the unlock and create pages.</summary>
    internal static class PathInput
    {
        /// <summary>Fallback for an invalid path that no page box can be named for.</summary>
        public const string NotFullPathMessage = "Enter a full path or use Browse.";
        public const string KeyFileNotFoundMessage = "Key file not found.";

        // The box labels and Browse button texts of both pages (the XAML uses the same text; PathFieldViewTests checks it).
        public const string NotebookBoxLabel = "Notebook file";
        public const string KeyFileBoxLabel = "Key file (optional)";
        public static readonly string BrowseNotebookLabel = "Browse notebook" + (char)0x2026;
        public static readonly string BrowseKeyFileLabel = "Browse key file" + (char)0x2026;

        public static readonly string NotebookNotFullPathMessage = NotFullPathIn(NotebookBoxLabel, BrowseNotebookLabel);
        public static readonly string KeyFileNotFullPathMessage = NotFullPathIn(KeyFileBoxLabel, BrowseKeyFileLabel);

        public static readonly string NotebookFolderMissingMessage = FolderMissingIn(NotebookBoxLabel, BrowseNotebookLabel);
        public static readonly string KeyFileFolderMissingMessage = FolderMissingIn(KeyFileBoxLabel, BrowseKeyFileLabel);

        private static string NotFullPathIn(string boxLabel, string browseLabel)
        {
            return "Enter a full path in \"" + boxLabel + "\" or use " + browseLabel;
        }

        private static string FolderMissingIn(string boxLabel, string browseLabel)
        {
            return "The folder in \"" + boxLabel + "\" does not exist. Choose an existing folder or use " + browseLabel;
        }

        /// <summary>True when the folder of a full path exists (a drive root has no parent folder: true).</summary>
        public static bool FolderExists(string fullPath)
        {
            string folder = Path.GetDirectoryName(fullPath);
            return folder == null || Directory.Exists(folder);
        }

        /// <summary>
        /// For a typed, non-blank path: returns the error text (not local, invalid, or not a full path; the latter two
        /// as <paramref name="notFullPathMessage"/>) or null with <paramref name="fullPath"/> set.
        /// </summary>
        public static string CheckLocalFullPath(string path, string notFullPathMessage, out string fullPath)
        {
            fullPath = null;
            string full;
            try
            {
                full = LocalPath.RequireLocalFile(path);
            }
            catch (InvalidPathException)
            {
                return notFullPathMessage; // derives from NonLocalPathException: caught first
            }
            catch (NonLocalPathException)
            {
                return NotebookErrorText.NotLocalMessage;
            }
            if (!IsFullPath(path)) return notFullPathMessage;
            fullPath = full;
            return null;
        }

        /// <summary>
        /// True for "C:\..." and UNC paths. False for relative, drive-relative ("C:x") and root-relative ("\x")
        /// paths, which would resolve against whatever the current directory happens to be. Call only after
        /// <see cref="LocalPath.RequireLocalFile(string)"/> succeeded (invalid characters make IsPathRooted throw; UNC paths
        /// are already refused there).
        /// </summary>
        public static bool IsFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string p = path.Trim();
            if (!Path.IsPathRooted(p)) return false;
            if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal)) return true;
            return p.Length >= 3 && p[1] == ':' && (p[2] == '\\' || p[2] == '/');
        }

        /// <summary>
        /// For a key file that must already exist. Blank -> null key file, no error. Otherwise returns the error
        /// text (not local, not a full path, missing) or null with <paramref name="fullPath"/> set.
        /// </summary>
        public static string CheckExistingKeyFile(string keyFilePath, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(keyFilePath)) return null;
            string full;
            string error = CheckLocalFullPath(keyFilePath, KeyFileNotFullPathMessage, out full);
            if (error != null) return error;
            if (!File.Exists(full)) return KeyFileNotFoundMessage;
            fullPath = full;
            return null;
        }
    }

    /// <summary>Maps open/create failures to user-facing text. Exception messages never contain passwords.</summary>
    internal static class NotebookErrorText
    {
        public const string NotLocalMessage = "Only files on this PC can be opened.";

        public static string Describe(Exception ex, string otherPrefix)
        {
            if (ex is WrongCredentialsException) return UnlockViewModel.WrongCredentialsMessage;
            if (ex is FileNotFoundException) return "File not found.";
            if (ex is NotebookFormatException) return "This file is not a valid notebook or KeePass database.";
            // Before the non-local check: InvalidPathException derives from NonLocalPathException.
            if (ex is InvalidPathException) return PathInput.NotFullPathMessage;
            if (ex is NonLocalPathException) return NotLocalMessage;
            if (ex is UnauthorizedAccessException) return "Access denied: " + ex.Message;
            if (ex is IOException) return ex.Message;
            return otherPrefix + ex.Message;
        }
    }
}

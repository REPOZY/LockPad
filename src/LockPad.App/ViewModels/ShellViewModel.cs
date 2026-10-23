using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows.Input;
using KeePassLib.Cryptography.KeyDerivation;
using LockPad.App.Infrastructure;
using LockPad.App.Services;
using LockPad.Core;

namespace LockPad.App.ViewModels
{
    /// <summary>
    /// Root view model: hosts the current page (unlock, create or editor) in the single main window. Owns the open
    /// notebook, the idle lock, manual lock, the lock on Windows lock or sleep, safe close, switching to another notebook
    /// (File > Open / New notebook, which closes the current one exactly like closing the window), the window title and
    /// session-end handling.
    /// <para>
    /// Lock invariants: a dirty notebook is never disposed implicitly; when its save fails at lock time it is held in
    /// memory and only the unlock page created on that unlocked-to-locked transition can bring it back; no lock
    /// teardown happens while a dialog or a save is in progress; every lock path clears LockPad's clipboard content and
    /// empties the editor view.
    /// </para>
    /// <para>
    /// When the idle timeout expires while the lock has to wait for a dialog or a save, the editor is obscured at once
    /// (note text, list and find bar hidden; the view model is left intact for the dialog's caller) and LockPad's
    /// clipboard content is cleared. The dialog is not touched. An obscured editor is never revealed again: activity
    /// does not reset it, and the first Tick, resume or input after the dialog or save ends performs the normal lock
    /// (save, then lock, or hold when the save fails).
    /// </para>
    /// </summary>
    public sealed class ShellViewModel : ObservableObject
    {
        internal const string HeldWarningPrefix = "Unsaved changes are held in memory. Saving failed: ";
        internal const string HeldCloseTitle = "Unsaved changes";
        internal const string HeldCloseMessage =
            "Unsaved changes are held in memory because saving failed. Close anyway and lose them?";
        internal const string CouldNotSaveTitle = "Could not save";
        internal const string CloseChoiceSuffix = "\n\nSave As keeps your changes in a new file. Close without saving "
            + "discards them. Cancel keeps the window open.";
        /// <summary>The same choice while switching notebooks (File > Open / New notebook).</summary>
        internal const string SwitchChoiceSuffix = "\n\nSave As keeps your changes in a new file. Close without saving "
            + "discards them. Cancel keeps this notebook open.";
        internal const string CannotOpenTitle = "Cannot open notebook";
        internal const string DiscardTitle = "Close without saving";
        internal const string DiscardMessage = "Discard all unsaved changes? This cannot be undone.";
        /// <summary>The window title on every page but the (unobscured) editor, and the suffix of the editor's.</summary>
        internal const string AppTitle = "LockPad";

        private const int MinIdleMinutes = 1;
        private const int MaxIdleMinutes = 60;

        private readonly IdleLockPolicy _idle;
        private readonly RelayCommand _openNotebookCommand;
        private readonly RelayCommand _newNotebookCommand;
        private object _currentView;
        private Notebook _openNotebook;
        private EditorViewModel _editor;
        private string _lockCountdownText = "";
        private DateTime _lastActivityWall;
        private bool _locking;
        /// <summary>True while PrepareForSessionEnd saves without dialogs. Argon2's waits pump sent messages, so a
        /// WM_CLOSE, a menu command or a timer can arrive inside that save: close, switch and lock all refuse
        /// meanwhile (red team L1: a re-entrant close disposed the notebook under the save).</summary>
        private bool _sessionSaving;
        private bool _lockPending;
        private string _startupNotebookPath;

        /// <param name="startupNotebookPath">The notebook path LockPad was started with (StartupArguments), or null. It is
        /// handed to the first unlock page only; later unlock pages (after a lock) pre-fill as before.</param>
        public ShellViewModel(NotebookStore store, SettingsStore settingsStore, AppSettings settings,
            IDialogService dialogs, IClipboardService clipboard, IClock clock, Func<KdfParameters> kdfFactory,
            string startupNotebookPath = null)
        {
            _startupNotebookPath = startupNotebookPath;
            Store = store;
            SettingsStore = settingsStore;
            Settings = settings;
            Dialogs = dialogs;
            Clipboard = clipboard;
            Clock = clock;
            KdfFactory = kdfFactory;
            _idle = new IdleLockPolicy(clock, IdleTimeoutFromSettings());
            WallClock = () => DateTime.UtcNow;
            _lastActivityWall = WallClock();
            OpenSettingsCommand = new RelayCommand(p => OpenSettings());
            OpenShortcutsCommand = new RelayCommand(p => Dialogs.ShowShortcuts());
            OpenAboutCommand = new RelayCommand(p => Dialogs.ShowAbout());
            _openNotebookCommand = new RelayCommand(p => OpenNotebook(), p => CanSwitchNotebook());
            _newNotebookCommand = new RelayCommand(p => NewNotebook(), p => CanSwitchNotebook());
            LockCommand = new RelayCommand(p => Lock());
        }

        /// <summary>File > Open notebook (Ctrl+O). See <see cref="OpenNotebook"/>.</summary>
        public ICommand OpenNotebookCommand { get { return _openNotebookCommand; } }

        /// <summary>File > New notebook. See <see cref="NewNotebook"/>.</summary>
        public ICommand NewNotebookCommand { get { return _newNotebookCommand; } }

        /// <summary>File > Lock: the same as Ctrl+L (<see cref="Lock"/>). The menu item is disabled without an editor.</summary>
        public ICommand LockCommand { get; private set; }

        /// <summary>
        /// "&lt;file name&gt; - LockPad" while the editor is shown (file name only, never the folder; "*" first while the
        /// notebook is dirty, including a pending re-lock); "LockPad" on every other page and while the editor is
        /// obscured, so nothing about the notebook shows while it locks.
        /// </summary>
        public string WindowTitle
        {
            get
            {
                EditorViewModel editor = _editor;
                if (editor == null || !ReferenceEquals(_currentView, editor) || editor.IsObscured) return AppTitle;
                Notebook nb = editor.Notebook;
                return (nb.IsDirty ? "*" : "") + editor.FileName + " - " + AppTitle;
            }
        }

        /// <summary>
        /// Open and New are offered on the editor and on the plain unlock page. Never on the held unlock page (a held
        /// notebook is never switched away from), the create page, a busy page, an obscured editor, or while a dialog,
        /// a save or a lock is in progress.
        /// </summary>
        private bool CanSwitchNotebook()
        {
            if (_locking || _sessionSaving || (Dialogs != null && Dialogs.IsShowingDialog)) return false;
            EditorViewModel editor = _editor;
            if (editor != null)
                return ReferenceEquals(_currentView, editor) && !editor.IsObscured && !editor.IsSaving;
            var unlock = _currentView as UnlockViewModel;
            return unlock != null && !unlock.HasHeldNotebook && !unlock.IsBusy && _openNotebook == null;
        }

        /// <summary>The editor is still the one shown, and not obscured (a lock is not pending).</summary>
        private bool IsShownAndClear(EditorViewModel editor)
        {
            return ReferenceEquals(_editor, editor) && ReferenceEquals(_currentView, editor) && !editor.IsObscured;
        }

        /// <summary>
        /// File > Open notebook. On the plain unlock page: Browse notebook. In the editor: the open-file picker first
        /// (Cancel changes nothing); a choice made while the editor was obscured or left meanwhile is discarded (the
        /// pending lock proceeds). The chosen path is then checked like the unlock page checks it (an existing file on a
        /// local drive) BEFORE anything is closed: a rejected path shows "Cannot open notebook" and the current notebook
        /// stays open, untouched. Otherwise the current notebook is closed like the window closes (saved, or the user
        /// is asked; Cancel keeps everything) and the plain unlock page shows the chosen file (validated again there).
        /// When the editor was obscured during that close, the switch ends like the lock it became: on the plain unlock
        /// page of the notebook just closed.
        /// </summary>
        public void OpenNotebook()
        {
            if (!CanSwitchNotebook()) return;
            EditorViewModel editor = _editor;
            if (editor == null)
            {
                ExecuteIfEnabled(((UnlockViewModel)_currentView).BrowseNotebookCommand);
                return;
            }
            string path = Dialogs.PickOpenNotebookPath();
            if (path == null || !IsShownAndClear(editor)) return;
            if (UnlockViewModel.ExistingLocalPathOrEmpty(path, System.IO.File.Exists).Length == 0)
            {
                Dialogs.ShowError(CannotOpenTitle, UnlockViewModel.RejectedStartupPathMessage(path));
                return;
            }
            if (!ReleaseForSwitch(editor)) return;
            if (editor.IsObscured) ShowUnlock();
            else ShowUnlockForChosenPath(path);
        }

        /// <summary>
        /// File > New notebook. On the plain unlock page: its New notebook button. In the editor: closes the current
        /// notebook like the window closes (saved, or the user is asked; Cancel keeps everything), then the create
        /// page; the plain unlock page instead when the editor was obscured during that close.
        /// </summary>
        public void NewNotebook()
        {
            if (!CanSwitchNotebook()) return;
            EditorViewModel editor = _editor;
            if (editor == null)
            {
                ExecuteIfEnabled(((UnlockViewModel)_currentView).NewNotebookCommand);
                return;
            }
            if (!ReleaseForSwitch(editor)) return;
            if (editor.IsObscured) ShowUnlock();
            else ShowCreate();
        }

        /// <summary>As <see cref="SaveOrAskThenRelease"/>. When its teardown throws half-way, the notebook is already
        /// released (every teardown step runs): the plain unlock page is shown before the exception propagates, so the
        /// window is never left blank. A failure before the release (the editor is still there) changes nothing.</summary>
        private bool ReleaseForSwitch(EditorViewModel editor)
        {
            try
            {
                return SaveOrAskThenRelease(editor, SwitchChoiceSuffix);
            }
            catch
            {
                if (_editor == null && _currentView == null && _openNotebook == null) ShowUnlock();
                throw;
            }
        }

        private static void ExecuteIfEnabled(ICommand command)
        {
            if (command.CanExecute(null)) command.Execute(null);
        }

        /// <summary>The window title and the Open / New enabled state may have changed.</summary>
        private void RaiseNotebookStateChanged()
        {
            try
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
            finally
            {
                RefreshSwitchCommands();
            }
        }

        /// <summary>Tells the menu that File > Open / New may have changed. Never throws: a failing CanExecuteChanged
        /// subscriber must not stop a lock, a teardown or the timer (fix round 2, Y2).</summary>
        private void RefreshSwitchCommands()
        {
            try { _openNotebookCommand.RaiseCanExecuteChanged(); }
            catch (Exception) { }
            try { _newNotebookCommand.RaiseCanExecuteChanged(); }
            catch (Exception) { }
        }

        private void OnEditorPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, _editor)) return;
            string name = e.PropertyName;
            if (name == nameof(EditorViewModel.StatusText) || name == nameof(EditorViewModel.FileName)
                || name == nameof(EditorViewModel.IsObscured) || string.IsNullOrEmpty(name))
                RaiseNotebookStateChanged();
        }

        public NotebookStore Store { get; private set; }
        public SettingsStore SettingsStore { get; private set; }

        /// <summary>
        /// The one shared settings instance. It is mutated in place and never replaced, because other components
        /// (clipboard timeout provider, editor, unlock page) hold the same reference.
        /// </summary>
        public AppSettings Settings { get; }

        /// <summary>Raised after settings were changed and saved (OK in the settings dialog).</summary>
        public event EventHandler SettingsChanged;

        public ICommand OpenSettingsCommand { get; private set; }

        /// <summary>Opens the Shortcuts window (Help > Keyboard shortcuts and F1; works on every page).</summary>
        public ICommand OpenShortcutsCommand { get; private set; }

        /// <summary>Opens the About window (Help > About LockPad; works on every page).</summary>
        public ICommand OpenAboutCommand { get; private set; }

        /// <summary>Applies a theme to the running app. Test seam (the real one needs an Application).</summary>
        internal Action<AppTheme> ApplyTheme { get; set; } = ThemeService.Apply;

        /// <summary>Applies capture protection to every window of the running app. Test seam.</summary>
        internal Action<bool> ApplyCaptureProtection { get; set; } = CaptureProtection.ApplyToApp;

        /// <summary>
        /// Shows the settings dialog (the Settings menu item, the only place to set the theme); on OK copies the values
        /// into <see cref="Settings"/>, saves, and applies capture protection and the theme.
        /// </summary>
        public void OpenSettings()
        {
            var vm = new SettingsViewModel(Settings);
            if (!Dialogs.ShowSettings(vm)) return;
            CommitSettings(vm.ApplyTo(Settings));
        }

        private void CommitSettings(AppSettings normalized)
        {
            Settings.IdleLockMinutes = normalized.IdleLockMinutes;
            Settings.ClipboardClearSeconds = normalized.ClipboardClearSeconds;
            Settings.Theme = normalized.Theme;
            Settings.HideFromScreenCapture = normalized.HideFromScreenCapture;
            SettingsStore.Save(Settings);
            // The security setting first (it never throws): a failing theme swap must not leave protection off.
            ApplyCaptureProtection(Settings.HideFromScreenCapture);
            ApplyTheme(ThemeService.Parse(Settings.Theme));
            ApplyIdleTimeout(); // keeps the last-activity time: a shorter timeout may lock on the next Tick
            EventHandler handler = SettingsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public IDialogService Dialogs { get; private set; }
        public IClipboardService Clipboard { get; private set; }

        /// <summary>Monotonic clock for the idle policy (does not jump with the Windows clock).</summary>
        public IClock Clock { get; private set; }

        public Func<KdfParameters> KdfFactory { get; private set; }

        /// <summary>
        /// Wall-clock companion to <see cref="Clock"/>: the monotonic clock may not advance while the PC sleeps, so the
        /// idle lock also expires when wall-clock time since the last activity reaches the timeout. Test seam.
        /// </summary>
        internal Func<DateTime> WallClock { get; set; }

        public object CurrentView
        {
            get { return _currentView; }
            private set
            {
                try
                {
                    SetProperty(ref _currentView, value);
                }
                finally
                {
                    RaiseNotebookStateChanged();
                }
            }
        }

        /// <summary>The editor while a notebook is shown; null on the unlock and create pages (e.g. when locked).</summary>
        public EditorViewModel Editor
        {
            get { return _editor; }
            private set
            {
                EditorViewModel old = _editor;
                if (ReferenceEquals(old, value)) return;
                // Re-subscribed before any listener runs: a throwing listener must not leave the title on the old editor.
                if (old != null) old.PropertyChanged -= OnEditorPropertyChanged;
                if (value != null) value.PropertyChanged += OnEditorPropertyChanged;
                try
                {
                    SetProperty(ref _editor, value);
                }
                finally
                {
                    try
                    {
                        OnPropertyChanged(nameof(IsEditorShown));
                    }
                    finally
                    {
                        RaiseNotebookStateChanged();
                    }
                }
            }
        }

        /// <summary>True while an editor is shown (also while obscured). File > Save, Save As, Change master password and
        /// Lock are enabled only then.</summary>
        public bool IsEditorShown
        {
            get { return _editor != null; }
        }

        /// <summary>"Locks in m:ss" while the editor is shown; empty otherwise.</summary>
        public string LockCountdownText
        {
            get { return _lockCountdownText; }
            private set { SetProperty(ref _lockCountdownText, value ?? ""); }
        }

        /// <summary>
        /// Shows the unlock page. With <paramref name="held"/>, the page re-unlocks that in-memory notebook (its
        /// unsaved changes are kept) instead of opening a file; <paramref name="heldWarning"/> is shown as a banner.
        /// The first call also hands over the startup notebook path, which is then forgotten.
        /// </summary>
        public void ShowUnlock(Notebook held = null, string heldWarning = null)
        {
            string startup = _startupNotebookPath;
            _startupNotebookPath = null;
            ShowUnlockPage(held, heldWarning, startup);
        }

        /// <summary>The plain unlock page for a file chosen with File > Open notebook. Untrusted like the startup
        /// argument, and validated the same way: pre-filled only when it is an existing file on a local drive, otherwise
        /// an empty box and the rejected-path message.</summary>
        private void ShowUnlockForChosenPath(string path)
        {
            _startupNotebookPath = null;
            ShowUnlockPage(null, null, path);
        }

        private void ShowUnlockPage(Notebook held, string heldWarning, string prefillPath)
        {
            LeaveEditor();
            LockCountdownText = "";
            var page = new UnlockViewModel(Store, SettingsStore, Settings, Dialogs, ShowEditor, ShowCreate,
                held, heldWarning, prefillPath);
            page.PropertyChanged += OnPagePropertyChanged;
            CurrentView = page;
        }

        public void ShowCreate()
        {
            LeaveEditor();
            LockCountdownText = "";
            var page = new CreateNotebookViewModel(Store, Dialogs, KdfFactory, OnNotebookCreated, () => ShowUnlock());
            page.PropertyChanged += OnPagePropertyChanged;
            CurrentView = page;
        }

        /// <summary>A Windows lock or sleep that arrived during an unlock or create attempt applies to that attempt only:
        /// when the attempt ends (IsBusy false; after it handed over to ShowEditor when it succeeded), it is dropped.</summary>
        private void OnPagePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "IsBusy") return;
            var unlock = sender as UnlockViewModel;
            var create = sender as CreateNotebookViewModel;
            if ((unlock != null && !unlock.IsBusy) || (create != null && !create.IsBusy)) _lockPending = false;
            if (ReferenceEquals(sender, _currentView)) RaiseNotebookStateChanged(); // File > Open / New follow IsBusy
        }

        /// <summary>A Windows lock or sleep with no editor shown: remembered only while an unlock or create attempt is in
        /// flight (its KDF, or its modal "Read-only" / "Back up your key file" warning), so ShowEditor locks at once.</summary>
        private void RememberLockForPageInFlight()
        {
            if (IsPageBusy()) _lockPending = true;
        }

        /// <summary>A new notebook is remembered like an unlocked one (only its path), so the next start offers it.</summary>
        private void OnNotebookCreated(Notebook notebook)
        {
            if (notebook == null) throw new ArgumentNullException(nameof(notebook));
            Settings.LastNotebookPath = notebook.FilePath; // the shared instance is mutated in place, never replaced
            SettingsStore.Save(Settings); // best effort, never throws
            ShowEditor(notebook);
        }

        /// <summary>
        /// Shows the editor for <paramref name="notebook"/>, which this shell then owns: a previously open notebook
        /// is disposed when a different one replaces it. Re-showing the same (held) notebook keeps it. Starts a full
        /// idle timeout.
        /// </summary>
        /// <exception cref="InvalidOperationException">A different notebook with unsaved changes is still open. It is
        /// never disposed implicitly (programming error: the held unlock page only restores the held notebook).</exception>
        public void ShowEditor(Notebook notebook)
        {
            if (notebook == null) throw new ArgumentNullException(nameof(notebook));

            // Red-team M1: Windows locked or the PC went to sleep while the unlock or create that leads here was in
            // flight. Never leave the editor shown. The held notebook stays on its held page; any other (just opened or
            // created, so clean) notebook is shown and locked at once, exactly as Ctrl+L right after it appeared: it is
            // disposed, the plain unlock page shows its path, and the clipboard is cleared. Nothing renders meanwhile.
            bool lockAtOnce = _lockPending;
            _lockPending = false;
            if (lockAtOnce && _editor == null && ReferenceEquals(notebook, _openNotebook)) return;

            Notebook previous = _openNotebook;
            bool replacing = previous != null && !ReferenceEquals(previous, notebook);
            if (replacing && previous.IsDirty)
                throw new InvalidOperationException("A notebook with unsaved changes is still open; it is never disposed implicitly.");

            LeaveEditor();
            _openNotebook = notebook;
            if (replacing) previous.Dispose();
            ResetIdle();
            Editor = new EditorViewModel(notebook, Store, Dialogs, SettingsStore, Settings);
            CurrentView = Editor;
            UpdateCountdown();
            if (lockAtOnce) Lock();
        }

        /// <summary>
        /// Keyboard or mouse input in the main window. Input that arrives after the idle deadline (e.g. the first mouse
        /// move after waking the PC, processed before the next Tick) never extends the session: it locks instead, or
        /// obscures while the lock must wait or before the save of unsaved changes. An obscured editor is never revived
        /// by input.
        /// </summary>
        public void RecordActivity()
        {
            if (_editor != null && IsLockDue())
            {
                LockOrHideFirst();
                return;
            }
            ResetIdle();
        }

        /// <summary>
        /// Called every second by the main window's timer. Locks when the editor is shown and the idle timeout expired
        /// (by the monotonic clock or by wall-clock time) or the editor is obscured. While a dialog or a save is in
        /// progress it obscures the editor instead, and the lock happens on the first Tick after it ends. Updates
        /// <see cref="LockCountdownText"/>. Then, and only then, refreshes File > Open / New (a dialog, a save or a lock
        /// ending is not observable otherwise): a failing CanExecuteChanged subscriber can never stop the idle lock.
        /// </summary>
        public void Tick()
        {
            LockIfDueAndUpdateCountdown();
            RefreshSwitchCommands(); // swallows a subscriber's failure: nothing throws out of the timer from here
        }

        private void LockIfDueAndUpdateCountdown()
        {
            ApplyIdleTimeout();
            // Windows clock set backwards: restart the wall-clock measure from now, so a later sleep is still caught
            // (otherwise the negative delta would mask wall-clock expiry until the clock caught up again).
            DateTime wallNow = WallClock();
            if (_lastActivityWall > wallNow) _lastActivityWall = wallNow;
            if (_editor == null)
            {
                _hiddenOutsideTick = null;
                LockCountdownText = "";
                return;
            }
            if (IsLockDue())
            {
                bool waitForRedraw = ReferenceEquals(_hiddenOutsideTick, _editor);
                _hiddenOutsideTick = null;
                if (!IsLockDeferred && !MustHideBeforeSaving() && !waitForRedraw)
                {
                    Lock();
                    return;
                }
                ObscureForPendingLock();
            }
            UpdateCountdown();
        }

        /// <summary>The editor that late input or a resume hid for the lock's save. The timer runs at a higher
        /// dispatcher priority than rendering, so a Tick already queued could start the save before the window redrew;
        /// the first Tick after such a hide therefore only clears this (re-attack 2026-10-22).</summary>
        private EditorViewModel _hiddenOutsideTick;

        /// <summary>The PC resumed from sleep (marshalled to the UI thread by the window). Locks when the idle timeout
        /// passed while asleep; while deferred, or before the save of unsaved changes, it obscures instead (then a
        /// later Tick locks).</summary>
        internal void OnResume()
        {
            EditorViewModel editor = _editor;
            if (editor == null) return;
            // Already hidden for the lock's save (by a Tick, late input or the suspend): a Tick saves and locks once the
            // window has redrawn; a resume queued before the redraw must not start the save (final re-attack, Low 1).
            if (editor.IsObscured && editor.Notebook.IsDirty && !editor.Notebook.IsReadOnly) return;
            if (IsLockDue()) LockOrHideFirst();
        }

        /// <summary>The resume and late-input locks: as <see cref="LockOrObscure"/>, but hides first when a save is
        /// needed (see <see cref="MustHideBeforeSaving"/>); the save then waits for the second Tick.</summary>
        private void LockOrHideFirst()
        {
            if (MustHideBeforeSaving())
            {
                // Marked first: the obscure step rethrows a listener's failure after hiding (final re-attack, Low 2).
                _hiddenOutsideTick = _editor;
                ObscureForPendingLock();
            }
            else LockOrObscure();
        }

        /// <summary>
        /// Full check 2026-10-22, L1: the save of unsaved changes before a lock runs on the UI thread and takes seconds
        /// (two or three key derivations), with the window frozen as it was. So the idle, resume and late-input locks
        /// first hide the notes and clear the clipboard, and the next Tick, after the window has redrawn, saves and
        /// locks. Ctrl+L and a Windows lock lock at once: the user is there, or the screen is already locked.
        /// </summary>
        private bool MustHideBeforeSaving()
        {
            EditorViewModel editor = _editor;
            return editor != null && !editor.IsObscured && editor.Notebook.IsDirty && !editor.Notebook.IsReadOnly;
        }

        /// <summary>
        /// Windows locked the session, switched user or disconnected a remote session, or the PC is about to sleep
        /// (the window queues this after <see cref="OnSuspending"/>). User decision 2026-10-19: always locks, exactly
        /// like Ctrl+L: saves, then locks, or holds when the save fails; while a dialog or a save is in progress it
        /// obscures at once and clears the clipboard, and the lock follows when it ends. No editor (already locked, the
        /// held screen, the unlock or create page): no-op, except that during an unlock or create attempt it is
        /// remembered, so that attempt ends locked instead of showing the editor (red-team M1).
        /// </summary>
        public void OnWindowsLocked()
        {
            if (_editor == null)
            {
                RememberLockForPageInFlight();
                return;
            }
            LockOrObscure();
        }

        /// <summary>
        /// The PC is about to sleep: the fast, synchronous part, run before the window queues the full lock. Hides the
        /// note content and clears LockPad's clipboard content; no save, no teardown. If the queued lock is cut off by
        /// the sleep, the obscured editor is locked by the first Tick after resume. No editor: as
        /// <see cref="OnWindowsLocked"/> (remembered during an unlock or create attempt, otherwise no-op).
        /// </summary>
        public void OnSuspending()
        {
            if (_editor == null)
            {
                RememberLockForPageInFlight();
                return;
            }
            ObscureForPendingLock();
        }

        /// <summary>The idle timeout expired, or it did earlier and the editor was obscured (a later, longer timeout
        /// or activity never cancels that).</summary>
        private bool IsLockDue()
        {
            return (_editor != null && _editor.IsObscured) || IsIdleExpired();
        }

        private void LockOrObscure()
        {
            if (IsLockDeferred) ObscureForPendingLock();
            else Lock();
        }

        /// <summary>
        /// The lock is due but must wait for a dialog or a save: hide the note content now and clear LockPad's
        /// clipboard content. Nothing is torn down, disposed or cleared, and the dialog is left alone. Once per editor.
        /// </summary>
        private void ObscureForPendingLock()
        {
            EditorViewModel editor = _editor;
            if (editor == null || editor.IsObscured) return;
            // Both steps run even if the first throws (e.g. a PropertyChanged listener).
            RunAll(editor.Obscure, ClearClipboard);
        }

        /// <summary>
        /// Locks now (Ctrl+L, idle timeout, resume, Windows lock or sleep). No editor shown: no-op, so an existing (held)
        /// unlock page is never replaced. Clean or read-only: clears the clipboard, disposes the notebook and shows the
        /// unlock page. Dirty:
        /// saves without any dialog (the user may be away); when that fails the notebook stays in memory and the unlock
        /// page offers to restore it, with the failure as a warning.
        /// </summary>
        public void Lock()
        {
            EditorViewModel editor = _editor;
            if (editor == null || _locking || _sessionSaving) return;
            _locking = true;
            _hiddenOutsideTick = null;
            try
            {
                Notebook nb = editor.Notebook;
                if (nb.IsDirty && !nb.IsReadOnly)
                {
                    SaveResult result = SaveWithoutDialogs(nb);
                    if (result.Status != SaveStatus.Saved)
                    {
                        Hold(editor, nb, result);
                        return;
                    }
                }
                // Every step runs even if an earlier one throws: the editor must never stay on screen.
                RunAll(
                    () => Editor = null,
                    editor.ClearForLock,
                    () => CurrentView = null,
                    ClearClipboard,
                    DisposeOpenNotebook,
                    // Its own step, so a throwing countdown listener cannot stop the unlock page from being built
                    // (final re-attack 2026-10-22, L2).
                    () => LockCountdownText = "",
                    () => ShowUnlock());
            }
            finally
            {
                _locking = false;
                LockCountdownText = "";
                // The new page appeared while _locking was still true.
                RefreshSwitchCommands();
            }
        }

        /// <summary>
        /// Called when the main window is asked to close; false keeps it open. Never closes while the unlock (or create)
        /// page runs work in the background, or while a dialog or a save is in progress. A held notebook asks before
        /// its unsaved changes are dropped: twice, both default No, like "Close without saving". A dirty editor saves
        /// first (the user is present, so the external-change question may be asked). A failure other than the user's Cancel asks "Could not save": Save As... (the normal
        /// Save As flow with its guards; closes only when it succeeds), Close without saving (closes only after a
        /// second, default-No confirmation) or Cancel (stays open), so a lasting failure never traps the user. When
        /// true, the notebook is disposed, the view is emptied and the clipboard was cleared once.
        /// </summary>
        public bool TryClose()
        {
            // A lock in progress (WM_CLOSE can arrive inside its save) or a busy page: try again later.
            if (_locking || _sessionSaving || IsPageBusy()) return false;

            EditorViewModel editor = _editor;
            if (editor == null)
            {
                Notebook held = _openNotebook;
                if (held == null) return true;
                if (held.IsDirty)
                {
                    // As safe as "Close without saving": two confirmations, both default No (Enter and Esc = No).
                    if (!Dialogs.ConfirmDefaultNo(HeldCloseTitle, HeldCloseMessage)) return false;
                    if (!Dialogs.ConfirmDefaultNo(DiscardTitle, DiscardMessage)) return false;
                    // The dialogs' modal loops run timers and input: close only what was confirmed.
                    if (!ReferenceEquals(_openNotebook, held) || _editor != null) return false;
                }
                DisposeOpenNotebook();
                ClearClipboard();
                return true;
            }

            return SaveOrAskThenRelease(editor, CloseChoiceSuffix);
        }

        /// <summary>
        /// Closes the editor's notebook while the user is present: the one implementation shared by closing the window
        /// (<see cref="TryClose"/>) and switching notebooks (File > Open / New notebook). False, with the editor and the
        /// notebook exactly as they were, while a dialog or a save is in progress, or when the user cancels. A dirty
        /// notebook (or a pending re-lock) saves first, with the normal dialogs (the external-change question may be
        /// asked). A failure other than the user's Cancel asks "Could not save": Save As... (the normal Save As flow
        /// with its guards; continues only when it succeeds), Close without saving (only after a second, default-No
        /// confirmation) or Cancel. When true: the editor was emptied and detached (undo-delete history included), the
        /// view is empty, the notebook is disposed and LockPad's clipboard content was cleared once.
        /// </summary>
        /// <param name="choiceSuffix">The end of the "Could not save" text: <see cref="CloseChoiceSuffix"/> when the
        /// window closes, <see cref="SwitchChoiceSuffix"/> when switching notebooks.</param>
        private bool SaveOrAskThenRelease(EditorViewModel editor, string choiceSuffix)
        {
            if (_locking || _sessionSaving || Dialogs.IsShowingDialog || editor.IsSaving) return false;

            Notebook nb = editor.Notebook;
            if (nb.IsDirty && !nb.IsReadOnly)
            {
                SaveResult result = editor.SaveNow(false);
                if (result.Status != SaveStatus.Saved)
                {
                    // Cancelled / ExternalChangeDetected: the user chose Cancel (or closed the picker); no extra dialog.
                    if (result.Status == SaveStatus.Cancelled || result.Status == SaveStatus.ExternalChangeDetected)
                        return false;
                    if (!ResolveCloseSaveFailure(editor, result, choiceSuffix)) return false;
                }
            }

            // Every step runs even if an earlier one throws (the first exception is rethrown afterwards: the window then
            // stays open; a switch then shows the plain unlock page): the editor must not stay on screen and the notebook
            // must not stay open.
            RunAll(
                () => Editor = null,
                editor.ClearForLock,
                () => CurrentView = null,
                () => LockCountdownText = "",
                DisposeOpenNotebook,
                ClearClipboard);
            return true;
        }

        /// <summary>
        /// The close-time "Could not save" choice (also when switching notebooks). True when the notebook may be
        /// released: Save As succeeded, or the user confirmed discarding the unsaved changes. Never used on session end
        /// (that path stays quiet and refuses).
        /// </summary>
        private bool ResolveCloseSaveFailure(EditorViewModel editor, SaveResult failure, string choiceSuffix)
        {
            CloseSaveFailureChoice choice = Dialogs.AskCloseAfterSaveFailure(CouldNotSaveTitle,
                failure.Message + choiceSuffix);
            // The dialog's modal loop runs timers: if the editor was replaced meanwhile, close nothing.
            if (!ReferenceEquals(_editor, editor)) return false;

            if (choice == CloseSaveFailureChoice.SaveAs)
            {
                // The existing flow: picker, never replaces an existing file, shows "Save failed" on failure.
                SaveResult saved = editor.SaveAsNow();
                return saved.Status == SaveStatus.Saved && ReferenceEquals(_editor, editor);
            }
            if (choice == CloseSaveFailureChoice.CloseWithoutSaving)
                return Dialogs.ConfirmDefaultNo(DiscardTitle, DiscardMessage) && ReferenceEquals(_editor, editor);
            return false; // Cancel, Esc, or the dialog closed: stay open, nothing is lost
        }

        /// <summary>
        /// Windows is logging off or shutting down; false cancels the session end (Windows then reports that LockPad
        /// is preventing shutdown). No dialogs. A dirty editor saves (also while a dialog is open: the save shows
        /// none); when that fails it cancels and locks holding the notebook, unless a dialog is open, in which case the
        /// editor stays in place because the dialog may still use it. A held notebook gets one more save attempt;
        /// cancels unless it succeeds. Clean or read-only (or saved): allows after emptying the undo-delete stack and one
        /// clipboard clear (the notebook stays open in case the session end is aborted). Cancels while a save or a lock is in progress (Argon2's waits pump sent
        /// messages, so WM_QUERYENDSESSION can arrive inside one).
        /// </summary>
        public bool PrepareForSessionEnd()
        {
            if (_locking || _sessionSaving) return false;
            EditorViewModel editor = _editor;
            if (editor == null)
            {
                Notebook held = _openNotebook;
                if (held != null && held.IsDirty)
                {
                    var unlock = CurrentView as UnlockViewModel;
                    if (unlock != null && unlock.IsBusy) return false; // never save under the background compare
                    if (SaveDuringSessionEnd(held).Status != SaveStatus.Saved) return false;
                    if (!ReferenceEquals(_openNotebook, held) || _editor != null) return false; // changed meanwhile
                    DisposeOpenNotebook();
                    ShowUnlock(); // nothing is held any more
                }
                ClearClipboard();
                return true;
            }

            if (editor.IsSaving) return false; // a save is half done: never start another one on top of it

            Notebook nb = editor.Notebook;
            if (nb.IsDirty && !nb.IsReadOnly)
            {
                SaveResult result = SaveDuringSessionEnd(nb);
                if (result.Status != SaveStatus.Saved)
                {
                    if (!Dialogs.IsShowingDialog) Hold(editor, nb, result);
                    return false;
                }
            }
            // Red-team L2: the session is ending, so deleted notes are no longer restorable, even if the close that
            // normally follows is refused (a dialog or a save in progress). The editor itself stays in case the session
            // end is aborted.
            RunAll(editor.ClearUndo, ClearClipboard);
            return true;
        }

        /// <summary>The quiet session-end save, with close, switch and lock refused while it runs (see
        /// <see cref="_sessionSaving"/>).</summary>
        private SaveResult SaveDuringSessionEnd(Notebook nb)
        {
            bool outer = _sessionSaving;
            _sessionSaving = true;
            try
            {
                return SaveWithoutDialogs(nb);
            }
            finally
            {
                _sessionSaving = outer;
            }
        }

        /// <summary>
        /// The unlocked-to-locked transition after a failed save: the notebook stays open, unsaved. Refused (fix round
        /// 2, Y1) when the notebook was disposed meanwhile: nothing can be restored, so there is no "held in memory"
        /// page; the editor is emptied and the plain unlock page shown. Also refused when this editor is no longer the
        /// shown one: then nothing is touched (whoever replaced it owns its notebook; a live notebook is never orphaned).
        /// </summary>
        private void Hold(EditorViewModel editor, Notebook nb, SaveResult failure)
        {
            if (nb.IsDisposed)
            {
                bool current = ReferenceEquals(_editor, editor);
                RunAll(
                    () => { if (current) Editor = null; },
                    editor.ClearForLock,
                    () => { if (current) LockCountdownText = ""; },
                    () => { if (current) CurrentView = null; },
                    () => { if (ReferenceEquals(_openNotebook, nb)) _openNotebook = null; },
                    () => { if (current) ShowUnlock(); },
                    ClearClipboard);
                return;
            }
            if (!ReferenceEquals(_editor, editor)) return;
            string warning = HeldWarningPrefix + failure.Message;
            // Every step runs even if an earlier one throws; the held notebook is never disposed.
            RunAll(
                () => Editor = null,
                editor.ClearForLock,
                () => LockCountdownText = "",
                () => CurrentView = null,
                () => ShowUnlock(nb, warning),
                ClearClipboard);
        }

        /// <summary>
        /// Runs every step even when an earlier one throws (e.g. a PropertyChanged listener), then rethrows the first
        /// exception with its original stack. Used for teardown, where stopping halfway could leave note text on screen
        /// with no idle lock, or a notebook open.
        /// </summary>
        private static void RunAll(params Action[] steps)
        {
            ExceptionDispatchInfo first = null;
            foreach (Action step in steps)
            {
                try
                {
                    step();
                }
                catch (Exception ex)
                {
                    if (first == null) first = ExceptionDispatchInfo.Capture(ex);
                }
            }
            if (first != null) first.Throw();
        }

        /// <summary>Never throws: an unexpected failure is treated like a failed save, so the notebook is held, not lost.</summary>
        private SaveResult SaveWithoutDialogs(Notebook nb)
        {
            try
            {
                return Store.Save(nb, false);
            }
            catch (Exception ex)
            {
                string message = string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message;
                return SaveResult.Fail(SaveStatus.IoError, message);
            }
        }

        private void DisposeOpenNotebook()
        {
            Notebook nb = _openNotebook;
            _openNotebook = null;
            if (nb != null) nb.Dispose();
        }

        private void ClearClipboard()
        {
            IClipboardService clipboard = Clipboard;
            if (clipboard != null) clipboard.ClearIfOwned();
        }

        private bool IsPageBusy()
        {
            var unlock = CurrentView as UnlockViewModel;
            if (unlock != null && unlock.IsBusy) return true;
            var create = CurrentView as CreateNotebookViewModel;
            return create != null && create.IsBusy;
        }

        private bool IsLockDeferred
        {
            get { return _locking || _sessionSaving || Dialogs.IsShowingDialog || (_editor != null && _editor.IsSaving); }
        }

        private void ResetIdle()
        {
            _idle.RecordActivity();
            _lastActivityWall = WallClock();
        }

        private bool IsIdleExpired()
        {
            return _idle.IsExpired || WallElapsed() >= _idle.Timeout;
        }

        /// <summary>Wall-clock time since the last activity; a clock set backwards counts as zero (the monotonic policy
        /// still applies).</summary>
        private TimeSpan WallElapsed()
        {
            TimeSpan e = WallClock() - _lastActivityWall;
            return e < TimeSpan.Zero ? TimeSpan.Zero : e;
        }

        private void UpdateCountdown()
        {
            if (_editor == null)
            {
                LockCountdownText = "";
                return;
            }
            TimeSpan left = _idle.Remaining;
            TimeSpan wallLeft = _idle.Timeout - WallElapsed();
            if (wallLeft < left) left = wallLeft;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            long seconds = (long)Math.Ceiling(left.TotalSeconds);
            LockCountdownText = "Locks in " + (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":"
                + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private void ApplyIdleTimeout()
        {
            TimeSpan timeout = IdleTimeoutFromSettings();
            if (_idle.Timeout != timeout) _idle.Timeout = timeout;
        }

        /// <summary>Read from the shared settings instance; out-of-range values are clamped, so the lock is never disabled.</summary>
        private TimeSpan IdleTimeoutFromSettings()
        {
            int minutes = Settings == null ? 5 : Settings.IdleLockMinutes;
            return TimeSpan.FromMinutes(Math.Min(MaxIdleMinutes, Math.Max(MinIdleMinutes, minutes)));
        }

        /// <summary>Detaches the editor from its notebook. The notebook itself stays open (it may be held).</summary>
        private void LeaveEditor()
        {
            EditorViewModel editor = _editor;
            if (editor == null) return;
            Editor = null;
            editor.Detach();
        }
    }
}

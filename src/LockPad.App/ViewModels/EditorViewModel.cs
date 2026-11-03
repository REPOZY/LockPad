using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using LockPad.App.Infrastructure;
using LockPad.App.Services;
using LockPad.Core;

namespace LockPad.App.ViewModels
{
    /// <summary>
    /// Editor page: notes list, filter, the selected note's text, save and view settings. Copying happens in the view's
    /// TextBoxes, which TextBoxClipboardGuard routes through <see cref="IClipboardService.CopyProtected"/>.
    /// </summary>
    public sealed class EditorViewModel : ObservableObject
    {
        internal const string StatusSaved = "Saved";
        internal const string StatusUnsaved = "Unsaved changes";
        internal const string StatusReadOnly = "Read-only";
        internal const string StatusNoteDeleted = "Note deleted. Undo with Ctrl+Z in the notes list.";
        internal const string DeleteTitle = "Delete note";
        private const string DeleteUndoSentence = "\"? You can bring it back with Undo delete until you lock or close LockPad. ";
        private const string DeleteNeverSavedSentence = "This note has never been saved, so after that it cannot be recovered.";
        private const string DeleteSavedSentence = "After you save, its last saved version is kept in the .bak backup file "
            + "until you save again.";
        internal const string NewNoteTitle = "New note";
        internal const string NewNotePromptTitle = "New note";
        internal const string NewNotePromptLabel = "Name of the new note:";
        internal const string SaveFailedTitle = "Save failed";
        internal const string KeepsChangingMessage = "The file keeps changing on disk. Use Save As to keep your changes.";
        /// <summary>Appended to a failed Save's error: names the way out of a lasting failure.</summary>
        internal const string SaveAsHintSuffix = " Use File > Save As (Ctrl+Shift+S) to save a copy elsewhere.";
        internal const int MaxExternalChangeAsks = 5;
        internal const string PasswordChangedTitle = "Master password changed";
        internal const string PasswordChangedMessage = "Your notebook and its backup file (.bak) now open only with the "
            + "new password. Copies you made yourself (for example on another USB stick, or older .bak.kept or "
            + ".bak1-.bak3 files) still open with the old password.";
        internal const string PasswordNotChangedTitle = "Password not changed";
        internal const string PasswordChangeIncompleteTitle = "Password change incomplete";
        internal const string OldBackupTitle = "Old backup still uses the old password";
        internal const string ExternalChangeRetrySuffix = " Save or reopen the notebook, then try again.";

        private const double MinFontSize = 8;
        private const double MaxFontSize = 48;
        private const double DefaultFontSize = 14;

        private readonly NotebookStore _store;
        private readonly IDialogService _dialogs;
        private readonly SettingsStore _settingsStore;
        private readonly AppSettings _settings;
        private readonly Dictionary<string, NoteItemViewModel> _items = new Dictionary<string, NoteItemViewModel>(StringComparer.Ordinal);

        private readonly RelayCommand _newNote;
        private readonly RelayCommand _renameNote;
        private readonly RelayCommand _deleteNote;
        private readonly RelayCommand _undoDelete;
        private readonly RelayCommand _save;
        private readonly RelayCommand _saveAs;
        private readonly RelayCommand _changePassword;

        /// <summary>The wrong-current-password back-off of Change master password: one per editor, i.e. per notebook
        /// session (a lock discards the editor). Reopening the dialog keeps it; a successful change resets it. Test
        /// seam (clock and delay).</summary>
        internal PasswordBackOff ChangePasswordBackOff { get; set; } = new PasswordBackOff(new SystemClock());

        /// <summary>Session-only undo stack of deleted notes (last = most recent). Never saved anywhere; emptied on lock,
        /// close, obscure and teardown (<see cref="ClearUndo"/>), so the deleted text does not outlive the session.</summary>
        private readonly List<DeletedNote> _undo = new List<DeletedNote>();

        /// <summary>A transient status ("Note deleted ..."), shown instead of Saved / Unsaved changes until the next
        /// change of selection, text, title or dirty state.</summary>
        private string _statusMessage;

        private NoteItemViewModel _selectedNote;
        private string _filterText = "";
        private bool _attached;
        /// <summary>The Change master password dialog while it is open; null otherwise.</summary>
        private ChangePasswordViewModel _changePasswordDialog;
        private int _saveDepth;

        public EditorViewModel(Notebook notebook, NotebookStore store, IDialogService dialogs,
            SettingsStore settingsStore, AppSettings settings)
        {
            if (notebook == null) throw new ArgumentNullException(nameof(notebook));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (dialogs == null) throw new ArgumentNullException(nameof(dialogs));
            if (settingsStore == null) throw new ArgumentNullException(nameof(settingsStore));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            Notebook = notebook;
            _store = store;
            _dialogs = dialogs;
            _settingsStore = settingsStore;
            _settings = settings;

            VisibleNotes = new ObservableCollection<NoteItemViewModel>();

            _newNote = new RelayCommand(p => NewNote(), p => !IsReadOnly);
            _renameNote = new RelayCommand(p => RenameSelected(), p => !IsReadOnly && SelectedNote != null);
            _deleteNote = new RelayCommand(p => DeleteSelected(), p => !IsReadOnly && SelectedNote != null);
            _undoDelete = new RelayCommand(p => UndoDelete(), p => !IsReadOnly && _undo.Count > 0);
            _save = new RelayCommand(p => SaveNow(), p => !IsReadOnly);
            _saveAs = new RelayCommand(p => SaveAsNow(), p => !IsReadOnly);
            _changePassword = new RelayCommand(p => ChangePasswordNow(), p => !IsReadOnly && !IsObscured);
            ZoomInCommand = new RelayCommand(p => SetFontSize(FontSize + 1));
            ZoomOutCommand = new RelayCommand(p => SetFontSize(FontSize - 1));
            ZoomResetCommand = new RelayCommand(p => SetFontSize(DefaultFontSize));
            ToggleWordWrapCommand = new RelayCommand(p => WordWrap = !WordWrap);

            RefreshVisibleNotes();
            if (VisibleNotes.Count > 0) SelectedNote = VisibleNotes[0];

            Notebook.DirtyChanged += OnDirtyChanged;
            _attached = true;
            ShowBackupNotice();
        }

        public Notebook Notebook { get; }

        public ObservableCollection<NoteItemViewModel> VisibleNotes { get; }

        public bool IsReadOnly { get { return Notebook.IsReadOnly; } }

        /// <summary>The notebook's file name without its folder (the window title shows it). Changes on Save As.</summary>
        public string FileName
        {
            get
            {
                try
                {
                    return System.IO.Path.GetFileName(Notebook.FilePath) ?? "";
                }
                catch (ArgumentException)
                {
                    return "";
                }
            }
        }

        public string StatusText
        {
            get
            {
                if (Notebook.IsReadOnly) return StatusReadOnly;
                if (_statusMessage != null) return _statusMessage;
                return Notebook.IsDirty ? StatusUnsaved : StatusSaved;
            }
        }

        /// <summary>
        /// Selection is kept while the note is visible; a filter that hides it clears it. Without a selection the
        /// editor is empty and disabled.
        /// </summary>
        public NoteItemViewModel SelectedNote
        {
            get { return _selectedNote; }
            set
            {
                if (!SetProperty(ref _selectedNote, value)) return;
                ClearStatusMessage();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(EditorText));
                OnPropertyChanged(nameof(EmptyStateText));
                _renameNote.RaiseCanExecuteChanged();
                _deleteNote.RaiseCanExecuteChanged();
            }
        }

        public bool HasSelection { get { return _selectedNote != null; } }

        internal const string NoNotesText = "No notes yet. Click New (Ctrl+N) to add one.";
        internal const string ReadOnlyNoNotesText = "This notebook has no notes.";
        internal const string NoMatchText = "No notes match the filter.";
        internal const string SelectNoteText = "Select a note on the left.";

        /// <summary>The muted hint shown in the editor area while no note is selected; null while one is. A read-only
        /// notebook never suggests New; a filter that hides every note says so.</summary>
        public string EmptyStateText
        {
            get
            {
                if (_selectedNote != null) return null;
                if (Notebook.Notes.Count == 0) return IsReadOnly ? ReadOnlyNoNotesText : NoNotesText;
                return VisibleNotes.Count == 0 ? NoMatchText : SelectNoteText;
            }
        }

        /// <summary>
        /// True once the idle timeout expired while the lock had to wait (a dialog or a save was in progress). The view
        /// then hides the note text, the notes list and the find bar behind a "Locked" panel. One way: it is never
        /// reset, because an obscured editor always proceeds to a lock, which discards this view model. Apart from the
        /// undo-delete stack, nothing in the view model is cleared, so the open dialog's caller can still use it.
        /// </summary>
        public bool IsObscured { get; private set; }

        /// <summary>Sets <see cref="IsObscured"/> and empties the undo stack (a lock always follows; the deleted text
        /// must not survive a sleep whose queued lock is cut off). Called by the shell. The view is told first, so the
        /// note is hidden whatever happens next; the stack is emptied even if a view listener throws.</summary>
        internal void Obscure()
        {
            if (IsObscured) return;
            IsObscured = true;
            try
            {
                OnPropertyChanged(nameof(IsObscured));
            }
            finally
            {
                ClearUndo();
                AbandonChangePasswordDialog(); // an open Change master password dialog empties and refuses (X1)
                try
                {
                    _changePassword.RaiseCanExecuteChanged(); // File > Change master password is disabled while obscured
                }
                catch (Exception)
                {
                    // A command source's CanExecute refresh failed; the editor is obscured either way.
                }
            }
        }

        /// <summary>The selected note's text, or "" without a selection. Setting it without a selection, on a
        /// read-only notebook, or once the editor is obscured changes nothing: input that reaches the hidden text box
        /// may not be the user's, and the lock would save it unseen (re-attack 2026-10-22, High).</summary>
        public string EditorText
        {
            get
            {
                NoteItemViewModel selected = _selectedNote;
                if (selected == null) return "";
                Note note = Notebook.FindNote(selected.Id);
                return note == null ? "" : note.Text;
            }
            set
            {
                NoteItemViewModel selected = _selectedNote;
                if (selected == null || IsReadOnly || IsObscured) return;
                Note note = Notebook.FindNote(selected.Id);
                if (note == null) return;
                Notebook.UpdateNote(selected.Id, note.Title, value ?? "");
                ClearStatusMessage();
            }
        }

        /// <summary>Case-insensitive ordinal substring match on title or text. Applied when it changes and after
        /// add/rename/delete, not on every keystroke in the editor (a note never vanishes while it is being typed in).</summary>
        public string FilterText
        {
            get { return _filterText; }
            set
            {
                if (!SetProperty(ref _filterText, value ?? "")) return;
                RefreshVisibleNotes();
            }
        }

        public double FontSize { get { return _settings.FontSize; } }

        public bool WordWrap
        {
            get { return _settings.WordWrap; }
            set
            {
                if (_settings.WordWrap == value) return;
                _settings.WordWrap = value; // the shared instance is mutated in place, never replaced
                _settingsStore.Save(_settings);
                OnPropertyChanged(nameof(WordWrap));
            }
        }

        public ICommand NewNoteCommand { get { return _newNote; } }
        /// <summary>Raised after a new note was named and created, once it is selected. The view moves focus into the
        /// editor text box so the user can type at once.</summary>
        public event EventHandler NoteCreated;

        public ICommand RenameNoteCommand { get { return _renameNote; } }
        /// <summary>Raised after a confirmed delete, once the neighbouring note (if any) is selected. The view moves focus.</summary>
        public event EventHandler NoteDeleted;

        public ICommand DeleteNoteCommand { get { return _deleteNote; } }

        /// <summary>Restores the most recently deleted note (button, Ctrl+Z in the notes list). Enabled while something
        /// can be undone and the notebook is writable.</summary>
        public ICommand UndoDeleteCommand { get { return _undoDelete; } }

        /// <summary>Raised after an undo, once the restored note is selected. The view keeps focus in the notes list.</summary>
        public event EventHandler NoteRestored;

        /// <summary>Number of deletions that can be undone.</summary>
        internal int UndoCount { get { return _undo.Count; } }

        public ICommand SaveCommand { get { return _save; } }
        public ICommand SaveAsCommand { get { return _saveAs; } }

        /// <summary>File > Change master password. Disabled for a read-only notebook and while obscured; the menu item
        /// is also disabled while no editor is shown (locked).</summary>
        public ICommand ChangePasswordCommand { get { return _changePassword; } }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ZoomResetCommand { get; }
        public ICommand ToggleWordWrapCommand { get; }

        /// <summary>
        /// Shows a busy indicator for the duration of a save; the returned object ends it. Defaults to a wait cursor.
        /// Test seam: tests replace it so they need no WPF input system.
        /// </summary>
        internal Func<IDisposable> BeginBusy { get; set; } = WaitCursor.Begin;

        /// <summary>
        /// Saves to the notebook's file. Runs synchronously on the UI thread by design: saving concurrently with edits
        /// would race on the notebook. A save blocks for about 2-3 s (two Argon2 runs: write and verify). Handles an
        /// external change by asking Overwrite / Save As / Cancel; shows "Save failed" for any other failure. A user
        /// cancel returns the ExternalChangeDetected result without an error.
        /// </summary>
        public SaveResult SaveNow()
        {
            return SaveNow(true);
        }

        /// <summary>True while <see cref="SaveNow()"/> or <see cref="SaveAsNow"/> runs (including their dialogs). The shell
        /// never locks in the middle of a save.</summary>
        internal bool IsSaving
        {
            get { return _saveDepth > 0; }
        }

        /// <summary>
        /// As <see cref="SaveNow()"/>. With <paramref name="showErrors"/> false the final "Save failed" dialog is not shown,
        /// so a caller can show its own (e.g. on close). The external-change question is still asked, and the "keeps
        /// changing" error is still shown: it ends with an ExternalChangeDetected result that callers treat like Cancel.
        /// </summary>
        internal SaveResult SaveNow(bool showErrors)
        {
            _saveDepth++;
            try
            {
                return SaveNowCore(showErrors);
            }
            finally
            {
                _saveDepth--;
                ShowBackupNotice();
            }
        }

        /// <summary>Shows, in the status bar, the store's notice that a .bak this notebook did not write was kept under a
        /// new name (once; the next change replaces it). Also called for a notebook shown with a pending notice, e.g.
        /// after Create or a save made while locking.</summary>
        private void ShowBackupNotice()
        {
            string notice = Notebook.TakeBackupNotice();
            if (notice != null) SetStatusMessage(notice);
        }

        private SaveResult SaveNowCore(bool showErrors)
        {
            using (BeginBusy())
            {
                SaveResult result = _store.Save(Notebook, false);
                int asks = 0;
                while (result.Status == SaveStatus.ExternalChangeDetected)
                {
                    if (asks >= MaxExternalChangeAsks)
                    {
                        // The file changed again during every overwrite attempt: stop asking, keep the changes in memory.
                        _dialogs.ShowError(SaveFailedTitle, KeepsChangingMessage);
                        return result;
                    }
                    asks++;

                    ExternalChangeChoice choice = _dialogs.AskExternalChange(Notebook.FilePath);
                    if (choice == ExternalChangeChoice.Overwrite)
                    {
                        // Can itself report ExternalChangeDetected if the file changed again meanwhile: ask again.
                        result = _store.Save(Notebook, true);
                        continue;
                    }
                    if (choice == ExternalChangeChoice.SaveAs)
                    {
                        string path = _dialogs.PickSaveAsPath(Notebook.FilePath);
                        if (path == null) return result; // picker closed: same as Cancel
                        result = SaveAsTo(path);
                        break;
                    }
                    return result; // Cancel
                }

                if (showErrors && result.Status != SaveStatus.Saved)
                    _dialogs.ShowError(SaveFailedTitle, result.Message + SaveAsHintSuffix);
                return result;
            }
        }

        /// <summary>
        /// Saves to a new file chosen by the user; the notebook then lives at that path. Never replaces an existing
        /// file. A closed picker returns <see cref="SaveStatus.Cancelled"/> without an error. Synchronous, like
        /// <see cref="SaveNow"/>.
        /// </summary>
        public SaveResult SaveAsNow()
        {
            _saveDepth++;
            try
            {
                string path = _dialogs.PickSaveAsPath(Notebook.FilePath);
                if (path == null) return SaveResult.Fail(SaveStatus.Cancelled, "");
                using (BeginBusy())
                {
                    SaveResult result = SaveAsTo(path);
                    if (result.Status != SaveStatus.Saved) _dialogs.ShowError(SaveFailedTitle, result.Message);
                    return result;
                }
            }
            finally
            {
                _saveDepth--;
                ShowBackupNotice();
            }
        }

        /// <summary>
        /// Changes the master password (user decisions 2026-10-19): shows the dialog, which requires the current
        /// password and checks it in memory, then, after the dialog closed, runs <see cref="NotebookStore.ChangeMasterKey"/>
        /// like Ctrl+S: synchronously, inside the busy scope and counted as a save (<see cref="IsSaving"/>), so the
        /// shell never locks mid-change (an idle expiry obscures, and the lock follows). Shows the outcome. Returns the
        /// store's result, or null when nothing ran (cancelled, read-only, obscured, or no longer the shown editor).
        /// <para>Fix round 2: only the write phase counts as saving (X2). While the dialog is open the shell treats it
        /// like any other owned dialog (IsShowingDialog): an idle expiry obscures, and a session end saves unsaved notes
        /// quietly. When the editor is obscured or left while the dialog is open (X1), the dialog is abandoned (boxes
        /// cleared, every attempt refused) and, once it returns, its credentials are disposed with no comparison, no
        /// store call and no message.</para>
        /// </summary>
        public ChangeKeyResult ChangePasswordNow()
        {
            if (IsReadOnly || IsObscured || !_attached) return null;
            using (var vm = new ChangePasswordViewModel(Notebook, _dialogs, ChangePasswordBackOff))
            {
                bool confirmed;
                _changePasswordDialog = vm;
                try
                {
                    confirmed = _dialogs.ShowChangePassword(vm);
                }
                finally
                {
                    _changePasswordDialog = null;
                }
                // The screen locked (or the shell left this editor) while the dialog was open: whoever confirmed it may
                // not be the user. vm.Dispose disposes any credentials it holds.
                if (!confirmed || IsObscured || !_attached || vm.IsAbandoned || vm.Current == null || vm.Replacement == null)
                    return null;

                ChangeKeyResult result;
                _saveDepth++;
                try
                {
                    using (BeginBusy())
                        result = _store.ChangeMasterKey(Notebook, vm.Current, vm.Replacement);
                    // The notebook took the new credentials when its file was written with them; that is a successful
                    // change, which also ends the wrong-password back-off.
                    if (result.Status == ChangeKeyStatus.Changed || result.Status == ChangeKeyStatus.ChangedButBackupNotRelocked
                        || result.Status == ChangeKeyStatus.ChangedButReplaceIncomplete)
                    {
                        vm.ReleaseReplacement();
                        ChangePasswordBackOff.Reset();
                    }
                }
                finally
                {
                    _saveDepth--;
                    try
                    {
                        ShowBackupNotice();
                    }
                    catch (Exception)
                    {
                        // A failing status listener must not hide the outcome below (final re-attack 2026-10-22, L4).
                    }
                }
                // Re-attack 2026-10-22: the write phase is over, so the outcome box is an ordinary dialog (a session end
                // while it is open saves unsaved notes quietly), and the dialog's credentials, including a new password
                // that was never applied, are released before it is shown.
                vm.Dispose();
                ShowChangePasswordOutcome(result);
                return result;
            }
        }

        /// <summary>Abandons an open Change master password dialog (X1). Never throws: it runs inside obscure and
        /// teardown.</summary>
        private void AbandonChangePasswordDialog()
        {
            ChangePasswordViewModel dialog = _changePasswordDialog;
            if (dialog == null) return;
            try
            {
                dialog.Abandon();
            }
            catch (Exception)
            {
                // Abandon itself never throws; ChangePasswordNow re-checks IsObscured and the attachment anyway.
            }
        }

        private void ShowChangePasswordOutcome(ChangeKeyResult result)
        {
            switch (result.Status)
            {
                case ChangeKeyStatus.Changed:
                    _dialogs.ShowInfo(PasswordChangedTitle, PasswordChangedMessage);
                    break;
                case ChangeKeyStatus.ChangedButBackupNotRelocked:
                    // True since fix round 2 (X3): the notebook needs a write, so the next save (also on lock or close)
                    // re-locks the .bak, proving it with the old key the notebook keeps until then.
                    _dialogs.ShowWarning(PasswordChangedTitle, "The password was changed, but the backup file could not "
                        + "be re-locked: " + Reason(result.Message) + ". It still opens with the old password; LockPad "
                        + "re-locks it the next time it saves (also when it locks or closes).");
                    break;
                case ChangeKeyStatus.ChangedButReplaceIncomplete:
                    // The second swap stopped half-way (X4); the reason names where the new copy is.
                    _dialogs.ShowError(PasswordChangeIncompleteTitle, "The new password is set, but replacing the file did "
                        + "not finish: " + Reason(result.Message) + ". All files now open only with the NEW password. The "
                        + "notebook file is missing, so Save (Ctrl+S) cannot finish this: use File > Save As (Ctrl+Shift+S) "
                        + "to save under a new name before you close LockPad.");
                    break;
                case ChangeKeyStatus.ExternalChangeDetected:
                    _dialogs.ShowError(PasswordNotChangedTitle, result.Message + ExternalChangeRetrySuffix);
                    break;
                case ChangeKeyStatus.FailedWhileReplacing:
                    // The file swap stopped half-way: a kept copy already uses the new password (fix round 1, F2).
                    _dialogs.ShowError(PasswordChangeIncompleteTitle, "Changing the password failed while replacing the "
                        + "file: " + Reason(result.Message) + ". Some files may already open only with the new password; "
                        + "keep both passwords until you have checked which files open with which.");
                    break;
                default:
                    _dialogs.ShowError(PasswordNotChangedTitle, "Changing the password failed: " + Reason(result.Message)
                        + ". Nothing was changed; the old password still works.");
                    break;
            }
        }

        /// <summary>A store reason placed before ". ": without its own final period, so none is doubled.</summary>
        private static string Reason(string message)
        {
            string reason = (message ?? "").TrimEnd();
            while (reason.EndsWith(".", StringComparison.Ordinal)) reason = reason.Substring(0, reason.Length - 1);
            return reason;
        }

        /// <summary>Stops listening to the notebook and empties the undo stack. Called by the shell when it leaves the
        /// editor (lock, hold, close, another notebook).</summary>
        internal void Detach()
        {
            ClearUndo();
            AbandonChangePasswordDialog();
            if (!_attached) return;
            _attached = false;
            Notebook.DirtyChanged -= OnDirtyChanged;
        }

        /// <summary>
        /// Called by the shell on lock and close: detaches, then empties what the view shows (selection, editor text,
        /// filter, notes list) so bound controls drop the note text. The notebook itself is never changed.
        /// </summary>
        internal void ClearForLock()
        {
            Detach();
            SelectedNote = null; // EditorText is now "" and PropertyChanged tells the bound TextBox
            if (_filterText.Length > 0)
            {
                _filterText = "";
                OnPropertyChanged(nameof(FilterText));
            }
            VisibleNotes.Clear();
            _items.Clear();
        }

        /// <summary>
        /// Index of the next case-insensitive match of <paramref name="query"/> at or after <paramref name="from"/>,
        /// wrapping around to the start; -1 when there is none or the query is empty.
        /// </summary>
        internal static int FindNext(string text, string query, int from)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return -1;
            int start = Math.Max(0, Math.Min(from, text.Length));
            int index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0 && start > 0) index = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
            return index;
        }

        private SaveResult SaveAsTo(string path)
        {
            SaveResult result = _store.SaveAs(Notebook, path);
            if (result.Status == SaveStatus.Saved)
            {
                // The notebook now lives at the new path: the title shows its name. A throwing listener must never turn
                // a successful save into an error.
                try
                {
                    OnPropertyChanged(nameof(FileName));
                }
                catch (Exception)
                {
                }
                // Only the notebook path is remembered (as on unlock).
                _settings.LastNotebookPath = Notebook.FilePath;
                _settingsStore.Save(_settings);
                // A pending re-lock of the old .bak was cleared by moving the notebook (fix round 3, Y1): nothing at
                // the old location is touched, so the user is told which file still opens with the old password.
                if (result.UnrelockedBackupPath != null)
                    _dialogs.ShowWarning(OldBackupTitle, "The backup file \"" + result.UnrelockedBackupPath + "\" was not "
                        + "re-locked and still opens with the OLD password. Delete it if you no longer need it.");
            }
            return result;
        }

        private void OnDirtyChanged(object sender, EventArgs e)
        {
            // Raised from inside NotebookStore.Save after a committed write: a throwing listener must never turn a
            // successful save into an error.
            try
            {
                _statusMessage = null; // saved, or newly dirty: the normal status takes over
                OnPropertyChanged(nameof(StatusText));
            }
            catch (Exception)
            {
            }
        }

        private void SetStatusMessage(string message)
        {
            _statusMessage = message;
            OnPropertyChanged(nameof(StatusText));
        }

        private void ClearStatusMessage()
        {
            if (_statusMessage == null) return;
            _statusMessage = null;
            OnPropertyChanged(nameof(StatusText));
        }

        /// <summary>
        /// Drops every undo entry (and with it the references to the deleted notes' text). Never throws: it runs inside
        /// obscure and teardown (Detach, ClearForLock), which must always complete, so a failing CanExecuteChanged
        /// listener is ignored, as in the shell's RunAll teardown. Also called by the shell when a session end is allowed.
        /// </summary>
        internal void ClearUndo()
        {
            if (_undo.Count == 0) return;
            _undo.Clear();
            _undo.TrimExcess();
            try
            {
                _undoDelete.RaiseCanExecuteChanged();
            }
            catch (Exception)
            {
                // A command source's CanExecute refresh failed; the stack is already empty and teardown continues.
            }
        }

        /// <summary>
        /// Asks for the name first (prefilled with "New note"); Cancel, Esc or closing the prompt creates nothing.
        /// The title follows the normal title rules (Notebook.AddNote sanitizes it; an empty title shows "(untitled)").
        /// </summary>
        private void NewNote()
        {
            if (IsReadOnly) return;
            string title = _dialogs.Prompt(NewNotePromptTitle, NewNotePromptLabel, NewNoteTitle);
            // Answered after the editor was obscured: it may not be the user who answered (re-attack 2026-10-22).
            if (title == null || IsReadOnly || IsObscured || !_attached) return;
            Note note = Notebook.AddNote(title);
            if (!Matches(note)) FilterText = ""; // the new note must be visible to be selected
            RefreshVisibleNotes();
            NoteItemViewModel item;
            if (!_items.TryGetValue(note.Id, out item)) return;
            SelectedNote = item;
            EventHandler handler = NoteCreated;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RenameSelected()
        {
            NoteItemViewModel selected = _selectedNote;
            if (selected == null || IsReadOnly) return;
            Note note = Notebook.FindNote(selected.Id);
            if (note == null) return;
            string title = _dialogs.Prompt("Rename note", "Title:", note.Title);
            // Answered after the editor was obscured (the lock became due while the prompt was open): it may not be the
            // user who answered, as for Change master password (re-attack 2026-10-22).
            if (title == null || IsObscured || !_attached) return;
            Notebook.UpdateNote(note.Id, title, note.Text);
            Note updated = Notebook.FindNote(note.Id);
            selected.SetTitle(updated == null ? title : updated.Title);
            ClearStatusMessage();
            RefreshVisibleNotes();
        }

        /// <summary>
        /// Confirms, removes the note and keeps it on the undo stack. The message says whether the note is in the last
        /// saved file (then, after the next save, that version is kept in the .bak file until the save after it) or was
        /// never saved.
        /// </summary>
        private void DeleteSelected()
        {
            NoteItemViewModel selected = _selectedNote;
            if (selected == null || IsReadOnly) return;
            string message = "Delete \"" + selected.DisplayTitle + DeleteUndoSentence
                + (Notebook.IsInSavedFile(selected.Id) ? DeleteSavedSentence : DeleteNeverSavedSentence);
            if (!_dialogs.Confirm(DeleteTitle, message)) return;
            // Confirmed after the editor was obscured: whoever answered may not be the user (re-attack 2026-10-22).
            if (IsObscured || !_attached) return;
            Note note = Notebook.FindNote(selected.Id);
            if (note == null) return;
            int notebookIndex = IndexInNotebook(note.Id);
            int index = VisibleNotes.IndexOf(selected);
            SelectedNote = null;
            Notebook.RemoveNote(selected.Id);
            _items.Remove(selected.Id);
            // Not while obscured (e.g. the PC went to sleep while the confirmation was open): the lock follows, and the
            // deleted text is not kept for it.
            if (!IsObscured)
            {
                _undo.Add(new DeletedNote(note, notebookIndex));
                _undoDelete.RaiseCanExecuteChanged();
            }
            RefreshVisibleNotes();
            // Select the neighbour: the next visible note (now at the same index), else the previous one.
            if (VisibleNotes.Count > 0)
                SelectedNote = VisibleNotes[Math.Max(0, Math.Min(index, VisibleNotes.Count - 1))];
            if (!IsObscured) SetStatusMessage(StatusNoteDeleted);
            EventHandler handler = NoteDeleted;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Restores the most recent deletion at its old position in the notebook (or the end), selects it and leaves
        /// the notebook dirty. The note keeps its id, also after a save in between (see Notebook.RestoreNote).
        /// </summary>
        private void UndoDelete()
        {
            if (IsReadOnly || IsObscured || _undo.Count == 0) return;
            DeletedNote entry = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _undoDelete.RaiseCanExecuteChanged();
            if (Notebook.FindNote(entry.Note.Id) != null) return; // cannot happen: ids are unique
            Notebook.RestoreNote(entry.Note, entry.Index);
            if (!Matches(entry.Note)) FilterText = ""; // the restored note must be visible to be selected
            RefreshVisibleNotes();
            NoteItemViewModel item;
            if (!_items.TryGetValue(entry.Note.Id, out item)) return;
            SelectedNote = item;
            ClearStatusMessage();
            EventHandler handler = NoteRestored;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private int IndexInNotebook(string id)
        {
            IReadOnlyList<Note> notes = Notebook.Notes;
            for (int i = 0; i < notes.Count; i++)
                if (notes[i].Id == id) return i;
            return notes.Count;
        }

        private void SetFontSize(double size)
        {
            double clamped = Math.Max(MinFontSize, Math.Min(MaxFontSize, size));
            if (_settings.FontSize == clamped) return;
            _settings.FontSize = clamped; // the shared instance is mutated in place, never replaced
            _settingsStore.Save(_settings);
            OnPropertyChanged(nameof(FontSize));
        }

        private bool Matches(Note note)
        {
            string filter = _filterText;
            if (string.IsNullOrEmpty(filter)) return true;
            return note.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || note.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Syncs VisibleNotes with the notebook in place (no Clear), so a bound list keeps its selection.</summary>
        private void RefreshVisibleNotes()
        {
            var wanted = new List<NoteItemViewModel>();
            foreach (Note note in Notebook.Notes)
            {
                NoteItemViewModel item;
                if (!_items.TryGetValue(note.Id, out item))
                {
                    item = new NoteItemViewModel(note.Id, note.Title);
                    _items.Add(note.Id, item);
                }
                if (Matches(note)) wanted.Add(item);
            }

            var wantedSet = new HashSet<NoteItemViewModel>(wanted);
            for (int i = VisibleNotes.Count - 1; i >= 0; i--)
                if (!wantedSet.Contains(VisibleNotes[i])) VisibleNotes.RemoveAt(i);

            for (int i = 0; i < wanted.Count; i++)
            {
                if (i < VisibleNotes.Count && ReferenceEquals(VisibleNotes[i], wanted[i])) continue;
                int existing = VisibleNotes.IndexOf(wanted[i]);
                if (existing >= 0) VisibleNotes.Move(existing, i);
                else VisibleNotes.Insert(i, wanted[i]);
            }

            if (_selectedNote != null && !wantedSet.Contains(_selectedNote)) SelectedNote = null;
            OnPropertyChanged(nameof(EmptyStateText)); // a note was added or removed, or the filter changed
        }

        /// <summary>An undo entry: the deleted note itself (id, title and text, the same immutable Note object the
        /// notebook held) and its index in the notebook when it was deleted.</summary>
        private sealed class DeletedNote
        {
            public DeletedNote(Note note, int index)
            {
                Note = note;
                Index = index;
            }

            public Note Note { get; }
            public int Index { get; }
        }

        /// <summary>Wait cursor for the duration of a synchronous save. UI thread only.</summary>
        private sealed class WaitCursor : IDisposable
        {
            private readonly Cursor _previous;

            private WaitCursor()
            {
                _previous = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
            }

            public static IDisposable Begin()
            {
                return new WaitCursor();
            }

            public void Dispose()
            {
                Mouse.OverrideCursor = _previous;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using KeePassLib;

namespace LockPad.Core
{
    /// <summary>Why a notebook was opened read-only.</summary>
    public enum ReadOnlyReason
    {
        /// <summary>Writable.</summary>
        None,
        /// <summary>The file has no LockPad marker: it may be the user's password database.</summary>
        NotANotebook,
        /// <summary>The file name is a backup copy (<c>name.bak</c>, which the next save of the main notebook replaces,
        /// or an older version's <c>name.bakN</c>): edits made in it would be lost.</summary>
        BackupCopy,
        /// <summary>The notebook path is a symbolic link; saving would replace the link, not the file it points to
        /// (File.Replace fails on it).</summary>
        SymbolicLink,
    }

    public sealed class Notebook : IDisposable
    {
        private readonly List<Note> _notes;
        private readonly HashSet<string> _removedIds = new HashSet<string>();
        private readonly HashSet<string> _savedIds = new HashSet<string>(StringComparer.Ordinal);
        private bool _isDirty;

        internal Notebook(string filePath, bool isReadOnly, IEnumerable<Note> notes)
            : this(filePath, isReadOnly ? ReadOnlyReason.NotANotebook : ReadOnlyReason.None, notes)
        {
        }

        internal Notebook(string filePath, ReadOnlyReason readOnlyReason, IEnumerable<Note> notes)
        {
            FilePath = filePath;
            ReadOnlyReason = readOnlyReason;
            _notes = notes == null ? new List<Note>() : new List<Note>(notes);
            RememberSavedIds(); // the notes passed in are the ones loaded from the file
        }

        public event EventHandler DirtyChanged;

        public string FilePath { get; internal set; }
        public bool IsReadOnly { get { return ReadOnlyReason != ReadOnlyReason.None; } }
        public ReadOnlyReason ReadOnlyReason { get; }
        /// <summary>True when the notes changed since the last save, or when the file needs a write even with unchanged
        /// notes (<see cref="NeedsWrite"/>). Lock, close and session end save a dirty notebook (and hold it when that
        /// fails), so a pending re-lock of the .bak is never dropped quietly.</summary>
        public bool IsDirty { get { return _isDirty || _needsWrite; } }
        public IReadOnlyList<Note> Notes { get { return _notes.AsReadOnly(); } }

        internal PwDatabase Database { get; set; }
        internal Credentials Credentials { get; set; }

        private bool _needsWrite;
        private bool _ownsPreviousCredentials;

        /// <summary>
        /// Set by NotebookStore.ChangeMasterKey (fix round 2, X3/X4) when the change committed the notebook file with the
        /// new key but the .bak was not re-locked (it still uses <see cref="PreviousCredentials"/>), or when the notebook
        /// file is missing after a half-finished swap. The next Save writes even with identical notes (and so re-locks
        /// the .bak); a successful Save or Save As clears it (<see cref="CompleteRelock"/>).
        /// </summary>
        internal bool NeedsWrite { get { return _needsWrite; } }

        /// <summary>The credentials this notebook had before its last key change, while the .bak may still use them.
        /// Used ONLY to prove that the .bak is this notebook's own (NotebookStore.IsOwnBackup tries the current key,
        /// then this one); never for writing. Released by <see cref="CompleteRelock"/> and by Dispose.</summary>
        internal Credentials PreviousCredentials { get; private set; }

        /// <summary>Whether this notebook disposes <see cref="PreviousCredentials"/> when it releases them.</summary>
        internal bool OwnsPreviousCredentials { get { return _ownsPreviousCredentials; } }

        /// <summary>Replaces <see cref="PreviousCredentials"/> without disposing the one it replaces (the caller
        /// decides). <paramref name="owned"/>: whether this notebook disposes it when it is released.</summary>
        internal void SetPreviousCredentials(Credentials credentials, bool owned)
        {
            PreviousCredentials = credentials;
            _ownsPreviousCredentials = credentials != null && owned;
        }

        /// <summary>Releases <see cref="PreviousCredentials"/> (disposing it when owned and not also the current
        /// credentials).</summary>
        internal void DropPreviousCredentials()
        {
            Credentials previous = PreviousCredentials;
            bool owned = _ownsPreviousCredentials;
            PreviousCredentials = null;
            _ownsPreviousCredentials = false;
            if (previous != null && owned && !ReferenceEquals(previous, Credentials)) previous.Dispose();
        }

        /// <summary>The file needs a write even with unchanged notes (see <see cref="NeedsWrite"/>).</summary>
        internal void MarkNeedsWrite()
        {
            bool wasDirty = IsDirty;
            _needsWrite = true;
            if (!wasDirty) RaiseDirtyChanged();
        }

        /// <summary>After a write that replaced (or proved and replaced) the .bak, or moved the notebook to a new
        /// file: nothing uses the previous key any more and no write is pending for it.</summary>
        internal void CompleteRelock()
        {
            DropPreviousCredentials();
            bool wasDirty = IsDirty;
            _needsWrite = false;
            if (wasDirty && !IsDirty) RaiseDirtyChanged();
        }

        /// <summary>The full path of the key file this notebook was opened (or created) with; null without one. A
        /// password change keeps it.</summary>
        public string KeyFilePath
        {
            get
            {
                Credentials credentials = Credentials;
                return credentials == null ? null : credentials.KeyFilePath;
            }
        }
        internal FileFingerprint LoadedFingerprint { get; set; }

        /// <summary>True while <see cref="Database"/>'s notes are exactly the file's (after a load or a successful
        /// save). NotebookStore clears it when it starts applying changes to the database, so a failed save, which leaves
        /// those changes applied in memory only, is never mistaken for "nothing to write".</summary>
        internal bool DatabaseMatchesFile { get; set; }

        /// <summary>The <c>.bak</c> this notebook object wrote, or proved to be its own (it opened with these credentials
        /// and carries the marker), with the content it had then; null when none. NotebookStore replaces a <c>.bak</c>
        /// without a new key derivation only while it is still exactly this file.</summary>
        internal string OwnBackupPath { get; set; }
        internal FileFingerprint OwnBackupFingerprint { get; set; }

        /// <summary>The LockPad.NotebookId of the file as loaded or last saved; null for a file from an older build
        /// (it gets one on its next save). Compared with a .bak's id in NotebookStore's ownership check.</summary>
        internal string FileNotebookId { get; set; }

        private readonly List<string> _backupNotices = new List<string>();

        /// <summary>Messages about <c>.bak</c> files that were not this notebook's own and were kept under a new name.
        /// Returns them (one sentence each) and forgets them, or null when there are none.</summary>
        public string TakeBackupNotice()
        {
            if (_backupNotices.Count == 0) return null;
            string notice = string.Join(" ", _backupNotices);
            _backupNotices.Clear();
            return notice;
        }

        internal void AddBackupNotice(string notice)
        {
            _backupNotices.Add(notice);
        }

        /// <summary>Ids removed since the last MarkClean. Includes ids of notes added and removed in the same
        /// session (never saved); the store simply finds no entry for those.</summary>
        internal IReadOnlyCollection<string> RemovedIds { get { return _removedIds; } }

        public Note AddNote(string title)
        {
            RequireWritable();
            var note = Note.Create(new PwUuid(true).ToHexString(), XmlSafeText.Sanitize(title), "", DateTime.UtcNow);
            _notes.Add(note);
            SetDirty();
            return note;
        }

        public void UpdateNote(string id, string title, string text)
        {
            RequireWritable();
            int i = IndexOf(id);
            if (i < 0) throw new KeyNotFoundException("Note not found: " + id);
            // Normalize to exactly what KeePassLib will store, so verification and dirty tracking compare like with like.
            title = XmlSafeText.Sanitize(title);
            text = XmlSafeText.NormalizeText(text);
            Note old = _notes[i];
            if (old.Title == title && old.Text == text) return;
            _notes[i] = Note.Create(old.Id, title, text, DateTime.UtcNow);
            SetDirty();
        }

        public void RemoveNote(string id)
        {
            RequireWritable();
            int i = IndexOf(id);
            if (i < 0) throw new KeyNotFoundException("Note not found: " + id);
            _notes.RemoveAt(i);
            _removedIds.Add(id);
            SetDirty();
        }

        /// <summary>
        /// Undo delete: puts a removed note back at <paramref name="index"/> (clamped to the list; past the end means
        /// the end), with its id, title and text. The id is kept even when the deletion was already saved: the store
        /// then writes a new entry with that id and drops KeePassLib's deletion record for it (see
        /// NotebookStore.ApplyChanges), so the note is never treated as deleted.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read-only, or a note with this id is present.</exception>
        public void RestoreNote(Note note, int index)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            RequireWritable();
            if (IndexOf(note.Id) >= 0) throw new InvalidOperationException("The note is already present: " + note.Id);
            _notes.Insert(Math.Max(0, Math.Min(index, _notes.Count)), note);
            _removedIds.Remove(note.Id);
            SetDirty();
        }

        public Note FindNote(string id)
        {
            int i = IndexOf(id);
            return i < 0 ? null : _notes[i];
        }

        /// <summary>True when a note with this id was in the file at the last load or successful save (so the version
        /// from that save is in the backups after the next save). False for a note created since then.</summary>
        public bool IsInSavedFile(string id)
        {
            return id != null && _savedIds.Contains(id);
        }

        public bool CredentialsMatch(Credentials other)
        {
            return Credentials != null && other != null && Credentials.Matches(other);
        }

        /// <summary>Called after a successful create or save: the notes in memory are now exactly the saved file's. A
        /// pending <see cref="NeedsWrite"/> is not cleared here (see <see cref="CompleteRelock"/>).</summary>
        internal void MarkClean()
        {
            _removedIds.Clear();
            RememberSavedIds();
            DatabaseMatchesFile = true;
            bool wasDirty = IsDirty;
            _isDirty = false;
            if (wasDirty && !IsDirty) RaiseDirtyChanged();
        }

        /// <summary>True once <see cref="Dispose"/> ran: the notebook can never be saved or shown again.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Closes the database and releases the credentials. Also drops every note reference (the list, and so
        /// every view of it handed out by <see cref="Notes"/>, becomes empty) and the saved and removed ids, so no note
        /// text stays reachable through a disposed notebook (fix round 1, X9). The shell empties and detaches the editor
        /// before it disposes; a disposed notebook can never be saved (NotebookStore checks the database first).</summary>
        public void Dispose()
        {
            IsDisposed = true;
            var db = Database;
            Database = null;
            _notes.Clear();
            _savedIds.Clear();
            _removedIds.Clear();
            if (db != null) db.Close();
            DropPreviousCredentials();
            var cred = Credentials;
            Credentials = null;
            if (cred != null) cred.Dispose();
        }

        private void RememberSavedIds()
        {
            _savedIds.Clear();
            foreach (Note n in _notes) _savedIds.Add(n.Id);
        }

        private int IndexOf(string id)
        {
            for (int i = 0; i < _notes.Count; i++)
                if (_notes[i].Id == id) return i;
            return -1;
        }

        private void RequireWritable()
        {
            if (IsReadOnly) throw new InvalidOperationException("This notebook is read-only.");
        }

        private void SetDirty()
        {
            if (_isDirty) return;
            bool wasDirty = IsDirty;
            _isDirty = true;
            if (!wasDirty) RaiseDirtyChanged();
        }

        private void RaiseDirtyChanged()
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

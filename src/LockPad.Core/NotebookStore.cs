using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Resources;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace LockPad.Core
{
    /// <summary>Creates, opens and atomically writes LockPad notebooks (KDBX files).</summary>
    public sealed class NotebookStore
    {
        public const string MarkerKey = "LockPad.Notebook";
        public const string NotesGroupName = "Notes";
        /// <summary>Database CustomData key of the random per-notebook id (32 hex characters, written by Create, added on
        /// the next save of an older notebook). Not secret, never shown. Tells two same-password notebooks apart in the
        /// .bak ownership check.</summary>
        internal const string NotebookIdKey = "LockPad.NotebookId";
        /// <summary>Create and Save As refuse backup-copy names (<c>anything.bak</c>, <c>anything.bakN</c>): the next
        /// save of the main notebook replaces its .bak, so a notebook saved under that name would be lost. Older
        /// LockPad versions rotated .bak1 to .bak3, which stay reserved.</summary>
        public const string BackupNameReservedMessage = "This name is reserved for backup copies. Choose another name.";
        /// <summary>
        /// The longest notebook path Create and Save As accept (full check 2026-10-22, L6). Without long-path support a
        /// Windows path has at most 259 characters, and a save also needs the longest derived name,
        /// <c>notebook.bak.kept-yyyyMMdd-HHmmss-NN</c> (28 more). A longer notebook could be opened but never saved
        /// in place.
        /// </summary>
        public const int MaxNotebookPathLength = 259 - 28;
        public const string PathTooLongMessage =
            "This path is too long for LockPad's backup and temporary files. Choose a shorter folder or file name.";

        private const string MarkerValue = "1";
        private const string FormatErrorMessage = "This file is not a valid notebook or KeePass database.";
        private const string VerifyFailedMessage = "The saved copy did not match. Your file was not changed.";
        private const string KeyFileIsDatabaseMessage = "This key file is a KeePass database. Choose a different key file.";
        private const string ReadOnlyMessage = "This notebook is read-only.";
        private const string FileExistsMessage = "File already exists.";
        private const string FileNotFoundMessage = "File not found.";
        private const string ExternalChangeMessage = "The file was changed by another program since it was opened.";
        private const string NotOverwritableMessage = "The file on disk is no longer a LockPad notebook that opens "
            + "with this password, so it was not overwritten. Use Save As to keep your changes.";
        private const string DifferentNotebookMessage = "The file on disk is now a different LockPad notebook, so it "
            + "was not overwritten. Use Save As to keep your changes.";
        private const string NotCheckableMessage = "LockPad could not safely check the file on disk (it uses other or "
            + "heavier key settings than this notebook, or it is unusually large), so it was not overwritten. Use Save "
            + "As to keep your changes.";
        /// <summary>Files larger than this are never test-opened for an ownership check (.bak, Overwrite): the check
        /// reads the whole file into memory, and a notebook of text notes is far smaller.</summary>
        private const long MaxCheckedFileBytes = 512L * 1024 * 1024;
        /// <summary>Test seam, invoked as (stage, tempPath) with stage "after-temp-write" and "before-replace".</summary>
        internal Action<string, string> TestHook { get; set; }
        /// <summary>Test seam for the swap, invoked as (temp, target, backup); null uses File.Replace.</summary>
        internal Action<string, string, string> ReplaceFile { get; set; }
        /// <summary>Test seam: the local time used in the name of a kept-aside <c>.bak</c>.</summary>
        internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

        /// <summary>The single backup kept next to a notebook: the version before its last save.</summary>
        public static string BackupPath(string notebookPath)
        {
            return notebookPath + ".bak";
        }

        public NotebookStore()
        {
            KeePassLibPolicy.Apply();
        }

        public Notebook Create(string path, Credentials credentials, KdfParameters kdf)
        {
            string full = LocalPath.RequireLocalFile(path);
            IOConnectionInfo ioc = LocalIoc(full);
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (kdf == null) throw new ArgumentNullException(nameof(kdf));
            CompositeKey key = credentials.CompositeKey; // throws if the credentials were disposed

            if (IsBackupFileName(full))
                throw new IOException(BackupNameReservedMessage);
            if (full.Length > MaxNotebookPathLength)
                throw new IOException(PathTooLongMessage);

            if (credentials.KeyFilePath != null)
                ValidateKeyFileForCreate(credentials.KeyFilePath, full);

            if (File.Exists(full) || Directory.Exists(full))
                throw new IOException(FileExistsMessage);

            var db = new PwDatabase();
            Notebook nb = null;
            try
            {
                db.New(ioc, key);
                db.DataCipherUuid = StandardAesEngine.AesUuid;
                db.KdfParameters = kdf;
                db.MemoryProtection.ProtectNotes = true;
                db.HistoryMaxItems = 10;
                db.RecycleBinEnabled = false;
                db.UseFileTransactions = false;
                db.CustomData.Set(MarkerKey, MarkerValue);
                db.CustomData.Set(NotebookIdKey, NewNotebookId());
                db.RootGroup.AddGroup(new PwGroup(true, true, NotesGroupName, PwIcon.Note), true);

                nb = new Notebook(full, false, new Note[0]);
                nb.Database = db;
                nb.Credentials = credentials;

                // requireNew: a file that appeared during the KDF runs is never replaced.
                SaveResult result = WriteVerifiedAndCommitCore(nb, full, true, null);
                if (result.Status != SaveStatus.Saved)
                    throw new IOException(result.Message);

                nb.MarkClean();
                return nb;
            }
            catch
            {
                // Detach so the caller keeps ownership of its credentials on failure.
                if (nb != null)
                {
                    nb.Database = null;
                    nb.Credentials = null;
                }
                db.Close();
                throw;
            }
        }

        public Notebook Open(string path, Credentials credentials)
        {
            string full = LocalPath.RequireLocalFile(path);
            IOConnectionInfo ioc = LocalIoc(full);
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            CompositeKey key = credentials.CompositeKey; // throws if the credentials were disposed

            if (!File.Exists(full))
                throw new FileNotFoundException("File not found.", full);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                // Compute exceptions (e.g. access denied) surface as-is; only "missing" is mapped.
                FileFingerprint before = FileFingerprint.Compute(full);
                if (!before.Exists)
                    throw new FileNotFoundException("File not found.", full);

                PwDatabase db = LoadDatabase(ioc, key);
                Notebook nb = null;
                try
                {
                    FileFingerprint after = FileFingerprint.Compute(full);
                    if (!after.SameContentAs(before))
                    {
                        db.Close();
                        continue;
                    }

                    bool marked = db.CustomData.Get(MarkerKey) == MarkerValue;
                    var notes = new List<Note>();
                    if (marked)
                    {
                        // KeePassLib resets MemoryProtection to defaults on every load; re-apply ours.
                        db.MemoryProtection.ProtectNotes = true;
                        PwGroup group = FindNotesGroup(db);
                        if (group == null)
                        {
                            // Marked but the group is gone (edited elsewhere): recreate it in memory only.
                            group = new PwGroup(true, true, NotesGroupName, PwIcon.Note);
                            db.RootGroup.AddGroup(group, true);
                        }
                        foreach (PwEntry e in group.Entries) notes.Add(ToNote(e));
                    }
                    else
                    {
                        // Foreign KeePass database: titles and notes only, never passwords or other fields.
                        foreach (PwEntry e in db.RootGroup.GetEntries(true)) notes.Add(ToNote(e));
                    }

                    nb = new Notebook(full, ReadOnlyReasonFor(full, marked), notes);
                    nb.Database = db;
                    nb.Credentials = credentials;
                    nb.LoadedFingerprint = after;
                    nb.DatabaseMatchesFile = true;
                    nb.FileNotebookId = ValidNotebookId(db.CustomData.Get(NotebookIdKey));
                    return nb;
                }
                catch
                {
                    if (nb != null)
                    {
                        nb.Database = null;
                        nb.Credentials = null;
                    }
                    db.Close();
                    throw;
                }
            }

            throw new IOException("The file changed while it was being opened.");
        }

        private static readonly Regex BackupFileName = new Regex(@"^.+\.bak[0-9]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Precedence: an unmarked file is NotANotebook whatever its name (that warning is the stronger one: it may be
        /// the user's password database, and the marker check is unchanged). A marked file whose path is a symbolic
        /// link is SymbolicLink: it could never be saved, and "open the real file instead" is the actionable advice
        /// even when the link is named like a backup. A marked file named like a backup copy (<c>anything.bak</c> or
        /// <c>anything.bakN</c>, ASCII digits, any case) is BackupCopy: the main notebook's next save replaces its .bak,
        /// so edits made in one would be lost (.bakN: the backups of older LockPad versions). A path whose link status cannot be verified (FileLinks.LinkCheck.CannotVerify) is
        /// treated as a link. Both checks run on the full path from LocalPath, where GetFullPath has expanded 8.3 short
        /// names (device-namespace paths, which skip that expansion, are refused there).
        /// </summary>
        internal static ReadOnlyReason ReadOnlyReasonFor(string fullPath, bool marked)
        {
            if (!marked) return ReadOnlyReason.NotANotebook;
            if (FileLinks.Check(fullPath) != FileLinks.LinkCheck.NotALink) return ReadOnlyReason.SymbolicLink;
            if (IsBackupFileName(fullPath)) return ReadOnlyReason.BackupCopy;
            return ReadOnlyReason.None;
        }

        /// <summary>True when <paramref name="path"/> names a backup copy (<c>anything.bak</c> or <c>anything.bakN</c>), after the same
        /// normalization Create and Save As apply (full path; trailing dots and spaces trimmed). False for a path that
        /// is not a valid local path (that is reported separately).</summary>
        public static bool IsBackupCopyName(string path)
        {
            string full;
            try { full = LocalPath.RequireLocalFile(path); }
            catch (NonLocalPathException) { return false; }
            return IsBackupFileName(full);
        }

        internal static bool IsBackupFileName(string fullPath)
        {
            return BackupFileName.IsMatch(Path.GetFileName(fullPath) ?? "");
        }

        /// <summary>
        /// Saves the notebook to its own file. Never throws for runtime failures: every failure is returned as a
        /// <see cref="SaveResult"/>, the in-memory notes stay intact and the notebook stays dirty. Only a null or
        /// disposed notebook throws. A clean notebook whose file is unchanged since it was loaded is not written
        /// (Saved, the .bak untouched) unless <paramref name="overwriteExternalChanges"/> is true.
        /// </summary>
        /// <param name="overwriteExternalChanges">False: refuse (ExternalChangeDetected) when the file differs from
        /// what was loaded. True: overwrite it anyway, but only if it still opens with this notebook's credentials and
        /// carries the LockPad marker; the replaced version is kept as the .bak.</param>
        public SaveResult Save(Notebook notebook, bool overwriteExternalChanges)
        {
            CompositeKey key = RequireOpen(notebook);
            if (notebook.IsReadOnly) return SaveResult.Fail(SaveStatus.ReadOnly, ReadOnlyMessage);

            try
            {
                string path = LocalPath.RequireLocalFile(notebook.FilePath);
                FileFingerprint expectedTarget;
                SaveResult refused;
                if (overwriteExternalChanges)
                {
                    refused = CheckOverwritable(path, key, notebook, out expectedTarget);
                }
                else
                {
                    refused = CheckUnchanged(path, notebook.LoadedFingerprint);
                    expectedTarget = notebook.LoadedFingerprint;
                }
                if (refused != null) return refused;

                // Nothing to save and the file is exactly what was loaded: writing would only replace the .bak with a
                // copy of the same notes. An explicit Overwrite always writes, and so does a pending re-lock of the .bak
                // after a key change (IsDirty includes NeedsWrite).
                if (!overwriteExternalChanges && !notebook.IsDirty) return SaveResult.Ok();

                // Dirty, but the notes equal the file's (typed then undone, a note added then deleted, renamed back):
                // writing would only replace the real older version in the .bak. The database in memory is the
                // file's state only while DatabaseMatchesFile holds (a failed save leaves its changes applied there).
                // Not while the .bak still uses the previous key: that write is the re-lock.
                if (!overwriteExternalChanges && !notebook.NeedsWrite && notebook.DatabaseMatchesFile
                    && NotesMatch(FindNotesGroup(notebook.Database), notebook))
                {
                    notebook.MarkClean();
                    return SaveResult.Ok();
                }

                ApplyChanges(notebook);

                // The commit sets LoadedFingerprint from the verified temp file; it is not recomputed afterwards.
                // The target is re-checked against expectedTarget right before the replace.
                SaveResult result = WriteVerifiedAndCommitCore(notebook, path, false, expectedTarget);
                if (result.Status == SaveStatus.Saved)
                {
                    notebook.MarkClean();
                    // The replace proved the .bak (current or previous key) or kept a foreign one aside, then made the
                    // replaced file (current key) the .bak: no file of this notebook uses the previous key any more.
                    notebook.CompleteRelock();
                }
                else if (notebook.PreviousCredentials != null && !File.Exists(path)
                    && result.Message.Contains(NewCopyKeptNote))
                {
                    // The swap stopped half-way (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2): the replaced file (current key)
                    // took the .bak name, so the old-key .bak is gone. The write stays pending (the file is missing).
                    notebook.DropPreviousCredentials();
                }
                return result;
            }
            catch (Exception ex) when (IsRuntimeFailure(ex))
            {
                return FailFrom(ex);
            }
        }

        /// <summary>
        /// Saves the notebook to a new file. Never replaces an existing file, not even one that appears while saving.
        /// On success the notebook moves to <paramref name="newPath"/>; on failure its path, its original file and
        /// that file's backups are untouched. Never throws for runtime failures (see <see cref="Save"/>).
        /// </summary>
        public SaveResult SaveAs(Notebook notebook, string newPath)
        {
            RequireOpen(notebook);
            if (newPath == null) throw new ArgumentNullException(nameof(newPath));

            try
            {
                string full = LocalPath.RequireLocalFile(newPath);
                // Every Save As route (menu, Ctrl+Shift+S, the close dialog, the external-change prompt) ends here.
                if (IsBackupFileName(full)) return SaveResult.Fail(SaveStatus.IoError, BackupNameReservedMessage);
                if (full.Length > MaxNotebookPathLength) return SaveResult.Fail(SaveStatus.IoError, PathTooLongMessage);
                if (File.Exists(full) || Directory.Exists(full))
                    return SaveResult.Fail(SaveStatus.IoError, FileExistsMessage);
                if (notebook.IsReadOnly) return SaveResult.Fail(SaveStatus.ReadOnly, ReadOnlyMessage);

                ApplyChanges(notebook);

                // A pending re-lock means the old file's .bak still uses the previous key (fix round 3, Y1).
                string oldBackup = notebook.PreviousCredentials != null ? BackupPath(notebook.FilePath) : null;

                // The copy is a new notebook with its own id (re-attack 2026-10-22, Medium): with the original's id, the
                // two could not be told apart by Overwrite or the .bak check, and one could destroy the other. The
                // original's id is restored if the copy is not written.
                PwDatabase db = notebook.Database;
                string originalId = db.CustomData.Get(NotebookIdKey);
                db.CustomData.Set(NotebookIdKey, NewNotebookId());
                SaveResult result = null;
                try
                {
                    result = WriteVerifiedAndCommitCore(notebook, full, true, null);
                }
                finally
                {
                    if (result == null || result.Status != SaveStatus.Saved)
                    {
                        if (originalId == null) db.CustomData.Remove(NotebookIdKey);
                        else db.CustomData.Set(NotebookIdKey, originalId);
                    }
                }
                if (result.Status == SaveStatus.Saved)
                {
                    notebook.FilePath = full;
                    notebook.MarkClean();
                    // The notebook now lives in a new file with no .bak of its own; the previous file and its .bak
                    // count as copies the user made. Nothing there is deleted or rewritten, but an old-key .bak that
                    // is still there is reported, so the user can be told.
                    notebook.CompleteRelock();
                    if (oldBackup != null && File.Exists(oldBackup))
                        return SaveResult.SavedLeavingUnrelockedBackup(oldBackup);
                }
                return result;
            }
            catch (Exception ex) when (IsRuntimeFailure(ex))
            {
                return FailFrom(ex);
            }
        }

        /// <summary>
        /// Changes the notebook's master key from <paramref name="current"/> (which must match the notebook's
        /// credentials) to <paramref name="replacement"/>, then writes the notebook twice so that both the file and its
        /// <c>.bak</c> open only with the new key: the first write saves the notes with the new key (the previous file,
        /// old key, becomes the .bak); the second, forced write of the same notes makes that new-key file the .bak.
        /// Never throws for runtime failures (see <see cref="Save"/>); only null arguments and a disposed notebook or
        /// credentials throw.
        /// <para>Refusals (ReadOnly, WrongCurrentPassword, SameAsCurrent, ExternalChangeDetected up front) write nothing
        /// and change nothing in memory. Ownership: the caller always keeps <paramref name="current"/>; the notebook
        /// takes <paramref name="replacement"/> when the first write succeeded (Changed, ChangedButBackupNotRelocked,
        /// ChangedButReplaceIncomplete); otherwise the caller keeps <paramref name="replacement"/>. The notebook keeps its
        /// previous credentials (only to prove the .bak) until the .bak is re-locked, then disposes them.</para>
        /// </summary>
        public ChangeKeyResult ChangeMasterKey(Notebook notebook, Credentials current, Credentials replacement)
        {
            CompositeKey oldKey = RequireOpen(notebook);
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            CompositeKey newKey = replacement.CompositeKey; // throws if the credentials were disposed
            Credentials oldCredentials = notebook.Credentials;

            if (notebook.IsReadOnly) return ChangeKeyResult.Of(ChangeKeyStatus.ReadOnly, ReadOnlyMessage);
            // Raw-key comparisons (no key derivation), as the lock screen's.
            if (!oldCredentials.Matches(current))
                return ChangeKeyResult.Of(ChangeKeyStatus.WrongCurrentPassword, WrongCurrentPasswordMessage);
            if (oldCredentials.Matches(replacement))
                return ChangeKeyResult.Of(ChangeKeyStatus.SameAsCurrent, SameAsCurrentMessage);

            PwDatabase db = notebook.Database;
            DateTime oldKeyChanged = db.MasterKeyChanged;
            // A key from an earlier change whose .bak re-lock is still pending (restored if this change fails).
            Credentials earlierPrevious = notebook.PreviousCredentials;
            bool earlierPreviousOwned = notebook.OwnsPreviousCredentials;
            bool switched = false;
            bool committed = false;
            try
            {
                string path = LocalPath.RequireLocalFile(notebook.FilePath);
                // There is no Overwrite path for a key change: the user saves or reloads first.
                SaveResult refused = CheckUnchanged(path, notebook.LoadedFingerprint);
                if (refused != null) return FromSaveFailure(refused);

                // Prove the .bak is ours with the OLD key (or an earlier key still pending) while the notebook still has
                // it, so the result is remembered for this notebook object. A foreign .bak is not remembered and is
                // handled by the write exactly as by a normal save (kept aside); a .bak held open by another program
                // throws here (IOException naming it) before anything changed. When this check cannot prove the .bak
                // (e.g. its KDF is heavier than the notebook's below-floor KDF), the first write, which applies
                // SaveFormatPolicy, proves it with the previous key instead (fix round 3, Y3).
                string backup = BackupPath(path);
                if (File.Exists(backup)) IsOwnBackup(notebook, backup, oldKey);

                // The old key stays with the notebook as its "previous key", used only to prove the .bak's ownership,
                // until the .bak is really re-locked (fix round 2, X3): the second write uses it when the first write's
                // memory of the .bak is gone (e.g. a scanner held it for a moment), and so does a later Save.
                notebook.Credentials = replacement;
                notebook.SetPreviousCredentials(oldCredentials,
                    !ReferenceEquals(oldCredentials, current) && !ReferenceEquals(oldCredentials, replacement));
                db.MasterKey = newKey;
                db.MasterKeyChanged = DateTime.UtcNow; // KeePass and KeePassXC show it as the key's change date
                switched = true;

                // First write: the pending notes with the new key; the previous file (old key) becomes the .bak.
                ApplyChanges(notebook);
                SaveResult first = WriteVerifiedAndCommitCore(notebook, path, false, notebook.LoadedFingerprint);
                if (first.Status != SaveStatus.Saved)
                {
                    RestoreKey(notebook, oldCredentials, oldKey, oldKeyChanged, earlierPrevious, earlierPreviousOwned);
                    switched = false;
                    ChangeKeyResult failed = FromSaveFailure(first, path);
                    // The swap stopped half-way and the notebook file is missing (re-attack 2026-10-22): keep a write
                    // pending, so a lock or close tries to save (and holds, or offers Save As) instead of dropping it.
                    if (failed.Status == ChangeKeyStatus.FailedWhileReplacing) notebook.MarkNeedsWrite();
                    return failed;
                }
                committed = true;
                // The .bak now uses the old key: a write is pending until the second write re-locks it. Marked before
                // MarkClean, so a notebook that was dirty does not flicker to clean.
                notebook.MarkNeedsWrite();
                notebook.MarkClean();
                // The file the earlier pending key protected was just replaced by the first write's .bak (old key).
                if (earlierPrevious != null && earlierPreviousOwned && !ReferenceEquals(earlierPrevious, oldCredentials))
                    earlierPrevious.Dispose();

                // Second write: the same notes again, forced (never skipped as identical content), expected to replace
                // exactly the file the first write committed. Its .bak (the old-key file) is proven by the first write's
                // memory or, failing that, with the previous key.
                SaveResult second = WriteVerifiedAndCommitCore(notebook, path, false, notebook.LoadedFingerprint);
                if (second.Status == SaveStatus.Saved)
                {
                    notebook.CompleteRelock();
                    return ChangeKeyResult.Of(ChangeKeyStatus.Changed, "");
                }
                if (!File.Exists(path) && second.Message.Contains(NewCopyKeptNote))
                {
                    // ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 in the second swap (X4): the first write's file (new key)
                    // became the .bak and the second's copy (new key) stayed at its temp path; the notebook file is
                    // missing. No file uses the old key any more, but a write stays pending, so a lock or close holds
                    // or retries instead of dropping the notes in memory.
                    notebook.DropPreviousCredentials();
                    return ChangeKeyResult.Of(ChangeKeyStatus.ChangedButReplaceIncomplete, second.Message);
                }
                return ChangeKeyResult.Of(ChangeKeyStatus.ChangedButBackupNotRelocked, second.Message);
            }
            catch (Exception ex) when (IsRuntimeFailure(ex))
            {
                string message = string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message;
                if (committed) return ChangeKeyResult.Of(ChangeKeyStatus.ChangedButBackupNotRelocked, message);
                if (switched)
                    RestoreKey(notebook, oldCredentials, oldKey, oldKeyChanged, earlierPrevious, earlierPreviousOwned);
                return ChangeKeyResult.Of(ChangeKeyStatus.Failed, message);
            }
        }

        /// <summary>Appended (with the temp path) to a failed save's reason when File.Replace failed half-way.</summary>
        private const string NewCopyKeptNote = " The new copy was kept at ";
        private const string WrongCurrentPasswordMessage = "The current password is wrong.";
        private const string SameAsCurrentMessage = "The new password is the same as the current one.";

        private static void RestoreKey(Notebook notebook, Credentials credentials, CompositeKey key, DateTime keyChanged,
            Credentials previous, bool previousOwned)
        {
            notebook.Credentials = credentials;
            notebook.SetPreviousCredentials(previous, previousOwned);
            notebook.Database.MasterKey = key;
            notebook.Database.MasterKeyChanged = keyChanged;
        }

        /// <summary>Maps a refused or failed write (old key kept) to a key-change result. When the notebook file is
        /// missing afterwards, File.Replace failed half-way and kept the verified new copy (new key) beside the
        /// previous version (old key): FailedWhileReplacing, and the reason says so. (On its other failures ReplaceFile
        /// leaves every file under its original name and the temp copy is deleted: Failed.)</summary>
        private static ChangeKeyResult FromSaveFailure(SaveResult result, string path = null)
        {
            if (result.Status == SaveStatus.ExternalChangeDetected)
                return ChangeKeyResult.Of(ChangeKeyStatus.ExternalChangeDetected, result.Message);
            string message = result.Message;
            if (path != null && !File.Exists(path) && message.Contains(NewCopyKeptNote))
                return ChangeKeyResult.Of(ChangeKeyStatus.FailedWhileReplacing,
                    message + " That new copy opens only with the new password.");
            return ChangeKeyResult.Of(ChangeKeyStatus.Failed, message);
        }

        private static CompositeKey RequireOpen(Notebook notebook)
        {
            if (notebook == null) throw new ArgumentNullException(nameof(notebook));
            Credentials credentials = notebook.Credentials;
            if (notebook.Database == null || credentials == null) throw new ObjectDisposedException(nameof(Notebook));
            return credentials.CompositeKey; // throws if the credentials were disposed
        }

        /// <summary>Only programmer errors propagate out of Save/SaveAs; everything else becomes a SaveResult.</summary>
        private static bool IsRuntimeFailure(Exception ex)
        {
            return !(ex is ArgumentNullException || ex is ObjectDisposedException);
        }

        /// <summary>The message is the bare reason; the UI owns the "Saving failed:" wording.</summary>
        private static SaveResult FailFrom(Exception ex)
        {
            string message = ex.Message;
            return SaveResult.Fail(SaveStatus.IoError, string.IsNullOrEmpty(message) ? ex.GetType().Name : message);
        }

        /// <summary>Null when the file on disk is exactly what was loaded. A fingerprint failure (locked, access
        /// denied) throws and is reported as IoError by the caller; nothing is written either way.</summary>
        private static SaveResult CheckUnchanged(string path, FileFingerprint loaded)
        {
            FileFingerprint current = FileFingerprint.Compute(path);
            if (!current.Exists) return SaveResult.Fail(SaveStatus.IoError, FileNotFoundMessage);
            if (loaded == null || !current.SameContentAs(loaded))
                return SaveResult.Fail(SaveStatus.ExternalChangeDetected, ExternalChangeMessage);
            return null;
        }

        /// <summary>
        /// Overwrite path: the file on disk is no longer the one we loaded, so its marker is unknown. LockPad never
        /// writes to a file without the marker, so it must open with our credentials and be marked. Null when it is;
        /// <paramref name="verified"/> is then the fingerprint of exactly the content that was checked, which the
        /// commit re-checks before replacing. If the file changes while it is being checked -> ExternalChangeDetected
        /// (it changed again; the user is asked again). A file with key settings heavier than the notebook's own is
        /// never test-opened (full check 2026-10-22, L2): anyone who can write to the folder could otherwise make the
        /// check run for hours on the UI thread. When both carry a notebook id, it must be this notebook's (full-check
        /// red team, M1): a different notebook with the same password would otherwise be replaced, and its content,
        /// now this notebook's .bak, destroyed by the next save.
        /// </summary>
        private SaveResult CheckOverwritable(string path, CompositeKey key, Notebook notebook,
            out FileFingerprint verified)
        {
            verified = null;
            FileFingerprint before = FileFingerprint.Compute(path); // throws (locked, denied) -> IoError in caller
            if (!before.Exists) return SaveResult.Fail(SaveStatus.IoError, FileNotFoundMessage);

            byte[] content = ReadForCheck(path);
            if (content == null) return SaveResult.Fail(SaveStatus.IoError, NotCheckableMessage);
            if (!FileFingerprint.Of(content).SameContentAs(before))
                return SaveResult.Fail(SaveStatus.ExternalChangeDetected, ExternalChangeMessage);
            KdfParameters fileKdf = KdbxHeader.ReadKdfParameters(content);
            if (fileKdf == null) return SaveResult.Fail(SaveStatus.IoError, NotOverwritableMessage);
            if (!KdfNoHeavierThan(fileKdf, notebook.Database.KdfParameters))
                return SaveResult.Fail(SaveStatus.IoError, NotCheckableMessage);

            bool marked;
            string fileId = null;
            PwDatabase db = null;
            try
            {
                TestHook?.Invoke("overwrite-kdf", path);
                db = LoadDatabase(content, key);
                marked = db.CustomData.Get(MarkerKey) == MarkerValue;
                fileId = ValidNotebookId(db.CustomData.Get(NotebookIdKey));
            }
            catch (Exception ex) when (ex is WrongCredentialsException || ex is NotebookFormatException)
            {
                marked = false;
            }
            finally
            {
                if (db != null) db.Close();
            }

            FileFingerprint after = FileFingerprint.Compute(path);
            if (!after.Exists) return SaveResult.Fail(SaveStatus.IoError, FileNotFoundMessage);
            if (!after.SameContentAs(before))
                return SaveResult.Fail(SaveStatus.ExternalChangeDetected, ExternalChangeMessage);
            if (!marked) return SaveResult.Fail(SaveStatus.IoError, NotOverwritableMessage);
            if (fileId != null && notebook.FileNotebookId != null
                && !string.Equals(fileId, notebook.FileNotebookId, StringComparison.Ordinal))
                return SaveResult.Fail(SaveStatus.IoError, DifferentNotebookMessage);

            verified = after;
            return null;
        }

        /// <summary>
        /// Mirrors the in-memory notes into the database's Notes group. Idempotent: re-applying after a failed save
        /// adds no second history entry and no second deleted-object record. Only Title and Notes are written; all
        /// other entry fields are left as they are.
        /// </summary>
        private static void ApplyChanges(Notebook notebook)
        {
            notebook.DatabaseMatchesFile = false; // until a successful commit (MarkClean)
            PwDatabase db = notebook.Database;
            PwGroup group = FindNotesGroup(db);
            if (group == null)
            {
                group = new PwGroup(true, true, NotesGroupName, PwIcon.Note);
                db.RootGroup.AddGroup(group, true);
            }

            var byId = new Dictionary<string, PwEntry>(StringComparer.Ordinal);
            foreach (PwEntry e in group.Entries)
            {
                string id = e.Uuid.ToHexString();
                if (!byId.ContainsKey(id)) byId.Add(id, e);
            }

            foreach (Note n in notebook.Notes)
            {
                string title = n.Title;
                string text = n.Text;
                PwEntry e;
                if (!byId.TryGetValue(n.Id, out e))
                {
                    e = new PwEntry(true, true);
                    e.Uuid = new PwUuid(MemUtil.HexStringToByteArray(n.Id));
                    SetNoteFields(e, title, text);
                    group.AddEntry(e, true);
                    byId.Add(n.Id, e);
                    // Undo delete keeps the note's id. If its deletion was already applied (a save, or a failed save
                    // attempt), KeePassLib holds a deletion record for this UUID; a live entry must never carry one, or
                    // a later merge or sync (KeePass, KeePassXC) could delete the restored note.
                    ForgetDeletion(db, e.Uuid);
                }
                else if (!string.Equals(e.Strings.ReadSafe(PwDefs.TitleField), title, StringComparison.Ordinal)
                    || !string.Equals(e.Strings.ReadSafe(PwDefs.NotesField), text, StringComparison.Ordinal))
                {
                    e.CreateBackup(db); // also enforces HistoryMaxItems / HistoryMaxSize (PwEntry.MaintainBackups)
                    SetNoteFields(e, title, text);
                    e.Touch(true);
                }
            }

            foreach (string id in notebook.RemovedIds)
            {
                PwEntry e;
                if (!byId.TryGetValue(id, out e)) continue; // never saved, or already removed by an earlier attempt
                if (notebook.FindNote(id) != null) continue;
                group.Entries.Remove(e);
                db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, DateTime.UtcNow));
            }
        }

        private static void ForgetDeletion(PwDatabase db, PwUuid uuid)
        {
            PwObjectList<PwDeletedObject> deleted = db.DeletedObjects;
            for (int i = (int)deleted.UCount - 1; i >= 0; i--)
                if (deleted.GetAt((uint)i).Uuid.Equals(uuid)) deleted.RemoveAt((uint)i);
        }

        private static void SetNoteFields(PwEntry e, string title, string text)
        {
            e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            e.Strings.Set(PwDefs.NotesField, new ProtectedString(true, text));
        }

        /// <summary>
        /// Writes the notebook's database to a temp file next to <paramref name="targetPath"/>, flushes it to disk,
        /// reopens and verifies it, then atomically replaces the target (keeping the old file as .bak) or moves the
        /// temp into place for a new file. The target is never modified unless verification succeeded.
        /// </summary>
        internal SaveResult WriteVerifiedAndCommit(Notebook notebook, string targetPath)
        {
            return WriteVerifiedAndCommitCore(notebook, targetPath, false, null);
        }

        /// <param name="requireNew">When true (Create, SaveAs), commit only by File.Move, which fails if the target
        /// exists: never Replace. A .bak already next to the new name is kept aside (it is never this notebook's).</param>
        /// <param name="expectedTarget">When non-null (Save), the target must still have exactly this content right
        /// before the file is replaced; otherwise nothing is replaced and the .bak is untouched (missing ->
        /// IoError "File not found.", different -> ExternalChangeDetected). The target is then never created by
        /// File.Move. Residual window: between this final hash and File.Replace (a hash of the file, no KDF).</param>
        private SaveResult WriteVerifiedAndCommitCore(Notebook notebook, string targetPath, bool requireNew,
            FileFingerprint expectedTarget)
        {
            if (requireNew && expectedTarget != null)
                throw new ArgumentException("requireNew and expectedTarget are mutually exclusive.");

            if (notebook == null) throw new ArgumentNullException(nameof(notebook));
            PwDatabase db = notebook.Database;
            Credentials credentials = notebook.Credentials;
            if (db == null || credentials == null) throw new ObjectDisposedException(nameof(Notebook));
            if (notebook.IsReadOnly) return SaveResult.Fail(SaveStatus.ReadOnly, ReadOnlyMessage);
            CompositeKey key = credentials.CompositeKey;

            string target = LocalPath.RequireLocalFile(targetPath);
            LocalIoc(target);
            string backup = BackupPath(target);
            string temp = LocalPath.RequireLocalFile(Path.Combine(
                Path.GetDirectoryName(target), Path.GetFileName(target) + "." + RandomHex8() + ".tmp"));
            IOConnectionInfo iocTemp = LocalIoc(temp);

            bool keepTemp = false;
            bool replaceStarted = false;
            bool targetExisted = false;
            try
            {
                db.UseFileTransactions = false;
                // A notebook from an older build has no id yet: it gets one with this write (an ordinary CustomData
                // entry; the marker stays "1"). An invalid value is replaced the same way.
                if (ValidNotebookId(db.CustomData.Get(NotebookIdKey)) == null)
                    db.CustomData.Set(NotebookIdKey, NewNotebookId());
                // AES-256, Argon2id at or above the floor (else fresh tuned parameters), protected notes, history 10,
                // no recycle bin: re-asserted on every write, whatever another client stored in the file.
                SaveFormatPolicy.Apply(db);
                KeePassLibPolicy.Apply(); // SaveAs runs the KDF: managed code only
                db.SaveAs(iocTemp, false, null);
                using (var fs = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    fs.Flush(true);

                TestHook?.Invoke("after-temp-write", temp);

                if (!VerifyTemp(iocTemp, key, notebook))
                    return SaveResult.Fail(SaveStatus.VerifyFailed, VerifyFailedMessage);

                TestHook?.Invoke("before-replace", temp);

                if (expectedTarget != null)
                {
                    // The KDF runs above take seconds; re-check the target so an external change, deletion or swapped-in
                    // foreign file in that time is never replaced or recreated. A Compute failure is an IoError (catch).
                    FileFingerprint current = FileFingerprint.Compute(target);
                    if (!current.Exists) return SaveResult.Fail(SaveStatus.IoError, FileNotFoundMessage);
                    if (!current.SameContentAs(expectedTarget))
                        return SaveResult.Fail(SaveStatus.ExternalChangeDetected, ExternalChangeMessage);
                }

                FileFingerprint written = FileFingerprint.Compute(temp);
                if (requireNew)
                {
                    if (File.Exists(target) || Directory.Exists(target))
                        return SaveResult.Fail(SaveStatus.IoError, FileExistsMessage);
                    // A new notebook has no history: a .bak already next to the new name is never its own (it may be
                    // the last copy of another notebook), so it is kept under a new name before this file appears.
                    RequireBackupIsNoFolder(backup);
                    if (File.Exists(backup)) KeepAside(notebook, backup);
                    notebook.OwnBackupPath = null;
                    notebook.OwnBackupFingerprint = null;
                    replaceStarted = true;
                    File.Move(temp, target); // atomic; fails rather than overwrites if a file appears right now
                    notebook.LoadedFingerprint = written;
                    notebook.FileNotebookId = ValidNotebookId(db.CustomData.Get(NotebookIdKey));
                    return SaveResult.Ok();
                }

                targetExisted = File.Exists(target);
                if (!targetExisted && expectedTarget != null)
                    return SaveResult.Fail(SaveStatus.IoError, FileNotFoundMessage); // never recreate via File.Move
                if (targetExisted)
                {
                    // One backup: the replaced file becomes target.bak, replacing the previous .bak (Win32 ReplaceFile
                    // renames the target onto the backup name). Older .bak1 to .bak3 files are never touched. A .bak
                    // that is not provably this notebook's own is kept under a new name first, never overwritten.
                    RequireBackupIsNoFolder(backup);
                    if (File.Exists(backup))
                    {
                        if (IsOwnBackup(notebook, backup, key)) ClearReadOnly(backup);
                        else KeepAside(notebook, backup);
                    }
                    replaceStarted = true;
                    if (ReplaceFile != null) ReplaceFile(temp, target, backup);
                    else File.Replace(temp, target, backup, true);
                    RememberOwnBackup(notebook, backup);
                }
                else
                {
                    replaceStarted = true;
                    File.Move(temp, target); // fails rather than overwrites if a file appeared meanwhile
                }

                notebook.LoadedFingerprint = written;
                notebook.FileNotebookId = ValidNotebookId(db.CustomData.Get(NotebookIdKey));
                return SaveResult.Ok();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                string message = ex.Message;
                if (requireNew && replaceStarted && File.Exists(target))
                    message = FileExistsMessage; // lost the race to a file created between the check and the move
                else if (replaceStarted && targetExisted && !File.Exists(target) && File.Exists(temp))
                {
                    // File.Replace failed half-way (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2): the original was renamed to
                    // the backup but the new copy could not take its place. Keep the verified new copy instead of
                    // deleting it. (On its other failures ReplaceFile leaves every file under its original name.)
                    keepTemp = true;
                    message += NewCopyKeptNote + temp;
                    message += File.Exists(backup) ? " and the previous version at " + backup + "." : ".";
                }
                else if (replaceStarted && targetExisted && File.Exists(target) && File.Exists(backup)
                    && BackupBlocksReplace(target, backup))
                {
                    // ReplaceFile reports a .bak it cannot overwrite as "Unable to remove the file to be replaced.",
                    // which points at the notebook: name the .bak instead.
                    message = BackupErrorMessage(backup, "it is read-only or in use by another program.");
                }
                return SaveResult.Fail(SaveStatus.IoError, message);
            }
            finally
            {
                if (!keepTemp) TryDelete(temp);
            }
        }

        /// <summary>"Could not replace the backup file "name.bak": reason".</summary>
        private static string BackupErrorMessage(string backup, string reason)
        {
            return "Could not replace the backup file \"" + Path.GetFileName(backup) + "\": " + reason;
        }

        /// <summary>A folder named like the backup is never moved or replaced: the save fails with a message naming it.</summary>
        private static void RequireBackupIsNoFolder(string backup)
        {
            if (Directory.Exists(backup))
                throw new IOException(BackupErrorMessage(backup, "it is a folder. Rename or move it, then save again."));
        }

        private static readonly Regex NotebookIdFormat = new Regex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

        /// <summary>A new random notebook id: 16 bytes from a cryptographic RNG as 32 lowercase hex characters.</summary>
        private static string NewNotebookId()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(32);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary><paramref name="value"/> when it is a well-formed notebook id, else null.</summary>
        private static string ValidNotebookId(string value)
        {
            return value != null && NotebookIdFormat.IsMatch(value) ? value : null;
        }

        /// <summary>
        /// True when <paramref name="backup"/> is provably this notebook's own: it is still exactly the file this notebook
        /// object wrote or proved earlier, or it opens with the notebook's credentials, carries the LockPad marker and,
        /// when both it and the notebook's file have a notebook id, the same id
        /// (one key derivation, then remembered for this notebook object). A link or reparse point is never opened and
        /// never its own. Anything that cannot be read or checked is not its own. Only reads the file.
        /// <para>After a key change whose .bak is not re-locked yet, a .bak that does not open with
        /// <paramref name="key"/> is tried with the notebook's previous key (<see cref="Notebook.PreviousCredentials"/>),
        /// under the same marker, notebook id and KDF-cap rules (a second key derivation; fix round 2, X3).</para>
        /// </summary>
        private bool IsOwnBackup(Notebook notebook, string backup, CompositeKey key)
        {
            try
            {
                if ((File.GetAttributes(backup) & FileAttributes.ReparsePoint) != 0) return false;
                FileFingerprint before = FileFingerprint.Compute(backup);
                if (!before.Exists) return true; // gone meanwhile: there is nothing to overwrite
                if (string.Equals(notebook.OwnBackupPath, backup, StringComparison.OrdinalIgnoreCase)
                    && before.SameContentAs(notebook.OwnBackupFingerprint))
                    return true;

                TestHook?.Invoke("classify-backup", backup);
                // The cap and the key derivation use the same bytes, and those are the bytes fingerprinted above, so the
                // file cannot be swapped between the check and the derivation (full check 2026-10-22).
                byte[] content = ReadForCheck(backup);
                if (content == null || !FileFingerprint.Of(content).SameContentAs(before)) return false;
                // Never derive a key with settings heavier than this notebook's own: a foreign file could make the
                // check slow or exhaust memory. A genuine own .bak was written with these or lighter parameters.
                if (!KdfNoHeavierThan(KdbxHeader.ReadKdfParameters(content), notebook.Database.KdfParameters))
                    return false;
                bool marked;
                string backupId;
                PwDatabase db = null;
                try
                {
                    TestHook?.Invoke("backup-kdf", backup);
                    db = LoadWithKeyOrPrevious(content, key, notebook.PreviousCredentials);
                    marked = db.CustomData.Get(MarkerKey) == MarkerValue;
                    backupId = ValidNotebookId(db.CustomData.Get(NotebookIdKey));
                }
                finally
                {
                    if (db != null) db.Close();
                }
                if (!marked) return false;
                // Two notebooks can share a password: when both carry an id, it must be the same notebook. Without an
                // id on either side (files from older builds), key + marker decide as before.
                if (backupId != null && notebook.FileNotebookId != null
                    && !string.Equals(backupId, notebook.FileNotebookId, StringComparison.Ordinal))
                    return false;

                FileFingerprint after = FileFingerprint.Compute(backup);
                if (!after.SameContentAs(before)) return false; // changed while it was checked: not proven
                notebook.OwnBackupPath = backup;
                notebook.OwnBackupFingerprint = after;
                return true;
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex))
            {
                // Another program holds the .bak open right now (a backup tool, antivirus, an indexer). That says
                // nothing about whose it is: never relocate it on that basis. The save fails, nothing changes, and the
                // user can retry. (Also for a .bak this session wrote: its content cannot be compared while locked.)
                throw new IOException(BackupErrorMessage(backup, "it is in use by another program. Try again in a moment."), ex);
            }
            catch (Exception ex) when (ex is WrongCredentialsException || ex is NotebookFormatException
                || ex is IOException || ex is UnauthorizedAccessException || ex is OutOfMemoryException)
            {
                // Wrong key, not KDBX, unreadable, or a KDF too large to run: not provably ours.
                return false;
            }
        }

        /// <summary>Opens the .bak's <paramref name="content"/> with <paramref name="key"/>; when that is the wrong key
        /// and the notebook still holds a previous key (a .bak re-lock is pending), with that one. Other failures
        /// propagate.</summary>
        private static PwDatabase LoadWithKeyOrPrevious(byte[] content, CompositeKey key, Credentials previous)
        {
            try
            {
                return LoadDatabase(content, key);
            }
            catch (WrongCredentialsException) when (previous != null)
            {
                return LoadDatabase(content, previous.CompositeKey);
            }
        }

        /// <summary>
        /// The whole file, read once for an ownership check that derives a key; null when it is larger than
        /// <see cref="MaxCheckedFileBytes"/> or shrinks while it is read. I/O errors propagate.
        /// </summary>
        private static byte[] ReadForCheck(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long length = fs.Length;
                if (length > MaxCheckedFileBytes) return null;
                var content = new byte[length];
                int read = 0;
                while (read < content.Length)
                {
                    int n = fs.Read(content, read, content.Length - read);
                    if (n == 0) return null;
                    read += n;
                }
                return content;
            }
        }

        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;

        /// <summary>True for ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION (a file another process holds open or
        /// locked), as .NET Framework reports them: an IOException whose HResult carries the Win32 code.</summary>
        private static bool IsSharingOrLockViolation(IOException ex)
        {
            int code = ex.HResult & 0xFFFF;
            return (ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)
                && (code == ErrorSharingViolation || code == ErrorLockViolation);
        }

        /// <summary>
        /// True when <paramref name="candidate"/> (read from a file's header) is Argon2id or Argon2d with readable memory,
        /// iterations and parallelism, and is no heavier than <paramref name="current"/> (the notebook's KDF for this
        /// save): memory at most the notebook's, iterations at most twice the notebook's, parallelism at most the
        /// notebook's. Lighter is allowed: the previous version may predate a save that replaced a below-floor KDF.
        /// </summary>
        private static bool KdfNoHeavierThan(KdfParameters candidate, KdfParameters current)
        {
            ulong memory, iterations, currentMemory, currentIterations;
            uint lanes, currentLanes;
            if (!ReadArgon2(candidate, out memory, out iterations, out lanes)) return false;
            if (!ReadArgon2(current, out currentMemory, out currentIterations, out currentLanes)) return false;
            return memory <= currentMemory
                && iterations <= currentIterations * 2
                && lanes <= currentLanes;
        }

        private static readonly PwUuid Argon2idUuid = new Argon2Kdf(Argon2Type.ID).Uuid;
        private static readonly PwUuid Argon2dUuid = new Argon2Kdf(Argon2Type.D).Uuid;

        private static bool ReadArgon2(KdfParameters p, out ulong memory, out ulong iterations, out uint lanes)
        {
            memory = iterations = 0;
            lanes = 0;
            if (p == null || !(Argon2idUuid.Equals(p.KdfUuid) || Argon2dUuid.Equals(p.KdfUuid))) return false;
            memory = p.GetUInt64(Argon2Kdf.ParamMemory, 0);
            iterations = p.GetUInt64(Argon2Kdf.ParamIterations, 0);
            lanes = p.GetUInt32(Argon2Kdf.ParamParallelism, 0);
            return memory > 0 && iterations > 0 && lanes > 0 && iterations <= ulong.MaxValue / 2;
        }

        /// <summary>ReplaceFile cannot overwrite a read-only backup; this notebook's own .bak may be read-only (set by the
        /// user or a copy or sync tool), so the attribute is cleared right before the replace. Failure names the .bak.</summary>
        private static void ClearReadOnly(string backup)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(backup);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(backup, attributes & ~FileAttributes.ReadOnly);
            }
            catch (FileNotFoundException)
            {
                // Gone meanwhile: nothing to overwrite.
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new IOException(BackupErrorMessage(backup, ex.Message), ex);
            }
        }

        /// <summary>
        /// After a ReplaceFile failure that left every file in place: true when the .bak, not the notebook, is the likely
        /// cause, i.e. the .bak is read-only or held open by another program while the notebook itself is not held open.
        /// Probes only by opening for reading.
        /// </summary>
        private static bool BackupBlocksReplace(string target, string backup)
        {
            try
            {
                bool backupFree = (File.GetAttributes(backup) & FileAttributes.ReadOnly) == 0 && CanOpenExclusively(backup);
                return !backupFree && CanOpenExclusively(target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool CanOpenExclusively(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Renames a <c>.bak</c> that is not this notebook's own to <c>name.bak.kept-yyyyMMdd-HHmmss</c> (then -2, -3, ...)
        /// with File.Move, which never overwrites, and records a notice for the user. The kept name matches neither the
        /// backup-name pattern nor <c>*.tmp</c>. On failure nothing was changed and the save fails.
        /// </summary>
        private void KeepAside(Notebook notebook, string backup)
        {
            string stem = backup + ".kept-" + Now().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string kept = stem;
            for (int n = 2; File.Exists(kept) || Directory.Exists(kept); n++) kept = stem + "-" + n;
            try
            {
                File.Move(backup, kept);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new IOException("An existing file \"" + Path.GetFileName(backup) + "\" was not created by this "
                    + "notebook and could not be moved aside: " + ex.Message, ex);
            }
            notebook.AddBackupNotice("An existing file \"" + Path.GetFileName(backup) + "\" was not created by this "
                + "notebook. It was kept as \"" + Path.GetFileName(kept) + "\".");
        }

        /// <summary>After a successful replace the .bak is the file this notebook just moved there: remember its content,
        /// so the next save replaces it without a key derivation. Best effort: if it cannot be read, the next save
        /// checks it again.</summary>
        private static void RememberOwnBackup(Notebook notebook, string backup)
        {
            try
            {
                FileFingerprint fp = FileFingerprint.Compute(backup);
                notebook.OwnBackupPath = fp.Exists ? backup : null;
                notebook.OwnBackupFingerprint = fp.Exists ? fp : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                notebook.OwnBackupPath = null;
                notebook.OwnBackupFingerprint = null;
            }
        }

        private static PwDatabase LoadDatabase(IOConnectionInfo ioc, CompositeKey key)
        {
            return LoadDatabase(db => db.Open(ioc, key, null));
        }

        /// <summary>Loads a file's bytes already read (an ownership check), with the same error mapping as a file.</summary>
        private static PwDatabase LoadDatabase(byte[] content, CompositeKey key)
        {
            return LoadDatabase(db =>
            {
                db.MasterKey = key;
                // As PwDatabase.Open does: KeePassLib's clean-up after a failed load expects a root group.
                db.RootGroup = new PwGroup(true, true);
                using (var ms = new MemoryStream(content, false))
                    new KdbxFile(db).Load(ms, KdbxFormat.Default, null);
            });
        }

        private static PwDatabase LoadDatabase(Action<PwDatabase> load)
        {
            var db = new PwDatabase();
            try
            {
                KeePassLibPolicy.Apply(); // loading runs the KDF: managed code only
                load(db);
                return db;
            }
            catch (InvalidCompositeKeyException)
            {
                db.Close();
                throw new WrongCredentialsException();
            }
            catch (IOException ex) when (IsCorruptContent(ex))
            {
                // Truncated or damaged file: a content problem, not an I/O failure.
                db.Close();
                throw new NotebookFormatException(FormatErrorMessage, ex);
            }
            catch (Exception ex) when (!(ex is IOException || ex is UnauthorizedAccessException || ex is OutOfMemoryException))
            {
                db.Close();
                throw new NotebookFormatException(FormatErrorMessage, ex);
            }
            catch
            {
                db.Close();
                throw;
            }
        }

        /// <summary>
        /// KeePassLib reports damaged content as I/O errors: BinaryReaderEx rethrows any read failure as a plain
        /// IOException, and truncation surfaces as EndOfStreamException, both with a KLRes "corrupted" message.
        /// The file was fully readable moments earlier (fingerprint), so these indicate bad content.
        /// </summary>
        private static bool IsCorruptContent(IOException ex)
        {
            if (ex is EndOfStreamException) return true;
            if (ex.GetType() != typeof(IOException)) return false;
            string m = ex.Message ?? "";
            return StartsWith(m, KLRes.FileCorrupted) || StartsWith(m, KLRes.FileHeaderCorrupted);
        }

        private static bool StartsWith(string s, string prefix)
        {
            return !string.IsNullOrEmpty(prefix) && s.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static bool VerifyTemp(IOConnectionInfo iocTemp, CompositeKey key, Notebook notebook)
        {
            var check = new PwDatabase();
            try
            {
                KeePassLibPolicy.Apply(); // Open runs the KDF: managed code only
                check.Open(iocTemp, key, null);
                return MatchesNotebook(check, notebook);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return false;
            }
            finally
            {
                check.Close();
            }
        }

        private static bool MatchesNotebook(PwDatabase db, Notebook notebook)
        {
            if (db.CustomData.Get(MarkerKey) != MarkerValue) return false;
            if (!SaveFormatPolicy.IsSatisfiedBy(db)) return false; // what was written carries the enforced settings
            PwGroup group = FindNotesGroup(db);
            if (group == null) return false;
            return NotesMatch(group, notebook);
        }

        /// <summary>True when <paramref name="group"/> holds exactly the notebook's notes: the same set of ids, each with
        /// the same title and text (ordinal). A missing group matches only an empty notebook.</summary>
        private static bool NotesMatch(PwGroup group, Notebook notebook)
        {
            if (group == null) return notebook.Notes.Count == 0;
            if ((long)group.Entries.UCount != notebook.Notes.Count) return false;

            var byId = new Dictionary<string, PwEntry>(StringComparer.Ordinal);
            foreach (PwEntry e in group.Entries)
            {
                string id = e.Uuid.ToHexString();
                if (byId.ContainsKey(id)) return false;
                byId.Add(id, e);
            }

            foreach (Note n in notebook.Notes)
            {
                PwEntry e;
                if (!byId.TryGetValue(n.Id, out e)) return false;
                if (!string.Equals(e.Strings.ReadSafe(PwDefs.TitleField), n.Title, StringComparison.Ordinal)) return false;
                if (!string.Equals(e.Strings.ReadSafe(PwDefs.NotesField), n.Text, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static PwGroup FindNotesGroup(PwDatabase db)
        {
            foreach (PwGroup g in db.RootGroup.Groups)
                if (string.Equals(g.Name, NotesGroupName, StringComparison.Ordinal)) return g;
            return null;
        }

        private static Note ToNote(PwEntry e)
        {
            // KeePassLib resets MemoryProtection on load, so titles are always written unprotected and therefore passed
            // through SafeXmlString. A title stored protected by another client can hold characters that would be
            // stripped; hold the stripped value so the in-memory notebook equals what any subsequent write stores.
            // Text gets the same lone-surrogate normalization as Notebook.UpdateNote.
            return Note.Create(
                e.Uuid.ToHexString(),
                XmlSafeText.Sanitize(e.Strings.ReadSafe(PwDefs.TitleField)),
                XmlSafeText.NormalizeText(e.Strings.ReadSafe(PwDefs.NotesField)),
                e.LastModificationTime);
        }

        private static void ValidateKeyFileForCreate(string keyFilePath, string notebookFullPath)
        {
            string keyFull = LocalPath.RequireLocalFile(keyFilePath);
            if (string.Equals(keyFull, notebookFullPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The key file cannot be the notebook file itself. Choose a different key file.");

            LocalIoc(keyFull);
            try
            {
                new KcpKeyFile(keyFull, true);
            }
            catch (InvalidDataException)
            {
                throw new ArgumentException(KeyFileIsDatabaseMessage);
            }
        }

        /// <summary>KeePassLib issues web requests for non-local connections; refuse them as a second line of defence.</summary>
        private static IOConnectionInfo LocalIoc(string fullPath)
        {
            IOConnectionInfo ioc = IOConnectionInfo.FromPath(fullPath);
            if (!ioc.IsLocalFile())
                throw new NonLocalPathException("Only local file paths are allowed.");
            return ioc;
        }

        private static string RandomHex8()
        {
            var bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(8);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best effort; a leftover temp file never affects the notebook itself.
            }
        }
    }
}

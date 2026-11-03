namespace LockPad.Core
{
    /// <summary>The outcome of <see cref="NotebookStore.ChangeMasterKey"/>.</summary>
    public enum ChangeKeyStatus
    {
        /// <summary>The notebook file and its .bak now open only with the new password.</summary>
        Changed,
        /// <summary>Refused: the notebook is read-only. Nothing was written or changed.</summary>
        ReadOnly,
        /// <summary>Refused: the current password (or key file) does not match. Nothing was written or changed.</summary>
        WrongCurrentPassword,
        /// <summary>Refused: the new credentials equal the current ones. Nothing was written or changed.</summary>
        SameAsCurrent,
        /// <summary>The file on disk differs from what was loaded (also when it changed during the first write). The
        /// old key is kept; the user saves or reopens first.</summary>
        ExternalChangeDetected,
        /// <summary>The first write failed: the old key is kept, the notebook file was not replaced.</summary>
        Failed,
        /// <summary>The first write failed half-way through the file swap (File.Replace renamed the notebook file to
        /// the .bak but could not move the new copy into place): the old key is kept in memory, but the verified new
        /// copy, which opens only with the new password, was kept at a temp path (named in the message) next to the
        /// previous version (old password).</summary>
        FailedWhileReplacing,
        /// <summary>The notebook file was written with the new key (the notebook now uses it), but the second write,
        /// which re-locks the .bak, failed: the .bak still opens with the old password. The notebook keeps the old key
        /// (only to prove the .bak) and needs a write: the next save, also on lock or close, re-locks it.</summary>
        ChangedButBackupNotRelocked,
        /// <summary>The notebook file was written with the new key, but the second write's file swap stopped half-way
        /// (File.Replace renamed the notebook file to the .bak and could not move the new copy into place): every file
        /// now opens only with the new password, the notebook file is missing, and the new copy is at a temp path named
        /// in the message. The notebook needs a write, so lock and close hold or retry instead of dropping it.</summary>
        ChangedButReplaceIncomplete,
    }

    public sealed class ChangeKeyResult
    {
        private ChangeKeyResult(ChangeKeyStatus status, string message)
        {
            Status = status;
            Message = message ?? "";
        }

        public ChangeKeyStatus Status { get; }

        /// <summary>The bare reason (empty for Changed); the UI owns the surrounding wording.</summary>
        public string Message { get; }

        internal static ChangeKeyResult Of(ChangeKeyStatus status, string message)
        {
            return new ChangeKeyResult(status, message);
        }
    }
}

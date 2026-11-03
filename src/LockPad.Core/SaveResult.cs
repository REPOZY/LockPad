namespace LockPad.Core
{
    public enum SaveStatus
    {
        Saved,
        ExternalChangeDetected,
        ReadOnly,
        VerifyFailed,
        IoError,
        /// <summary>The user cancelled (e.g. closed the Save As picker). Never returned by NotebookStore.</summary>
        Cancelled
    }

    public sealed class SaveResult
    {
        private static readonly SaveResult OkInstance = new SaveResult(SaveStatus.Saved, "");

        private SaveResult(SaveStatus status, string message, string unrelockedBackupPath = null)
        {
            Status = status;
            Message = message ?? "";
            UnrelockedBackupPath = unrelockedBackupPath;
        }

        public SaveStatus Status { get; }
        public string Message { get; }

        /// <summary>
        /// Set only by a successful Save As that cleared a pending re-lock of the old file's <c>.bak</c> after a key
        /// change (fix round 3, Y1): the full path of that .bak, which still exists and still uses the previous key.
        /// Nothing at the old location is deleted or rewritten; the caller tells the user. Null otherwise.
        /// </summary>
        public string UnrelockedBackupPath { get; }

        public static SaveResult Ok()
        {
            return OkInstance;
        }

        internal static SaveResult SavedLeavingUnrelockedBackup(string backupPath)
        {
            return new SaveResult(SaveStatus.Saved, "", backupPath);
        }

        public static SaveResult Fail(SaveStatus s, string message)
        {
            return new SaveResult(s, message);
        }
    }
}

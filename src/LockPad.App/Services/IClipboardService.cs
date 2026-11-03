namespace LockPad.App.Services
{
    public interface IClipboardService
    {
        /// <summary>
        /// Copies <paramref name="text"/> to the clipboard, excluded from clipboard history,
        /// cloud sync and monitors, and schedules an automatic clear. Null or empty text is a no-op.
        /// Throws if the clipboard stays unavailable after retries.
        /// </summary>
        void CopyProtected(string text);

        /// <summary>
        /// Clears the clipboard only if it still holds text LockPad placed on the clipboard (the last
        /// copy, or the previous one after an inconclusive failed copy). Never throws. Callers MUST call this on lock and on application exit: the copy is
        /// flushed to the clipboard and survives the process ending.
        /// </summary>
        void ClearIfOwned();

        /// <summary>
        /// True while a fingerprint of text LockPad placed on the clipboard is still held, i.e. a clear is still owed
        /// (no clear has succeeded or found foreign content since the copy). Used on exit to retry a failed clear.
        /// </summary>
        bool OwnsClipboardContent { get; }
    }
}

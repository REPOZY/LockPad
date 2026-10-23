namespace LockPad.Core
{
    /// <summary>
    /// Clipboard format names Windows honours to keep copied secrets out of clipboard
    /// history (Win+V), cloud clipboard sync and well-behaved clipboard monitors.
    /// Third-party clipboard managers that ignore these formats are not covered.
    /// </summary>
    public static class ClipboardFormats
    {
        /// <summary>Presence alone asks clipboard monitors to skip the content.</summary>
        public const string ExcludeFromMonitor = "ExcludeClipboardContentFromMonitorProcessing";

        /// <summary>DWORD 0 = do not add the content to Windows clipboard history.</summary>
        public const string CanIncludeInHistory = "CanIncludeInClipboardHistory";

        /// <summary>DWORD 0 = do not sync the content to the cloud clipboard.</summary>
        public const string CanUploadToCloud = "CanUploadToCloudClipboard";

        /// <summary>Returns a new 4-byte zero DWORD on every call.</summary>
        public static byte[] DwordZero()
        {
            return new byte[4];
        }
    }
}

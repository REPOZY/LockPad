using System;
using System.Runtime.Serialization;

namespace LockPad.Core
{
    /// <summary>
    /// Non-secret user preferences. Contains exactly seven members; never add secrets here.
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        private const double DefaultFontSize = 14;
        private const int DefaultIdleLockMinutes = 5;
        private const int DefaultClipboardClearSeconds = 20;

        /// <summary>
        /// Untrusted input: settings.json is user-writable. Stored verbatim and never opened here.
        /// Consumers must pass it through <c>LocalPath.RequireLocalFile</c> before use.
        /// </summary>
        [DataMember] public string LastNotebookPath { get; set; }

        [DataMember] public double FontSize { get; set; } = DefaultFontSize;

        [DataMember] public bool WordWrap { get; set; } = true;

        [DataMember] public string Theme { get; set; } = "System";

        [DataMember] public int IdleLockMinutes { get; set; } = DefaultIdleLockMinutes;

        [DataMember] public int ClipboardClearSeconds { get; set; } = DefaultClipboardClearSeconds;

        /// <summary>Keep LockPad's windows out of screenshots, recordings and screen sharing (on by default; 2026-10-21).</summary>
        [DataMember] public bool HideFromScreenCapture { get; set; } = true;

        // DataContractJsonSerializer neither runs the constructor nor field initializers,
        // so members absent from the JSON would otherwise get default(T).
        [OnDeserializing]
        private void ApplyDefaults(StreamingContext context)
        {
            LastNotebookPath = null;
            FontSize = DefaultFontSize;
            WordWrap = true;
            Theme = "System";
            IdleLockMinutes = DefaultIdleLockMinutes;
            ClipboardClearSeconds = DefaultClipboardClearSeconds;
            HideFromScreenCapture = true;
        }

        /// <summary>Returns a copy with all values clamped to their valid ranges.</summary>
        public AppSettings Normalized()
        {
            double font = FontSize;
            if (double.IsNaN(font) || double.IsInfinity(font)) font = DefaultFontSize;

            return new AppSettings
            {
                LastNotebookPath = LastNotebookPath,
                FontSize = Math.Min(48.0, Math.Max(8.0, font)),
                WordWrap = WordWrap,
                Theme = CanonicalTheme(Theme),
                IdleLockMinutes = Math.Min(60, Math.Max(1, IdleLockMinutes)),
                ClipboardClearSeconds = Math.Min(120, Math.Max(5, ClipboardClearSeconds)),
                HideFromScreenCapture = HideFromScreenCapture
            };
        }

        private static string CanonicalTheme(string theme)
        {
            foreach (string t in new[] { "System", "Light", "Dark" })
                if (string.Equals(theme, t, StringComparison.OrdinalIgnoreCase)) return t;
            return "System";
        }
    }
}

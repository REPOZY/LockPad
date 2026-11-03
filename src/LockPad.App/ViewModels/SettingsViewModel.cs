using LockPad.App.Infrastructure;
using LockPad.App.Services;
using LockPad.Core;

namespace LockPad.App.ViewModels
{
    /// <summary>
    /// Editable copy of the user-facing settings shown in the settings dialog. Nothing is applied until
    /// <see cref="ApplyTo"/> is called by the caller after the user pressed OK.
    /// </summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        private int _idleLockMinutes;
        private int _clipboardClearSeconds;
        private AppTheme _theme;
        private bool _hideFromScreenCapture;

        public SettingsViewModel(AppSettings current)
        {
            AppSettings s = (current ?? new AppSettings()).Normalized();
            _idleLockMinutes = s.IdleLockMinutes;
            _clipboardClearSeconds = s.ClipboardClearSeconds;
            _theme = ThemeService.Parse(s.Theme);
            _hideFromScreenCapture = s.HideFromScreenCapture;
        }

        /// <summary>Minutes of inactivity before the notebook locks (1-60 after normalization).</summary>
        public int IdleLockMinutes
        {
            get { return _idleLockMinutes; }
            set { SetProperty(ref _idleLockMinutes, value); }
        }

        /// <summary>Seconds before a protected copy is cleared from the clipboard (5-120 after normalization).</summary>
        public int ClipboardClearSeconds
        {
            get { return _clipboardClearSeconds; }
            set { SetProperty(ref _clipboardClearSeconds, value); }
        }

        public AppTheme Theme
        {
            get { return _theme; }
            set { SetProperty(ref _theme, value); }
        }

        /// <summary>Keep LockPad's windows out of screenshots, recordings and screen sharing.</summary>
        public bool HideFromScreenCapture
        {
            get { return _hideFromScreenCapture; }
            set { SetProperty(ref _hideFromScreenCapture, value); }
        }

        /// <summary>
        /// Copies the four values into a copy of <paramref name="target"/> and returns it clamped to the valid
        /// ranges. <paramref name="target"/> itself is not modified.
        /// </summary>
        public AppSettings ApplyTo(AppSettings target)
        {
            AppSettings source = target ?? new AppSettings();
            return new AppSettings
            {
                LastNotebookPath = source.LastNotebookPath,
                FontSize = source.FontSize,
                WordWrap = source.WordWrap,
                IdleLockMinutes = IdleLockMinutes,
                ClipboardClearSeconds = ClipboardClearSeconds,
                Theme = Theme.ToString(),
                HideFromScreenCapture = HideFromScreenCapture
            }.Normalized();
        }
    }
}

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LockPad.App.Services
{
    /// <summary>
    /// Follows Windows while LockPad runs (fix round 1, X8):
    /// <list type="bullet">
    /// <item>SystemParameters "HighContrast" changed: re-applies the theme in the new contrast mode
    /// (<see cref="ThemeService.OnHighContrastChanged"/>).</item>
    /// <item>Windows' colours changed (SystemEvents.UserPreferenceChanged with category Color, Accessibility,
    /// VisualStyle or General), e.g. one high-contrast scheme to another: while high contrast is on, the theme keys are
    /// rebuilt from the new system colours (<see cref="ThemeService.OnSystemColoursChanged"/>); otherwise nothing.</item>
    /// </list>
    /// Both are queued to the UI thread at Background priority (SystemEvents may raise on another thread, and WPF first
    /// updates its own system colours), never throw into the notification, and stop with <see cref="Dispose"/>, which
    /// unsubscribes from both static events (no leak, nothing re-applies after exit).
    /// </summary>
    internal sealed class SystemThemeWatcher : IDisposable
    {
        private readonly Action<PropertyChangedEventHandler> _removeParameterChanged;
        private readonly Action<UserPreferenceChangedEventHandler> _removePreferenceChanged;
        private readonly Action<Action> _post;
        private readonly Action _onHighContrastChanged;
        private readonly Action _onSystemColoursChanged;
        private readonly PropertyChangedEventHandler _parameterHandler;
        private readonly UserPreferenceChangedEventHandler _preferenceHandler;
        private volatile bool _disposed;

        /// <summary>Subscribes at once. Every argument is a seam; <see cref="ForApplication"/> passes the real ones.</summary>
        internal SystemThemeWatcher(
            Action<PropertyChangedEventHandler> addParameterChanged,
            Action<PropertyChangedEventHandler> removeParameterChanged,
            Action<UserPreferenceChangedEventHandler> addPreferenceChanged,
            Action<UserPreferenceChangedEventHandler> removePreferenceChanged,
            Action<Action> post, Action onHighContrastChanged, Action onSystemColoursChanged)
        {
            if (addParameterChanged == null) throw new ArgumentNullException(nameof(addParameterChanged));
            if (addPreferenceChanged == null) throw new ArgumentNullException(nameof(addPreferenceChanged));
            _removeParameterChanged = removeParameterChanged ?? throw new ArgumentNullException(nameof(removeParameterChanged));
            _removePreferenceChanged = removePreferenceChanged ?? throw new ArgumentNullException(nameof(removePreferenceChanged));
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _onHighContrastChanged = onHighContrastChanged ?? throw new ArgumentNullException(nameof(onHighContrastChanged));
            _onSystemColoursChanged = onSystemColoursChanged ?? throw new ArgumentNullException(nameof(onSystemColoursChanged));
            _parameterHandler = OnSystemParameterChanged;
            _preferenceHandler = OnUserPreferenceChanged;
            addParameterChanged(_parameterHandler);
            addPreferenceChanged(_preferenceHandler);
        }

        /// <summary>The application's watcher: the real static events, queued to <paramref name="dispatcher"/>.</summary>
        internal static SystemThemeWatcher ForApplication(Dispatcher dispatcher)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            return new SystemThemeWatcher(
                h => SystemParameters.StaticPropertyChanged += h,
                h => SystemParameters.StaticPropertyChanged -= h,
                h => SystemEvents.UserPreferenceChanged += h,
                h => SystemEvents.UserPreferenceChanged -= h,
                a => dispatcher.BeginInvoke(DispatcherPriority.Background, a),
                ThemeService.OnHighContrastChanged,
                ThemeService.OnSystemColoursChanged);
        }

        /// <summary>The preference categories Windows reports for a colour, contrast or visual-style change.</summary>
        internal static bool IsColourCategory(UserPreferenceCategory category)
        {
            return category == UserPreferenceCategory.Color || category == UserPreferenceCategory.Accessibility
                || category == UserPreferenceCategory.VisualStyle || category == UserPreferenceCategory.General;
        }

        private void OnSystemParameterChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null || e.PropertyName != "HighContrast") return;
            Queue(_onHighContrastChanged);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e == null || !IsColourCategory(e.Category)) return;
            Queue(_onSystemColoursChanged);
        }

        /// <summary>Best effort: never throws into the notification, and runs nothing once disposed.</summary>
        private void Queue(Action reapply)
        {
            if (_disposed) return;
            try
            {
                _post(() =>
                {
                    if (_disposed) return;
                    try { reapply(); }
                    catch (Exception) { }
                });
            }
            catch (Exception)
            {
                // The dispatcher is shutting down.
            }
        }

        /// <summary>Unsubscribes from both static events; once.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _removeParameterChanged(_parameterHandler); }
            catch (Exception) { }
            try { _removePreferenceChanged(_preferenceHandler); }
            catch (Exception) { }
        }
    }
}

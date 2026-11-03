using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.Core;

namespace LockPad.App
{
    public partial class App : Application
    {
        private DialogService _dialogs;
        private readonly UnhandledErrorGate _gate = new UnhandledErrorGate();
        private SystemThemeWatcher _themeWatcher;

        /// <summary>
        /// The single settings instance shared by every service and view model. Later code MUST
        /// mutate it in place and never replace it, so closures such as
        /// <c>() =&gt; settings.ClipboardClearSeconds</c> always see the current value.
        /// </summary>
        internal AppSettings Settings { get; private set; }
        internal SettingsStore SettingsStore { get; private set; }
        internal ClipboardService Clipboard { get; private set; }
        internal ShellViewModel Shell { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Before anything can run a key derivation or load a DLL by name.
            HardenNativeLoading();
            // Before any note can be in memory: no WER crash report for this user, and no heap in any report. Best
            // effort, never blocks startup. Undo: delete the value LockPad.exe under
            // HKCU\Software\Microsoft\Windows\Windows Error Reporting\ExcludedApplications (see ErrorReporting).
            ErrorReporting.Apply(new NativeErrorReportingApi(), ErrorReporting.CurrentExeName());
            base.OnStartup(e);

            _dialogs = new DialogService();

            // Never write crash logs or exception details anywhere: a stack trace or dump could
            // contain note text.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += (s, args) => args.SetObserved();

            try
            {
                SettingsStore = new SettingsStore(SettingsStore.DefaultDirectory);
                Settings = SettingsStore.Load();
                // Every window asks DWM for a title bar that matches the theme (best effort; see DarkTitleBar).
                DarkTitleBar.Api = new NativeDwmApi();
                // Every window asks to be left out of screenshots and screen sharing while the setting is on (best
                // effort; see CaptureProtection). Set before any window exists, so none is ever shown unprotected.
                CaptureProtection.Api = new NativeDisplayAffinityApi();
                CaptureProtection.SetEnabled(Settings.HideFromScreenCapture, null);
                ThemeService.Apply(ThemeService.Parse(Settings.Theme));
                // Windows high contrast on or off (the system styles and colours, or the theme again), and Windows'
                // colours changing while high contrast is on. Disposed on exit: both are static events.
                _themeWatcher = SystemThemeWatcher.ForApplication(Dispatcher);

                AppSettings settings = Settings;
                // Constructed here on the UI thread: it binds a DispatcherTimer to this thread.
                Clipboard = new ClipboardService(() => settings.ClipboardClearSeconds);
                // Before any window exists: every TextBox Copy/Cut goes through the protected clipboard.
                TextBoxClipboardGuard.Register(Clipboard, _dialogs);

                Shell = new ShellViewModel(
                    new NotebookStore(),
                    SettingsStore,
                    settings,
                    _dialogs,
                    Clipboard,
                    new SystemClock(),
                    () => KdfTuner.Argon2id(KdfTuner.DefaultTargetMilliseconds),
                    // "Open with" passes the double-clicked file: untrusted, it only pre-fills the first unlock page.
                    StartupArguments.NotebookPathFrom(e.Args));
                Shell.ShowUnlock();
                // Logoff/shutdown: the shell decides here, without dialogs. When allowed, WPF shuts down and closes
                // the window (Closing still runs, but its Cancel is ignored); the window is told to skip its long
                // clipboard retry so the logoff is not delayed.
                SessionEnding += OnSessionEnding;

                var window = new MainWindow { DataContext = Shell };
                MainWindow = window;
                window.Show();
            }
            catch (Exception ex)
            {
                // No window may exist yet: do not leave an invisible process behind.
                try { MessageBox.Show(DescribeUnhandled(ex), "LockPad could not start", MessageBoxButton.OK, MessageBoxImage.Error); }
                catch (Exception) { }
                Shutdown(1);
            }
        }

        // Best effort and silent. A terminating crash cannot be prevented, but the clipboard can
        // still be cleared. No logging anywhere.
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            if (!args.IsTerminating) return;
            try
            {
                Dispatcher dispatcher = Dispatcher;
                if (dispatcher.CheckAccess()) ClearClipboard();
                else dispatcher.Invoke(new Action(ClearClipboard), TimeSpan.FromSeconds(2), DispatcherPriority.Send);
            }
            catch (Exception) { }
        }

        private void ClearClipboard()
        {
            ClipboardService clipboard = Clipboard;
            if (clipboard != null) clipboard.ClearIfOwned();
        }

        /// <summary>
        /// Saves unsaved notes without dialogs; cancels the session end (Windows then shows that LockPad is preventing
        /// shutdown) when they could not be saved, keeping them in memory behind the lock screen.
        /// </summary>
        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            ShellViewModel shell = Shell;
            Func<bool> prepare = shell == null ? (Func<bool>)(() => true) : shell.PrepareForSessionEnd;
            if (!AllowSessionEnd(MainWindow as MainWindow, prepare)) e.Cancel = true;
        }

        /// <summary>
        /// The session-end decision. Refuses, without consulting the shell, while the window is in the middle of its
        /// own close (inside a dialog or the clipboard wait): WPF's shutdown would close it again and throw, skipping
        /// OnExit. An exception from <paramref name="prepareForSessionEnd"/> refuses too (never risk unsaved notes).
        /// When allowed, tells the window, which then skips its long clipboard retry.
        /// </summary>
        internal static bool AllowSessionEnd(MainWindow window, Func<bool> prepareForSessionEnd)
        {
            if (window != null && window.IsCloseInProgress) return false;
            bool allow;
            try
            {
                allow = prepareForSessionEnd();
            }
            catch (Exception)
            {
                allow = false;
            }
            if (allow && window != null) window.SessionEndAllowed = true;
            return allow;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Last resort, one attempt: MainWindow.Closing already retried for up to 20 s.
            if (Clipboard != null) Clipboard.ClearIfOwned();
            if (_themeWatcher != null) _themeWatcher.Dispose();
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Message only: never the stack trace, never note text. Keep running so the user can save.
            e.Handled = true;
            if (_gate.ShouldShow(DateTime.UtcNow))
            {
                _gate.Enter();
                try { _dialogs.ShowError("Unexpected error", DescribeUnhandled(e.Exception)); }
                catch (Exception) { }
                finally { _gate.Exit(); }
            }
            // Failing before the main window is up leaves nothing for the user to save.
            if (MainWindow == null || !MainWindow.IsLoaded) Shutdown(1);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// Called first thing at startup. Turns off KeePassLib's native code (LockPad never ships KeePassLibN, so a
        /// found copy can only be planted) and removes the current directory from the DLL search order with
        /// SetDllDirectory(""), so a DLL in whatever folder the process is in is never loaded by name.
        /// SetDefaultDllDirectories is deliberately not used: its effect on WPF's own native loads is untested.
        /// Returns false when SetDllDirectory failed; startup continues (the native KDF stays off either way).
        /// </summary>
        internal static bool HardenNativeLoading()
        {
            KeePassLibPolicy.Apply();
            return SetDllDirectory("");
        }

        /// <summary>The exception message, or the type name when the message is empty. Never the stack trace.</summary>
        internal static string DescribeUnhandled(Exception ex)
        {
            if (ex == null) return "Unknown error";
            string message = ex.Message;
            return string.IsNullOrEmpty(message) ? ex.GetType().Name : message;
        }
    }
}

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.App.Views;

namespace LockPad.App
{
    /// <summary>
    /// The single host window. Feeds input activity and a 1 s tick to the shell's idle lock, locks on Ctrl+L, when
    /// Windows locks, before sleep and (when the idle timeout passed) after waking, and closes only when the shell agrees
    /// (then clears the clipboard with retries).
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>Up to 20 s (user decision 2026-10-18): Win+V clipboard history has been seen holding the clipboard for
        /// about 15 s. The window is disabled meanwhile.</summary>
        internal static readonly TimeSpan ExitClipboardBudget = TimeSpan.FromSeconds(20);
        internal static readonly TimeSpan ExitClipboardInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>The starting size (2026-10-21; was 900 x 600): room for the Shortcuts window inside the main window.
        /// Shrunk to the work area on a small screen (e.g. 1920 x 1080 at 150 % is about 1280 x 690).</summary>
        internal const double DefaultWidth = 1000;
        internal const double DefaultHeight = 700;

        private readonly DispatcherTimer _lockTimer;
        private bool _systemEventsAttached;
        private bool _closeInProgress;
        private Point? _lastMousePosition;
        private bool _tickErrorReported;

        public MainWindow()
        {
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            Rect workArea = SystemParameters.WorkArea;
            Width = FitLength(DefaultWidth, workArea.Width, MinWidth);
            Height = FitLength(DefaultHeight, workArea.Height, MinHeight);
            // Started on Loaded (only a shown window ticks), stopped on Closed.
            _lockTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
            _lockTimer.Tick += OnLockTimerTick;
            PostToUi = action => Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }

        /// <summary><paramref name="preferred"/>, shrunk to <paramref name="available"/> but never below
        /// <paramref name="min"/>; an unusable <paramref name="available"/> (0, NaN) keeps <paramref name="preferred"/>.</summary>
        internal static double FitLength(double preferred, double available, double min)
        {
            if (!(available > 0)) return preferred;
            return Math.Max(min, Math.Min(preferred, available));
        }

        private ShellViewModel Shell
        {
            get { return DataContext as ShellViewModel; }
        }

        /// <summary>Ctrl+L locks (a no-op while locked). Plain Ctrl only: AltGr (Ctrl+Alt) + L types a character on
        /// some keyboard layouts. Returns true when the key was the shortcut.</summary>
        internal static bool HandleLockShortcut(ShellViewModel vm, Key key, ModifierKeys modifiers)
        {
            if (key != Key.L || modifiers != ModifierKeys.Control) return false;
            if (vm != null) vm.Lock();
            return true;
        }

        /// <summary>
        /// True when <paramref name="p"/> differs from the last mouse position seen. WPF raises synthetic mouse moves
        /// when the layout changes under a resting cursor (e.g. the countdown text); those are not user activity.
        /// </summary>
        internal bool IsNewMousePosition(Point p)
        {
            if (_lastMousePosition.HasValue && _lastMousePosition.Value == p) return false;
            _lastMousePosition = p;
            return true;
        }

        // ---- C12 (user decision 2026-10-18): input in LockPad's own dialogs counts as activity ----
        //
        // Keyboard and mouse input in a WPF window owned (directly or through another owned window) by the main window,
        // i.e. PromptWindow (Rename), SettingsWindow, ConfirmWindow (Delete note), ConfirmDefaultNoWindow, CloseChoiceWindow,
        // ShortcutsWindow,
        // AboutWindow (Help > About LockPad) and ChangePasswordWindow (File > Change master password), is recorded like the
        // main window's own input: key down, mouse down, mouse wheel and a real mouse move. It goes through
        // ShellViewModel.RecordActivity, so input after the idle deadline never extends the session: with the dialog open
        // the lock is deferred, so the editor is obscured and the clipboard cleared instead (SEC-H1), an obscured editor
        // is never revealed, and the lock follows when the dialog closes. The Win32 message boxes (ShowError, ShowWarning,
        // ShowInfo, the external-change question) and the OS file pickers are not WPF windows: their input cannot be
        // observed and does not count.

        private static readonly DependencyProperty LastDialogMousePositionProperty = DependencyProperty.RegisterAttached(
            "LastDialogMousePosition", typeof(Point?), typeof(MainWindow), new PropertyMetadata(null));

        static MainWindow()
        {
            // Class handlers on Window see every window's input first in the tunnel; filtered to owned dialogs below.
            EventManager.RegisterClassHandler(typeof(Window), PreviewKeyDownEvent, new KeyEventHandler(OnAnyWindowInput));
            EventManager.RegisterClassHandler(typeof(Window), PreviewMouseDownEvent, new MouseButtonEventHandler(OnAnyWindowInput));
            EventManager.RegisterClassHandler(typeof(Window), PreviewMouseWheelEvent, new MouseWheelEventHandler(OnAnyWindowInput));
            EventManager.RegisterClassHandler(typeof(Window), PreviewMouseMoveEvent, new MouseEventHandler(OnAnyWindowMouseMove));
        }

        /// <summary>The main window that owns <paramref name="window"/> through its Owner chain; null for the main window
        /// itself (it counts its own input) and for any window not owned by it.</summary>
        internal static MainWindow OwningMainWindow(Window window)
        {
            if (window == null || window is MainWindow) return null;
            for (Window owner = window.Owner; owner != null; owner = owner.Owner)
            {
                var main = owner as MainWindow;
                if (main != null) return main;
            }
            return null;
        }

        /// <summary>As <see cref="IsNewMousePosition"/>, tracked per dialog (positions are relative to each window).</summary>
        internal static bool IsNewDialogMousePosition(Window dialog, Point p)
        {
            var last = (Point?)dialog.GetValue(LastDialogMousePositionProperty);
            if (last.HasValue && last.Value == p) return false;
            dialog.SetValue(LastDialogMousePositionProperty, p);
            return true;
        }

        private static void OnAnyWindowInput(object sender, InputEventArgs e)
        {
            RecordDialogActivity(sender as Window);
        }

        private static void OnAnyWindowMouseMove(object sender, MouseEventArgs e)
        {
            var dialog = sender as Window;
            if (OwningMainWindow(dialog) == null) return;
            if (IsNewDialogMousePosition(dialog, e.GetPosition(dialog))) RecordDialogActivity(dialog);
        }

        /// <summary>
        /// Records the activity on the owning main window's shell. Only while the dialog service reports a dialog open:
        /// every LockPad dialog is shown through it, and that state makes the shell defer the lock, so this input can
        /// obscure but never tear down a view the dialog's caller may still use.
        /// </summary>
        private static void RecordDialogActivity(Window dialog)
        {
            MainWindow main = OwningMainWindow(dialog);
            if (main == null) return;
            ShellViewModel vm = main.Shell;
            if (vm == null || vm.Dialogs == null || !vm.Dialogs.IsShowingDialog) return;
            vm.RecordActivity();
        }

        // ---- Lock when Windows locks or the PC goes to sleep (user decision 2026-10-19; no setting) ----

        /// <summary>The longest the suspend handler waits for the UI thread to obscure the editor.</summary>
        internal static readonly TimeSpan SuspendObscureTimeout = TimeSpan.FromSeconds(1);

        /// <summary>Session lock (Win+L, Ctrl+Alt+Del > Lock, the lock screen after a timeout), switch user and a remote
        /// disconnect lock. Everything else does nothing; logoff is handled by the session-ending path.</summary>
        internal static SystemLockTrigger TriggerForSessionSwitch(SessionSwitchReason reason)
        {
            switch (reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                    return SystemLockTrigger.Lock;
                default:
                    return SystemLockTrigger.None;
            }
        }

        /// <summary>Suspend (sleep or hibernate) locks; Resume keeps the idle check on waking; StatusChange nothing.</summary>
        internal static SystemLockTrigger TriggerForPowerMode(PowerModes mode)
        {
            switch (mode)
            {
                case PowerModes.Suspend: return SystemLockTrigger.Suspend;
                case PowerModes.Resume: return SystemLockTrigger.Resume;
                default: return SystemLockTrigger.None;
            }
        }

        /// <summary>Queues work on the UI thread (BeginInvoke). Test seam.</summary>
        internal Action<Action> PostToUi { get; set; }

        /// <summary>(Un)subscribes this window's handlers to the static SystemEvents. Test seam.</summary>
        internal Action<MainWindow, bool> SystemEventsSubscriber { get; set; } = SubscribeSystemEvents;

        private static void SubscribeSystemEvents(MainWindow window, bool subscribe)
        {
            if (subscribe)
            {
                SystemEvents.PowerModeChanged += window.OnPowerModeChanged;
                SystemEvents.SessionSwitch += window.OnSessionSwitch;
            }
            else
            {
                SystemEvents.PowerModeChanged -= window.OnPowerModeChanged;
                SystemEvents.SessionSwitch -= window.OnSessionSwitch;
            }
        }

        /// <summary>Subscribes once (Loaded can be raised more than once).</summary>
        internal void AttachSystemEvents()
        {
            if (_systemEventsAttached) return;
            SystemEventsSubscriber(this, true);
            _systemEventsAttached = true;
        }

        /// <summary>Static events: an unremoved handler would keep this window alive and keep firing.</summary>
        internal void DetachSystemEvents()
        {
            if (!_systemEventsAttached) return;
            SystemEventsSubscriber(this, false);
            _systemEventsAttached = false;
        }

        /// <summary>
        /// Acts on a session or power event; may be called on any thread. Lock and Resume are queued to the UI thread.
        /// Suspend has little time before the machine sleeps, so it first obscures the editor and clears the clipboard
        /// synchronously (fast steps), waiting at most <see cref="SuspendObscureTimeout"/> for the UI thread, and then
        /// queues the full lock with its save. A save cut off by the sleep continues after resume (the atomic save
        /// pipeline cannot be damaged by that), and an obscured editor is locked by the first Tick after resume anyway.
        /// </summary>
        internal void HandleSystemTrigger(SystemLockTrigger trigger)
        {
            switch (trigger)
            {
                case SystemLockTrigger.Lock:
                    PostToShell(vm => vm.OnWindowsLocked());
                    break;
                case SystemLockTrigger.Resume:
                    PostToShell(vm => vm.OnResume());
                    break;
                case SystemLockTrigger.Suspend:
                    ObscureBeforeSuspend();
                    PostToShell(vm => vm.OnWindowsLocked());
                    break;
            }
        }

        private void PostToShell(Action<ShellViewModel> action)
        {
            try
            {
                PostToUi(() =>
                {
                    ShellViewModel vm = Shell;
                    if (vm != null) action(vm);
                });
            }
            catch (Exception)
            {
                // The dispatcher is shutting down: nothing left to lock.
            }
        }

        /// <summary>
        /// The synchronous suspend step. In practice it runs on the UI thread: SystemEvents creates its hidden window on
        /// the STA thread that first subscribes (this window's UI thread, in Loaded), so its handlers run there and the
        /// step runs inline, unbounded (accepted, red-team L1: the editor is obscured before the clipboard is cleared).
        /// The branch for any other thread is kept as a safeguard: it posts the step at Send priority and waits at most
        /// SuspendObscureTimeout, never deadlocking; when the wait times out, the step still runs before the queued lock.
        /// </summary>
        private void ObscureBeforeSuspend()
        {
            Action obscure = () =>
            {
                ShellViewModel vm = Shell;
                if (vm != null) vm.OnSuspending();
            };
            try
            {
                if (Dispatcher.CheckAccess())
                {
                    obscure();
                    return;
                }
                DispatcherOperation op = Dispatcher.BeginInvoke(DispatcherPriority.Send, obscure);
                op.Wait(SuspendObscureTimeout);
            }
            catch (Exception)
            {
                // Never throw into SystemEvents; the queued lock still follows.
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            HandleSystemTrigger(TriggerForSessionSwitch(e.Reason));
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            HandleSystemTrigger(TriggerForPowerMode(e.Mode));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _lockTimer.Start();
            AttachSystemEvents();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _lockTimer.Stop();
            DetachSystemEvents();
        }

        private void OnLockTimerTick(object sender, EventArgs e)
        {
            ShellViewModel vm = Shell;
            Exception error = RunLockTick(vm);
            if (error == null)
            {
                _tickErrorReported = false;
                return;
            }
            // Reported once until a tick succeeds again: the timer keeps firing every second.
            if (_tickErrorReported) return;
            _tickErrorReported = true;
            ReportLater(vm.Dialogs, error);
        }

        /// <summary>
        /// Runs the shell's 1 s tick and returns its exception instead of throwing it (re-attack 2026-10-22). A
        /// DispatcherTimer reschedules itself only after its Tick handlers return normally, so one throwing tick would
        /// stop the idle lock for the rest of the process. The lock's teardown runs every step before it rethrows
        /// (RunAll), so the lock itself has happened when an error comes back here.
        /// </summary>
        internal static Exception RunLockTick(ShellViewModel vm)
        {
            if (vm == null) return null;
            try
            {
                vm.Tick();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>Records activity; true when that activity arrived after the idle deadline and locked instead.</summary>
        private bool RecordActivityLocked()
        {
            return RecordActivityLocked(Shell);
        }

        /// <summary>Records activity; true when it arrived after the idle deadline, so the editor was locked away or
        /// hidden for the lock: the input must not reach it (re-attack 2026-10-22, High: a key that only hid the editor
        /// went on to the hidden text box, and the lock saved its edit).</summary>
        internal static bool RecordActivityLocked(ShellViewModel vm)
        {
            if (vm == null) return false;
            bool hadEditor = vm.Editor != null;
            vm.RecordActivity();
            return hadEditor && (vm.Editor == null || vm.Editor.IsObscured);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (RecordActivityLocked())
            {
                e.Handled = true; // the key must not reach the editor that was just locked away
                return;
            }
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HandleLockShortcut(Shell, key, Keyboard.Modifiers)) e.Handled = true;
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (RecordActivityLocked()) e.Handled = true;
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (RecordActivityLocked()) e.Handled = true;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (IsNewMousePosition(e.GetPosition(this))) RecordActivityLocked();
        }

        /// <summary>
        /// Set by the application once it allowed a logoff/shutdown. WPF then closes this window while shutting down
        /// (Closing still runs, its Cancel is ignored); the clipboard gets a single clear instead of the up-to-20-s retry
        /// loop, so the logoff is not delayed.
        /// </summary>
        internal bool SessionEndAllowed { get; set; }

        /// <summary>
        /// True for the whole of <see cref="HandleClosing"/>, including the nested message loops it may run (a save
        /// dialog, the held-notebook Confirm, the clipboard retry wait). A session end must not be allowed then: WPF's
        /// shutdown would try to close this window again and throw.
        /// </summary>
        internal bool IsCloseInProgress
        {
            get { return _closeInProgress; }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            HandleClosing(e);
        }

        /// <summary>
        /// The Closing logic. Never throws: Window.WmClose destroys the window when an exception escapes the Closing
        /// handler (its Cancel is then never read), so an unexpected failure cancels the close and is reported
        /// asynchronously, after the close request has returned.
        /// </summary>
        internal void HandleClosing(CancelEventArgs e)
        {
            if (_closeInProgress)
            {
                // A second close request arriving inside the first one's dialog or clipboard wait.
                e.Cancel = true;
                return;
            }
            _closeInProgress = true;
            try
            {
                HandleClosingCore(e);
            }
            finally
            {
                _closeInProgress = false;
            }
        }

        private void HandleClosingCore(CancelEventArgs e)
        {
            ShellViewModel vm = Shell;
            if (vm == null) return;

            bool canClose;
            try
            {
                canClose = vm.TryClose();
            }
            catch (Exception ex)
            {
                e.Cancel = true; // keep the window (and any unsaved notes) rather than close on an unknown state
                ReportLater(vm.Dialogs, ex);
                return;
            }
            if (!canClose)
            {
                e.Cancel = true;
                return;
            }

            IClipboardService clipboard = vm.Clipboard;
            if (clipboard == null || !clipboard.OwnsClipboardContent) return;
            if (SessionEndAllowed)
            {
                clipboard.ClearIfOwned(); // never throws
                return;
            }
            IsEnabled = false;
            try
            {
                ClipboardExitHelper.ClearWithRetry(clipboard, ExitClipboardBudget, ExitClipboardInterval, PumpingWait);
            }
            catch (Exception)
            {
                // Never block closing; App.OnExit clears once more.
            }
        }

        /// <summary>Message only, never a stack trace or note text (same as the app's unhandled-error dialog).</summary>
        private void ReportLater(IDialogService dialogs, Exception ex)
        {
            if (dialogs == null) return;
            string message = App.DescribeUnhandled(ex);
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try { dialogs.ShowError("Unexpected error", message); }
                    catch (Exception) { }
                }));
            }
            catch (Exception)
            {
                // The dispatcher is shutting down: nothing to report to.
            }
        }

        /// <summary>Waits while the dispatcher keeps running (clipboard window messages, timers), unlike Thread.Sleep.</summary>
        internal static void PumpingWait(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = duration };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        /// <summary>File > Exit: the normal close (Closing, then HandleClosing and the shell's TryClose).</summary>
        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnFindCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = FindEditorView(this) != null;
            e.Handled = true;
        }

        private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            EditorView view = FindEditorView(this);
            if (view == null) return;
            view.ShowFind();
            e.Handled = true;
        }

        private static EditorView FindEditorView(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                var view = child as EditorView;
                if (view != null) return view;
                view = FindEditorView(child);
                if (view != null) return view;
            }
            return null;
        }
    }
}

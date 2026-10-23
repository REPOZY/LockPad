using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LockPad.App.Services
{
    /// <summary>The one user32 call capture protection makes. Test seam: tests use a fake, so they never call the real one.</summary>
    internal interface IDisplayAffinityApi
    {
        /// <summary>SetWindowDisplayAffinity; true when Windows accepted it.</summary>
        bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    }

    /// <summary>
    /// Keeps LockPad's windows out of screenshots, screen recordings and screen sharing (user decision 2026-10-21, on by
    /// default; Settings > "Hide LockPad from screenshots and screen sharing"). Every LockPad window calls
    /// <see cref="Attach"/> in its constructor, so it asks when its HWND is created (SourceInitialized, before it is first
    /// shown): WDA_EXCLUDEFROMCAPTURE while on (the window does not appear in captures at all; Windows before 10 version
    /// 2004 treat it as WDA_MONITOR, a black box), WDA_NONE while off. If Windows refuses WDA_EXCLUDEFROMCAPTURE,
    /// WDA_MONITOR is tried. The shell re-applies it to every open window when the setting changes.
    /// <para>
    /// Not a guarantee (Microsoft: "unlike a security feature or an implementation of DRM, there is no guarantee"): it
    /// covers capture through the public Windows capture APIs (Snipping Tool, screen sharing and recording apps), not a
    /// phone camera or malware on the PC (UI Automation can read the text without capturing anything). Not covered,
    /// because they are separate windows LockPad does not create as a Window: WPF popups (menus, drop-down lists,
    /// tooltips), Win32 message boxes (errors and warnings: file paths and error text), the Open/Save file dialogs, and
    /// IME candidate lists and Windows text suggestions (which show what is typed). None of the first three shows note
    /// text; confirmations, which can show a note title, use ConfirmWindow. Capture of a Remote Desktop, VM or Citrix
    /// session on the connecting computer, and virtual displays or Miracast, are outside Windows' protection. Best effort:
    /// a refusal, a missing export or any exception is ignored and never keeps a window from opening.
    /// </para>
    /// </summary>
    internal static class CaptureProtection
    {
        internal const uint WdaNone = 0x0;
        internal const uint WdaMonitor = 0x1;
        internal const uint WdaExcludeFromCapture = 0x11;

        private static bool _isOn = true;

        /// <summary>The user32 call. Null does nothing: App startup sets the real one, tests set a fake.</summary>
        internal static IDisplayAffinityApi Api { get; set; }

        /// <summary>The value last set; on until startup reads the settings, so a window is never unprotected by default.</summary>
        internal static bool IsOn
        {
            get { return _isOn; }
        }

        /// <summary>A window's HWND, zero before it exists. Test seam: tests map windows to fake handles.</summary>
        internal static Func<Window, IntPtr> HandleOf { get; set; } = w => new WindowInteropHelper(w).Handle;

        /// <summary>Applies the current value to <paramref name="window"/> once its HWND exists.</summary>
        internal static void Attach(Window window)
        {
            if (window == null) return;
            window.SourceInitialized += (s, e) => Apply(s as Window);
        }

        /// <summary>Records <paramref name="on"/> and applies it to every window in <paramref name="windows"/> that already
        /// has an HWND (the others apply it on SourceInitialized). Never throws.</summary>
        internal static void SetEnabled(bool on, IEnumerable windows)
        {
            _isOn = on;
            if (windows == null) return;
            var list = new List<Window>();
            try
            {
                foreach (object o in windows)
                {
                    var w = o as Window;
                    if (w != null) list.Add(w);
                }
            }
            catch (Exception) { }
            foreach (Window w in list) Apply(w);
        }

        /// <summary>The shell's default: <see cref="SetEnabled"/> for every window of the running app (only the value is
        /// recorded when there is no Application, as in tests).</summary>
        internal static void ApplyToApp(bool on)
        {
            Application app = Application.Current;
            WindowCollection windows = null;
            try
            {
                if (app != null) windows = app.Windows;
            }
            catch (Exception) { }
            SetEnabled(on, windows);
        }

        internal static void Apply(Window window)
        {
            IDisplayAffinityApi api = Api;
            if (api == null || window == null) return;
            IntPtr hwnd;
            try
            {
                hwnd = HandleOf(window);
            }
            catch (Exception)
            {
                return;
            }
            if (hwnd == IntPtr.Zero) return;
            Apply(hwnd, _isOn, api);
        }

        /// <summary>On: WDA_EXCLUDEFROMCAPTURE, then WDA_MONITOR if that is refused or throws. Off: WDA_NONE. Never throws.</summary>
        internal static void Apply(IntPtr hwnd, bool on, IDisplayAffinityApi api)
        {
            if (api == null) return;
            if (!on)
            {
                try { api.SetWindowDisplayAffinity(hwnd, WdaNone); }
                catch (Exception) { }
                return;
            }
            try
            {
                if (api.SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture)) return;
            }
            catch (Exception) { }
            try { api.SetWindowDisplayAffinity(hwnd, WdaMonitor); }
            catch (Exception) { }
        }
    }

    /// <summary>The real call. This import is pinned to System32 (as in DarkTitleBar and ErrorReporting).</summary>
    internal sealed class NativeDisplayAffinityApi : IDisplayAffinityApi
    {
        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        bool IDisplayAffinityApi.SetWindowDisplayAffinity(IntPtr hwnd, uint affinity)
        {
            return SetWindowDisplayAffinity(hwnd, affinity);
        }
    }
}

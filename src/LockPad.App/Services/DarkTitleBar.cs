using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LockPad.App.Services
{
    /// <summary>The one DWM call LockPad makes. Test seam: tests use a fake, so they never call the real DWM.</summary>
    internal interface IDwmApi
    {
        /// <summary>DwmSetWindowAttribute with a 4-byte integer value; returns its HRESULT.</summary>
        int SetWindowAttribute(IntPtr hwnd, int attribute, int value);
    }

    /// <summary>
    /// Dark title bars in the Dark theme. WPF draws only the client area; the title bar belongs to Windows and stays light
    /// unless the window asks DWM for DWMWA_USE_IMMERSIVE_DARK_MODE. Every LockPad window calls <see cref="Attach"/> in
    /// its constructor, so it asks when its HWND is created (SourceInitialized, before it is first shown): 1 when the
    /// effective theme is dark (Dark, or System while Windows apps use dark mode), 0 when it is light. ThemeService
    /// re-applies it to every open window when the theme changes. Attribute 20 is tried first; Windows 10 builds before
    /// 20H1 only know 19. Best effort: a failure HRESULT, a missing DLL or export, or any exception is ignored and never
    /// keeps a window from opening.
    /// </summary>
    internal static class DarkTitleBar
    {
        /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE (dwmapi.h, Windows 10 20H1 and later).</summary>
        internal const int UseImmersiveDarkMode = 20;

        /// <summary>The same attribute's number on Windows 10 1809 to 1909.</summary>
        internal const int UseImmersiveDarkModeBefore20H1 = 19;

        /// <summary>The DWM calls. Null does nothing: App startup sets the real one, tests set a fake.</summary>
        internal static IDwmApi Api { get; set; }

        /// <summary>The effective theme ThemeService last applied.</summary>
        internal static bool IsDark { get; private set; }

        /// <summary>A window's HWND, zero before it exists. Test seam: tests map windows to fake handles, so they create
        /// no real HWNDs.</summary>
        internal static Func<Window, IntPtr> HandleOf { get; set; } = w => new WindowInteropHelper(w).Handle;

        /// <summary>Applies the current theme to <paramref name="window"/> once its HWND exists.</summary>
        internal static void Attach(Window window)
        {
            if (window == null) return;
            window.SourceInitialized += (s, e) => Apply(s as Window);
        }

        /// <summary>Records the effective theme and applies it to every window in <paramref name="windows"/> that already
        /// has an HWND (the others apply it on SourceInitialized).</summary>
        internal static void SetTheme(bool dark, IEnumerable windows)
        {
            IsDark = dark;
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

        internal static void Apply(Window window)
        {
            IDwmApi api = Api;
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
            Apply(hwnd, IsDark, api);
        }

        /// <summary>Attribute 20; when that fails (failure HRESULT or exception), attribute 19. Never throws.</summary>
        internal static void Apply(IntPtr hwnd, bool dark, IDwmApi api)
        {
            if (api == null) return;
            int value = dark ? 1 : 0;
            try
            {
                if (api.SetWindowAttribute(hwnd, UseImmersiveDarkMode, value) >= 0) return;
            }
            catch (Exception) { }
            try { api.SetWindowAttribute(hwnd, UseImmersiveDarkModeBefore20H1, value); }
            catch (Exception) { }
        }
    }

    /// <summary>The real call. This import is pinned to System32 (as in ErrorReporting); WPF's own earlier load of dwmapi.dll is outside this pin.</summary>
    internal sealed class NativeDwmApi : IDwmApi
    {
        [DllImport("dwmapi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        public int SetWindowAttribute(IntPtr hwnd, int attribute, int value)
        {
            return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        }
    }
}

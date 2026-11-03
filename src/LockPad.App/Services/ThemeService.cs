using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LockPad.App.Services
{
    public enum AppTheme { System, Light, Dark }

    /// <summary>
    /// Applies the theme: a colour dictionary (Themes/Light.xaml or Themes/Dark.xaml) plus the control templates
    /// (Themes/Controls.xaml), merged into the application's resources. In Windows high contrast neither is merged: the
    /// system styles render, and the theme's brush keys (which the views use) map to the system colours instead.
    /// </summary>
    public static class ThemeService
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string LightPath = "Themes/Light.xaml";
        private const string DarkPath = "Themes/Dark.xaml";
        private const string ControlsPath = "Themes/Controls.xaml";
        private const string PackPrefix = "pack://application:,,,/LockPad;component/";

        /// <summary>Marks the code-built high-contrast colour dictionary, so it is replaced like the theme files.</summary>
        internal const string HighContrastMarkerKey = "LockPad.HighContrastColours";

        /// <summary>Whether Windows high contrast is on. Test seam (SystemParameters.HighContrast).</summary>
        internal static Func<bool> HighContrast { get; set; } = () => SystemParameters.HighContrast;

        /// <summary>The effective theme (dark or not) last applied; re-applied when high contrast changes.</summary>
        private static bool? _lastDark;

        /// <summary>The system colours the high-contrast dictionary was last built from; null in normal contrast.</summary>
        private static Color[] _lastSystemColours;

        /// <summary>The colours of every system brush the high-contrast map uses, in map order. Test seam.</summary>
        internal static Func<Color[]> ReadSystemColours { get; set; } = CurrentSystemColours;

        /// <summary>
        /// The theme's brush keys in high contrast: each maps to the system colour of the same role, so the views keep
        /// working with Windows' own colours. Covers every key of Light.xaml and Dark.xaml (a test checks it).
        /// </summary>
        private static readonly KeyValuePair<string, Func<Brush>>[] HighContrastBrushes =
        {
            Map("WindowBackground", () => SystemColors.WindowBrush),
            Map("PanelBackground", () => SystemColors.WindowBrush),
            Map("EditorBackground", () => SystemColors.WindowBrush),
            Map("EditorForeground", () => SystemColors.WindowTextBrush),
            Map("AccentBrush", () => SystemColors.HighlightBrush),
            Map("BorderBrush", () => SystemColors.WindowTextBrush),
            Map("MutedForeground", () => SystemColors.WindowTextBrush),
            Map("ErrorForeground", () => SystemColors.WindowTextBrush),
            Map("WarningBackground", () => SystemColors.WindowBrush),
            Map("EditorSelectionBrush", () => SystemColors.HighlightBrush),
            Map("DialogBorder", () => SystemColors.WindowTextBrush),
            Map("TransparentBrush", () => Brushes.Transparent),
            Map("ControlBackground", () => SystemColors.ControlBrush),
            Map("ControlHoverBackground", () => SystemColors.ControlBrush),
            Map("ControlPressedBackground", () => SystemColors.ControlBrush),
            Map("ControlBorder", () => SystemColors.ControlTextBrush),
            Map("ControlDisabledBackground", () => SystemColors.ControlBrush),
            Map("ControlDisabledBorder", () => SystemColors.GrayTextBrush),
            Map("DisabledForeground", () => SystemColors.GrayTextBrush),
            Map("DefaultButtonBorder", () => SystemColors.HighlightBrush),
            Map("FocusBorderBrush", () => SystemColors.HighlightBrush),
            Map("PrimaryBackground", () => SystemColors.ControlBrush),
            Map("PrimaryHoverBackground", () => SystemColors.ControlBrush),
            Map("PrimaryPressedBackground", () => SystemColors.ControlBrush),
            Map("PrimaryForeground", () => SystemColors.ControlTextBrush),
            Map("PopupBackground", () => SystemColors.MenuBrush),
            Map("PopupBorder", () => SystemColors.MenuTextBrush),
            Map("HighlightBackground", () => SystemColors.HighlightBrush),
            Map("CheckMarkBrush", () => SystemColors.ControlTextBrush),
            Map("ScrollBarTrack", () => SystemColors.ScrollBarBrush),
            Map("ScrollBarThumb", () => SystemColors.ControlTextBrush),
            Map("ScrollBarThumbHover", () => SystemColors.HighlightBrush),
            Map("ScrollBarArrow", () => SystemColors.ControlTextBrush),
            Map("ProgressTrack", () => SystemColors.ControlBrush),
            Map("ProgressFill", () => SystemColors.HighlightBrush),
            Map("StrengthWeakBrush", () => SystemColors.HighlightBrush),
            Map("StrengthFairBrush", () => SystemColors.HighlightBrush),
            Map("StrengthStrongBrush", () => SystemColors.HighlightBrush),
            Map("ToolTipBackground", () => SystemColors.InfoBrush),
            Map("ToolTipForeground", () => SystemColors.InfoTextBrush),
            Map("ToolTipBorder", () => SystemColors.InfoTextBrush),
        };

        private static KeyValuePair<string, Func<Brush>> Map(string key, Func<Brush> brush)
        {
            return new KeyValuePair<string, Func<Brush>>(key, brush);
        }

        private static Color[] CurrentSystemColours()
        {
            var colours = new Color[HighContrastBrushes.Length];
            for (int i = 0; i < colours.Length; i++)
            {
                var solid = HighContrastBrushes[i].Value() as SolidColorBrush;
                colours[i] = solid == null ? Colors.Transparent : solid.Color;
            }
            return colours;
        }

        private static bool SameColours(Color[] a, Color[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>AppsUseLightTheme: 0 means dark; 1, missing or non-int means light.</summary>
        public static bool SystemPrefersDark(object appsUseLightThemeValue)
        {
            return appsUseLightThemeValue is int && (int)appsUseLightThemeValue == 0;
        }

        /// <summary>Parses a settings theme name; anything unrecognised is System.</summary>
        public static AppTheme Parse(string name)
        {
            AppTheme theme;
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out theme)
                && Enum.IsDefined(typeof(AppTheme), theme))
                return theme;
            return AppTheme.System;
        }

        public static void Apply(AppTheme theme)
        {
            Application app = Application.Current;
            if (app == null) return;

            ApplyResolved(app.Resources.MergedDictionaries, app.Windows, ResolveDark(theme, ReadAppsUseLightTheme));
        }

        /// <summary>True for Dark, and for System while Windows apps use dark mode. The system setting is read only for
        /// System.</summary>
        internal static bool ResolveDark(AppTheme theme, Func<object> readAppsUseLightTheme)
        {
            if (theme == AppTheme.Dark) return true;
            if (theme != AppTheme.System) return false;
            return SystemPrefersDark(readAppsUseLightTheme());
        }

        /// <summary>Swaps the theme dictionaries (normal or high contrast, see <see cref="HighContrast"/>) and applies the
        /// matching title bar to every open window (DarkTitleBar; never dark in high contrast).</summary>
        internal static void ApplyResolved(Collection<ResourceDictionary> merged, IEnumerable windows, bool dark)
        {
            bool highContrast = HighContrast();
            _lastDark = dark;
            _lastSystemColours = highContrast ? ReadSystemColours() : null;
            ApplyTo(merged, dark, highContrast);
            DarkTitleBar.SetTheme(dark && !highContrast, windows);
        }

        /// <summary>High contrast was turned on or off: re-applies the theme last applied, with the new contrast mode.
        /// Nothing before the first theme was applied.</summary>
        internal static void ReapplyForHighContrast(Collection<ResourceDictionary> merged, IEnumerable windows)
        {
            if (_lastDark.HasValue) ApplyResolved(merged, windows, _lastDark.Value);
        }

        /// <summary>Called by the application (SystemThemeWatcher) when SystemParameters.HighContrast changes.</summary>
        public static void OnHighContrastChanged()
        {
            Application app = Application.Current;
            if (app == null) return;
            ReapplyForHighContrast(app.Resources.MergedDictionaries, app.Windows);
        }

        /// <summary>While high contrast is on and the system colours differ from the ones the dictionary was built
        /// from (Windows switched from one high-contrast scheme to another), rebuilds the theme keys from them and
        /// updates the title bars; true when it did. Otherwise nothing (fix round 2, Y4: unchanged colours, e.g. a
        /// General or VisualStyle notice, swap nothing; normal contrast: the theme files do not depend on the system
        /// colours). Nothing before the first theme was applied.</summary>
        internal static bool ReapplyIfHighContrast(Collection<ResourceDictionary> merged, IEnumerable windows)
        {
            if (!_lastDark.HasValue || !HighContrast()) return false;
            if (SameColours(ReadSystemColours(), _lastSystemColours)) return false;
            ApplyResolved(merged, windows, _lastDark.Value);
            return true;
        }

        /// <summary>Called by the application (SystemThemeWatcher, already queued to the UI thread) when Windows'
        /// colours or visual style change.</summary>
        public static void OnSystemColoursChanged()
        {
            Application app = Application.Current;
            if (app == null) return;
            Collection<ResourceDictionary> merged = app.Resources.MergedDictionaries;
            System.Windows.Threading.Dispatcher dispatcher = app.Dispatcher;
            OnSystemColoursChanged(merged, app.Windows,
                a => dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, a));
        }

        /// <summary>Re-applies now if the colours changed, and, while high contrast is on, queues ONE more check with
        /// <paramref name="post"/> (red team L4b: the notice may arrive before every system colour is updated; the
        /// snapshot compare makes the check cheap). The queued check never throws and queues nothing further.</summary>
        internal static void OnSystemColoursChanged(Collection<ResourceDictionary> merged, IEnumerable windows,
            Action<Action> post)
        {
            ReapplyIfHighContrast(merged, windows);
            if (!_lastDark.HasValue || !HighContrast()) return;
            post(() =>
            {
                try { ReapplyIfHighContrast(merged, windows); }
                catch (Exception) { }
            });
        }

        /// <summary>As <see cref="ApplyTo(Collection{ResourceDictionary}, bool, bool)"/> in normal contrast.</summary>
        internal static void ApplyTo(Collection<ResourceDictionary> merged, bool dark)
        {
            ApplyTo(merged, dark, false);
        }

        /// <summary>
        /// Replaces only the theme dictionaries in <paramref name="merged"/>; other dictionaries are untouched. Normal
        /// contrast: the colour file, then the control templates. High contrast: only the system-colour mapping of the
        /// theme's brush keys; no control templates, so Windows' own styles render.
        /// </summary>
        internal static void ApplyTo(Collection<ResourceDictionary> merged, bool dark, bool highContrast)
        {
            ResourceDictionary colours = highContrast ? HighContrastColours() : Load(dark ? DarkPath : LightPath);
            ResourceDictionary controls = highContrast ? null : Load(ControlsPath);
            for (int i = merged.Count - 1; i >= 0; i--)
                if (IsThemeDictionary(merged[i])) merged.RemoveAt(i);
            merged.Add(colours);
            if (controls != null) merged.Add(controls);
        }

        private static ResourceDictionary Load(string path)
        {
            return new ResourceDictionary { Source = new Uri(PackPrefix + path, UriKind.Absolute) };
        }

        /// <summary>The theme's brush keys mapped to the current system colours (read when this is built).</summary>
        internal static ResourceDictionary HighContrastColours()
        {
            var dict = new ResourceDictionary();
            dict[HighContrastMarkerKey] = true;
            foreach (KeyValuePair<string, Func<Brush>> entry in HighContrastBrushes) dict[entry.Key] = entry.Value();
            return dict;
        }

        private static bool IsThemeDictionary(ResourceDictionary d)
        {
            if (d.Contains(HighContrastMarkerKey)) return true;
            Uri source = d.Source;
            if (source == null) return false;
            string s = source.OriginalString;
            return s.EndsWith(LightPath, StringComparison.OrdinalIgnoreCase)
                || s.EndsWith(DarkPath, StringComparison.OrdinalIgnoreCase)
                || s.EndsWith(ControlsPath, StringComparison.OrdinalIgnoreCase);
        }

        private static object ReadAppsUseLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                    return key == null ? null : key.GetValue("AppsUseLightTheme");
            }
            catch (SecurityException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}

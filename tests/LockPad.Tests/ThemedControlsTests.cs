using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using LockPad.App;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// 2026-10-21 visual polish: themed control templates (both themes, high-contrast fallback), primary buttons, the
    /// Create / Cancel order, the strength meter and the editor's empty states.
    /// </summary>
    [TestFixture]
    public class ThemedControlsTests
    {
        private const string Pack = "pack://application:,,,/LockPad;component/Themes/";

        private TempDir _dir;
        private readonly List<Notebook> _notebooks = new List<Notebook>();

        [SetUp]
        public void SetUp()
        {
            RegisterPackScheme();
            _dir = new TempDir();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _notebooks) nb.Dispose();
            _notebooks.Clear();
            _dir.Dispose();
        }

        private static void RegisterPackScheme()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            GC.KeepAlive(Application.Current);
        }

        private static ResourceDictionary Load(string name)
        {
            return new ResourceDictionary { Source = new Uri(Pack + name + ".xaml", UriKind.Absolute) };
        }

        private static string AppDir()
        {
            DirectoryInfo d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "LockPad.sln"))) d = d.Parent;
            Assert.That(d, Is.Not.Null);
            return Path.Combine(d.FullName, "src", "LockPad.App");
        }

        /// <summary>The theme's colours and the control styles, as ThemeService merges them (normal contrast).</summary>
        private static void ApplyTheme(FrameworkElement root, bool dark)
        {
            ThemeService.ApplyTo(root.Resources.MergedDictionaries, dark, false);
        }

        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        private static void Layout(FrameworkElement e)
        {
            if (e is Window)
            {
                e.ApplyTemplate();
                e = (FrameworkElement)VisualTreeHelper.GetChild(e, 0);
            }
            e.Measure(new Size(900, 700));
            e.Arrange(new Rect(0, 0, 900, 700));
            e.UpdateLayout();
            Pump();
        }

        private static Color Colour(Brush b)
        {
            Assert.That(b, Is.InstanceOf<SolidColorBrush>());
            return ((SolidColorBrush)b).Color;
        }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes("pw"), null);
        }

        private Notebook NewNotebook(params string[] titles)
        {
            string path = _dir.File("nb" + _notebooks.Count + ".kdbx");
            using (new NotebookStore().Create(path, Pw(), TestKdf.Fast())) { }
            Notebook nb = new NotebookStore().Open(path, Pw());
            _notebooks.Add(nb);
            foreach (string t in titles) nb.AddNote(t);
            return nb;
        }

        private static SecureString Secure(string s)
        {
            return UnlockViewModelTests.Secure(s);
        }

        // ---- the style dictionary ----

        private static readonly object[] StyleKeys =
        {
            typeof(Button), "PrimaryButton", typeof(ComboBox), typeof(ComboBoxItem), typeof(CheckBox),
            typeof(ProgressBar), "StrengthMeter", typeof(ScrollBar), typeof(ToolTip), typeof(Menu), typeof(MenuItem),
            typeof(Separator), MenuItem.SeparatorStyleKey, "FocusVisual",
        };

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ControlsDictionary_DefinesEveryStyle()
        {
            ResourceDictionary controls = Load("Controls");
            foreach (object key in StyleKeys)
            {
                Assert.That(controls.Contains(key), Is.True, "missing style " + key);
                Assert.That(controls[key], Is.InstanceOf<Style>(), key.ToString());
            }
            Assert.That(((Style)controls["PrimaryButton"]).BasedOn, Is.SameAs(controls[typeof(Button)]),
                "Primary keeps every state of the themed button");
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [Apartment(ApartmentState.STA)]
        public void EveryBrushTheStylesUse_ExistsInTheTheme_AndBothThemesDefineTheSameKeys(string theme)
        {
            string xaml = File.ReadAllText(Path.Combine(AppDir(), "Themes", "Controls.xaml"));
            string[] used = Regex.Matches(xaml, @"\{DynamicResource\s+(\w+)\}").Cast<Match>()
                .Select(m => m.Groups[1].Value).Distinct().ToArray();
            Assert.That(used.Length, Is.GreaterThan(20), "the styles use theme brushes");
            ResourceDictionary dict = Load(theme);
            foreach (string key in used)
            {
                Assert.That(dict.Contains(key), Is.True, theme + " is missing " + key);
                Assert.That(dict[key], Is.InstanceOf<SolidColorBrush>(), key);
            }

            string[] light = Load("Light").Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k).ToArray();
            string[] dark = Load("Dark").Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k).ToArray();
            Assert.That(dark, Is.EqualTo(light));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ControlsXaml_ReferencesNoBrushByStaticResource()
        {
            string xaml = File.ReadAllText(Path.Combine(AppDir(), "Themes", "Controls.xaml"));
            ResourceDictionary theme = Load("Dark");
            foreach (Match m in Regex.Matches(xaml, @"\{StaticResource\s+(\w+)\}"))
                Assert.That(theme.Contains(m.Groups[1].Value), Is.False,
                    "a theme brush must be a DynamicResource (the theme changes live): " + m.Value);
        }

        // ---- contrast (WCAG 2 relative luminance) ----

        private static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static double Luminance(Color c)
        {
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        internal static double ContrastRatio(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        [Test]
        public void ContrastRatio_KnownValues()
        {
            Assert.That(ContrastRatio(Colors.Black, Colors.White), Is.EqualTo(21.0).Within(0.01));
            Assert.That(ContrastRatio(Colors.White, Colors.White), Is.EqualTo(1.0).Within(0.001));
            Assert.That(ContrastRatio(Color.FromRgb(0x76, 0x76, 0x76), Colors.White), Is.EqualTo(4.54).Within(0.01));
        }

        /// <summary>Every text/background pair the templates and views draw (4.5:1), and every focus indicator and
        /// meter fill against what it is drawn on (3:1).</summary>
        private static readonly string[][] TextPairs =
        {
            new[] { "EditorForeground", "WindowBackground" },
            new[] { "EditorForeground", "PanelBackground" },
            new[] { "EditorForeground", "EditorBackground" },
            new[] { "EditorForeground", "WarningBackground" },
            new[] { "EditorForeground", "ControlBackground" },
            new[] { "EditorForeground", "ControlHoverBackground" },
            new[] { "EditorForeground", "ControlPressedBackground" },
            new[] { "EditorForeground", "PopupBackground" },
            new[] { "EditorForeground", "HighlightBackground" },
            new[] { "MutedForeground", "WindowBackground" },
            new[] { "MutedForeground", "PanelBackground" },
            new[] { "MutedForeground", "EditorBackground" },
            new[] { "MutedForeground", "PopupBackground" },
            new[] { "ErrorForeground", "WindowBackground" },
            new[] { "ErrorForeground", "PanelBackground" },
            new[] { "DisabledForeground", "ControlDisabledBackground" },
            new[] { "DisabledForeground", "PopupBackground" },
            new[] { "DisabledForeground", "WindowBackground" },
            new[] { "DisabledForeground", "PanelBackground" },
            new[] { "PrimaryForeground", "PrimaryBackground" },
            new[] { "PrimaryForeground", "PrimaryHoverBackground" },
            new[] { "PrimaryForeground", "PrimaryPressedBackground" },
            new[] { "ToolTipForeground", "ToolTipBackground" },
        };

        private static readonly string[][] NonTextPairs =
        {
            new[] { "FocusBorderBrush", "WindowBackground" },
            new[] { "FocusBorderBrush", "PanelBackground" },
            new[] { "FocusBorderBrush", "EditorBackground" },
            new[] { "FocusBorderBrush", "PopupBackground" },
            new[] { "FocusBorderBrush", "WarningBackground" },
            new[] { "ProgressFill", "ProgressTrack" },
            new[] { "StrengthWeakBrush", "ProgressTrack" },
            new[] { "StrengthFairBrush", "ProgressTrack" },
            new[] { "StrengthStrongBrush", "ProgressTrack" },
            new[] { "CheckMarkBrush", "ControlBackground" },
        };

        [TestCase("Light")]
        [TestCase("Dark")]
        [Apartment(ApartmentState.STA)]
        public void Contrast_EveryThemePairMeetsWcag(string theme)
        {
            ResourceDictionary dict = Load(theme);
            var failures = new List<string>();
            foreach (KeyValuePair<string[][], double> group in new Dictionary<string[][], double>
            {
                { TextPairs, 4.5 },
                { NonTextPairs, 3.0 },
            })
            {
                foreach (string[] pair in group.Key)
                {
                    Color fg = ((SolidColorBrush)dict[pair[0]]).Color;
                    Color bg = ((SolidColorBrush)dict[pair[1]]).Color;
                    Assert.That(fg.A, Is.EqualTo(255), pair[0] + " is opaque");
                    Assert.That(bg.A, Is.EqualTo(255), pair[1] + " is opaque");
                    double ratio = ContrastRatio(fg, bg);
                    if (ratio < group.Value)
                        failures.Add(pair[0] + " on " + pair[1] + ": " + ratio.ToString("0.00") + " < " + group.Value);
                }
            }
            Assert.That(failures, Is.Empty, theme);
        }

        // ---- every brush a trigger in Controls.xaml sets, against what it is drawn on ----

        private const double Text = 4.5;
        private const double Graphic = 3.0;

        /// <summary>One brush a trigger sets: the pairs it forms (foreground key, background key, minimum ratio), or why
        /// WCAG does not apply.</summary>
        private sealed class TriggerBrush
        {
            public readonly object[][] Pairs;
            public readonly string Exempt;

            private TriggerBrush(object[][] pairs, string exempt)
            {
                Pairs = pairs;
                Exempt = exempt;
            }

            public static TriggerBrush Of(params object[][] pairs)
            {
                return new TriggerBrush(pairs, null);
            }

            public static TriggerBrush ExemptBecause(string reason)
            {
                return new TriggerBrush(new object[0][], reason);
            }
        }

        private static object[] P(string fg, string bg, double min)
        {
            return new object[] { fg, bg, min };
        }

        private const string DisabledBorder = "a disabled control's border: WCAG 1.4.11 exempts inactive components";

        /// <summary>
        /// Keyed "owner [trigger] TargetName.Property = brush key". Owner: the keyed style or template, else the implicit
        /// style's target type. The keyboard ring is checked against the surface around the item (menu bar: Panel,
        /// drop-down: Popup) AND against the highlight inside it.
        /// </summary>
        private static readonly Dictionary<string, TriggerBrush> TriggerBrushes = new Dictionary<string, TriggerBrush>
        {
            { "Button [IsDefaulted=True] Chrome.BorderBrush = DefaultButtonBorder", TriggerBrush.Of(
                P("DefaultButtonBorder", "WindowBackground", Graphic), P("DefaultButtonBorder", "PanelBackground", Graphic),
                P("DefaultButtonBorder", "ControlBackground", Graphic)) },
            { "Button [IsMouseOver=True] Chrome.Background = ControlHoverBackground", TriggerBrush.Of(
                P("EditorForeground", "ControlHoverBackground", Text)) },
            { "Button [IsPressed=True] Chrome.Background = ControlPressedBackground", TriggerBrush.Of(
                P("EditorForeground", "ControlPressedBackground", Text)) },
            { "Button [IsEnabled=False] Chrome.Background = ControlDisabledBackground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Text)) },
            { "Button [IsEnabled=False] Chrome.BorderBrush = ControlDisabledBorder", TriggerBrush.ExemptBecause(DisabledBorder) },
            { "Button [IsEnabled=False] .Foreground = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Text)) },

            { "PrimaryButton [IsDefaulted=True] Chrome.BorderBrush = DefaultButtonBorder", TriggerBrush.Of(
                P("DefaultButtonBorder", "WindowBackground", Graphic), P("DefaultButtonBorder", "PanelBackground", Graphic)) },
            { "PrimaryButton [IsMouseOver=True] Chrome.Background = PrimaryHoverBackground", TriggerBrush.Of(
                P("PrimaryForeground", "PrimaryHoverBackground", Text)) },
            { "PrimaryButton [IsPressed=True] Chrome.Background = PrimaryPressedBackground", TriggerBrush.Of(
                P("PrimaryForeground", "PrimaryPressedBackground", Text)) },
            { "PrimaryButton [IsEnabled=False] Chrome.Background = ControlDisabledBackground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Text)) },
            { "PrimaryButton [IsEnabled=False] Chrome.BorderBrush = ControlDisabledBorder", TriggerBrush.ExemptBecause(DisabledBorder) },
            { "PrimaryButton [IsEnabled=False] .Foreground = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Text)) },

            { "ScrollBarLineButton [IsMouseOver=True] Bd.Background = ControlHoverBackground", TriggerBrush.Of(
                P("ScrollBarArrow", "ControlHoverBackground", Graphic)) },
            { "ScrollBarLineButton [IsPressed=True] Bd.Background = ControlPressedBackground", TriggerBrush.Of(
                P("ScrollBarArrow", "ControlPressedBackground", Graphic)) },
            { "ScrollBarThumbStyle [IsMouseOver=True] Bd.Background = ScrollBarThumbHover", TriggerBrush.Of(
                P("ScrollBarThumbHover", "ScrollBarTrack", Graphic)) },
            { "ScrollBarThumbStyle [IsDragging=True] Bd.Background = ScrollBarThumbHover", TriggerBrush.Of(
                P("ScrollBarThumbHover", "ScrollBarTrack", Graphic)) },

            { "CheckBox [IsMouseOver=True] Box.Background = ControlHoverBackground", TriggerBrush.Of(
                P("CheckMarkBrush", "ControlHoverBackground", Graphic)) },
            { "CheckBox [IsPressed=True] Box.Background = ControlPressedBackground", TriggerBrush.Of(
                P("CheckMarkBrush", "ControlPressedBackground", Graphic)) },
            { "CheckBox [IsEnabled=False] Box.Background = ControlDisabledBackground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Graphic)) },
            { "CheckBox [IsEnabled=False] Box.BorderBrush = ControlDisabledBorder", TriggerBrush.ExemptBecause(DisabledBorder) },
            { "CheckBox [IsEnabled=False] CheckMark.Stroke = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Graphic)) },
            { "CheckBox [IsEnabled=False] Indeterminate.Fill = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Graphic)) },
            { "CheckBox [IsEnabled=False] .Foreground = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "WindowBackground", Text), P("DisabledForeground", "PanelBackground", Text)) },

            { "StrengthMeter [Strength=Fair] .Foreground = StrengthFairBrush", TriggerBrush.Of(
                P("StrengthFairBrush", "ProgressTrack", Graphic)) },
            { "StrengthMeter [Strength=Strong] .Foreground = StrengthStrongBrush", TriggerBrush.Of(
                P("StrengthStrongBrush", "ProgressTrack", Graphic)) },

            { "ComboBoxToggleButtonTemplate [IsMouseOver=True] Bd.Background = ControlHoverBackground", TriggerBrush.Of(
                P("EditorForeground", "ControlHoverBackground", Text)) },
            { "ComboBoxToggleButtonTemplate [IsChecked=True] Bd.Background = ControlPressedBackground", TriggerBrush.Of(
                P("EditorForeground", "ControlPressedBackground", Text)) },
            { "ComboBoxToggleButtonTemplate [IsEnabled=False] Bd.Background = ControlDisabledBackground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Text)) },
            { "ComboBoxToggleButtonTemplate [IsEnabled=False] Bd.BorderBrush = ControlDisabledBorder",
                TriggerBrush.ExemptBecause(DisabledBorder) },
            { "ComboBoxToggleButtonTemplate [IsEnabled=False] Arrow.Fill = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Graphic)) },

            { "ComboBoxItem [IsSelected=True] Bd.Background = HighlightBackground", TriggerBrush.Of(
                P("EditorForeground", "HighlightBackground", Text)) },
            { "ComboBoxItem [IsMouseOver=True] Bd.Background = ControlHoverBackground", TriggerBrush.Of(
                P("EditorForeground", "ControlHoverBackground", Text)) },
            { "ComboBoxItem [IsHighlighted=True] Bd.Background = ControlHoverBackground", TriggerBrush.Of(
                P("EditorForeground", "ControlHoverBackground", Text)) },
            { "ComboBoxItem [IsHighlighted=True] KeyboardRing.BorderBrush = FocusBorderBrush", TriggerBrush.Of(
                P("FocusBorderBrush", "PopupBackground", Graphic), P("FocusBorderBrush", "ControlHoverBackground", Graphic),
                P("FocusBorderBrush", "HighlightBackground", Graphic)) },
            { "ComboBoxItem [IsKeyboardFocused=True] KeyboardRing.BorderBrush = FocusBorderBrush", TriggerBrush.Of(
                P("FocusBorderBrush", "PopupBackground", Graphic), P("FocusBorderBrush", "ControlHoverBackground", Graphic),
                P("FocusBorderBrush", "HighlightBackground", Graphic)) },
            { "ComboBoxItem [IsSelected=True&IsHighlighted=True] Bd.Background = HighlightBackground", TriggerBrush.Of(
                P("EditorForeground", "HighlightBackground", Text)) },
            { "ComboBoxItem [IsEnabled=False] .Foreground = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "PopupBackground", Text)) },

            { "ComboBox [IsEnabled=False] .Foreground = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "ControlDisabledBackground", Text)) },

            { "TopLevelHeaderTemplate [IsHighlighted=True] Bd.Background = HighlightBackground", TriggerBrush.Of(
                P("EditorForeground", "HighlightBackground", Text)) },
            { "TopLevelHeaderTemplate [IsHighlighted=True] KeyboardRing.BorderBrush = FocusBorderBrush", TriggerBrush.Of(
                P("FocusBorderBrush", "PanelBackground", Graphic), P("FocusBorderBrush", "HighlightBackground", Graphic)) },
            { "TopLevelHeaderTemplate [IsSubmenuOpen=True] Bd.Background = HighlightBackground", TriggerBrush.Of(
                P("EditorForeground", "HighlightBackground", Text)) },
            { "TopLevelItemTemplate [IsHighlighted=True] Bd.Background = HighlightBackground", TriggerBrush.Of(
                P("EditorForeground", "HighlightBackground", Text)) },
            { "TopLevelItemTemplate [IsHighlighted=True] KeyboardRing.BorderBrush = FocusBorderBrush", TriggerBrush.Of(
                P("FocusBorderBrush", "PanelBackground", Graphic), P("FocusBorderBrush", "HighlightBackground", Graphic)) },
            { "SubmenuItemTemplate [IsHighlighted=True] Bd.Background = HighlightBackground", TriggerBrush.Of(
                P("EditorForeground", "HighlightBackground", Text)) },
            { "SubmenuItemTemplate [IsHighlighted=True] GestureText.Foreground = EditorForeground", TriggerBrush.Of(
                P("EditorForeground", "HighlightBackground", Text)) },
            { "SubmenuItemTemplate [IsHighlighted=True] KeyboardRing.BorderBrush = FocusBorderBrush", TriggerBrush.Of(
                P("FocusBorderBrush", "PopupBackground", Graphic), P("FocusBorderBrush", "HighlightBackground", Graphic)) },
            { "SubmenuItemTemplate [IsEnabled=False] GestureText.Foreground = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "PopupBackground", Text)) },
            { "SubmenuHeaderTemplate [IsHighlighted=True] Bd.Background = HighlightBackground", TriggerBrush.Of(
                P("EditorForeground", "HighlightBackground", Text)) },
            { "SubmenuHeaderTemplate [IsHighlighted=True] KeyboardRing.BorderBrush = FocusBorderBrush", TriggerBrush.Of(
                P("FocusBorderBrush", "PopupBackground", Graphic), P("FocusBorderBrush", "HighlightBackground", Graphic)) },

            { "MenuItem [IsEnabled=False] .Foreground = DisabledForeground", TriggerBrush.Of(
                P("DisabledForeground", "PopupBackground", Text), P("DisabledForeground", "PanelBackground", Text)) },
        };

        private static readonly string[] BrushProperties = { "Background", "BorderBrush", "Foreground", "Stroke", "Fill" };

        private static string XKey(XElement e)
        {
            XAttribute key = e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"));
            return key == null ? null : key.Value;
        }

        private static string TypeName(string typeAttribute)
        {
            Match m = Regex.Match(typeAttribute ?? "", @"^\{x:Type\s+(\w+)\}$");
            return m.Success ? m.Groups[1].Value : typeAttribute;
        }

        /// <summary>The keyed style or template a trigger belongs to, else its implicit style's target type.</summary>
        private static string Owner(XElement setter)
        {
            XElement[] owners = setter.Ancestors().Where(a => a.Name.LocalName == "Style" || a.Name.LocalName == "ControlTemplate")
                .ToArray();
            foreach (XElement o in owners)
                if (XKey(o) != null) return XKey(o);
            XElement style = owners.LastOrDefault(o => o.Name.LocalName == "Style") ?? owners.First();
            return TypeName((string)style.Attribute("TargetType"));
        }

        private static string Condition(XElement trigger)
        {
            Func<XElement, string> one = c =>
            {
                string binding = (string)c.Attribute("Binding");
                string left = binding != null ? Regex.Match(binding, @"\{Binding\s+(\w+)\}").Groups[1].Value
                    : (string)c.Attribute("Property");
                return left + "=" + (string)c.Attribute("Value");
            };
            if (trigger.Name.LocalName == "Trigger" || trigger.Name.LocalName == "DataTrigger") return one(trigger);
            return string.Join("&", trigger.Descendants().Where(d => d.Name.LocalName == "Condition").Select(one));
        }

        /// <summary>Every brush Setter inside a trigger in Controls.xaml, as the TriggerBrushes key.</summary>
        private static List<string> TriggerBrushSetters()
        {
            XDocument doc = XDocument.Load(Path.Combine(AppDir(), "Themes", "Controls.xaml"));
            var found = new List<string>();
            foreach (XElement setter in doc.Descendants().Where(e => e.Name.LocalName == "Setter"))
            {
                XElement trigger = setter.Ancestors().FirstOrDefault(a => a.Name.LocalName.EndsWith("Trigger", StringComparison.Ordinal));
                string property = (string)setter.Attribute("Property");
                if (trigger == null || !BrushProperties.Contains(property)) continue;
                string value = (string)setter.Attribute("Value") ?? "";
                Match key = Regex.Match(value, @"^\{DynamicResource\s+(\w+)\}$");
                Assert.That(key.Success, Is.True, "a trigger sets a theme brush as a DynamicResource: " + setter);
                found.Add(Owner(setter) + " [" + Condition(trigger) + "] " + (string)setter.Attribute("TargetName") + "."
                    + property + " = " + key.Groups[1].Value);
            }
            return found;
        }

        [Test]
        public void TriggerBrushes_TheTableListsExactlyTheTriggersInControlsXaml()
        {
            List<string> found = TriggerBrushSetters();
            Assert.That(found.Count, Is.GreaterThanOrEqualTo(40), "the parser finds the triggers");
            Assert.That(found, Is.Unique);
            Assert.That(found.Where(f => !TriggerBrushes.ContainsKey(f)), Is.Empty,
                "a trigger sets a brush the contrast table does not check; add it with the surface it is drawn on");
            Assert.That(TriggerBrushes.Keys.Where(k => !found.Contains(k)), Is.Empty, "the table names a trigger that no longer exists");
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [Apartment(ApartmentState.STA)]
        public void TriggerBrushes_EverySetBrushMeetsWcagAgainstWhatItIsDrawnOn(string theme)
        {
            ResourceDictionary dict = Load(theme);
            var failures = new List<string>();
            foreach (KeyValuePair<string, TriggerBrush> entry in TriggerBrushes)
            {
                string set = entry.Key.Substring(entry.Key.LastIndexOf("= ", StringComparison.Ordinal) + 2);
                if (entry.Value.Exempt != null) continue;
                Assert.That(entry.Value.Pairs, Is.Not.Empty, entry.Key);
                foreach (object[] pair in entry.Value.Pairs)
                {
                    Assert.That((string)pair[0] == set || (string)pair[1] == set, Is.True,
                        entry.Key + ": each pair includes the brush the trigger sets");
                    Color fg = ((SolidColorBrush)dict[(string)pair[0]]).Color;
                    Color bg = ((SolidColorBrush)dict[(string)pair[1]]).Color;
                    double ratio = ContrastRatio(fg, bg);
                    if (ratio < (double)pair[2])
                        failures.Add(entry.Key + ": " + pair[0] + " on " + pair[1] + " " + ratio.ToString("0.00") + " < " + pair[2]);
                }
            }
            Assert.That(failures, Is.Empty, theme);
        }

        // ---- the focus ring and the keyboard position in menus and lists ----

        [TestCase("Light")]
        [TestCase("Dark")]
        [Apartment(ApartmentState.STA)]
        public void FocusRing_LeavesAGapToTheControl_AndMeets3To1AgainstEverySurfaceAControlSitsOn(string theme)
        {
            ResourceDictionary controls = Load("Controls");
            var style = (Style)controls["FocusVisual"];
            var template = (ControlTemplate)style.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
            var host = new Control { Template = template };
            host.Resources.MergedDictionaries.Add(Load(theme));
            host.ApplyTemplate();
            var ring = (System.Windows.Shapes.Rectangle)VisualTreeHelper.GetChild(host, 0);
            Assert.That(ring.Margin, Is.EqualTo(new Thickness(-4)), "the ring sits 4 px outside the control");
            Assert.That(ring.StrokeThickness, Is.EqualTo(2));
            Assert.That(-ring.Margin.Left - ring.StrokeThickness, Is.GreaterThanOrEqualTo(2),
                "a gap between the ring and the control shows the surface, so a ring the colour of the button stays visible");

            ResourceDictionary dict = Load(theme);
            Color focus = ((SolidColorBrush)dict["FocusBorderBrush"]).Color;
            foreach (string surface in new[] { "WindowBackground", "PanelBackground", "PopupBackground", "EditorBackground",
                "WarningBackground" })
                Assert.That(ContrastRatio(focus, ((SolidColorBrush)dict[surface]).Color), Is.GreaterThanOrEqualTo(Graphic),
                    "focus ring on " + surface);
        }

        private static void SetHighlighted(Control item, bool value)
        {
            item.GetType().GetProperty("IsHighlighted").SetValue(item, value);
        }

        [TestCase(true)]
        [TestCase(false)]
        [Apartment(ApartmentState.STA)]
        public void KeyboardPosition_MenuItemsAndListItemsGetARing_WithoutChangingTheirSize(bool dark)
        {
            var top = new MenuItem { Header = "_File" };
            top.Items.Add(new MenuItem { Header = "x" });
            var topItem = new MenuItem { Header = "_Settings" };
            var menu = new Menu();
            menu.Items.Add(top);
            menu.Items.Add(topItem);
            var subItem = new MenuItem { Header = "Save", InputGestureText = "Ctrl+S" };
            var subHeader = new MenuItem { Header = "More" };
            subHeader.Items.Add(new MenuItem { Header = "y" });
            var comboItem = new ComboBoxItem { Content = "Dark" };
            var panel = new StackPanel();
            panel.Children.Add(menu);
            panel.Children.Add(subItem);
            panel.Children.Add(subHeader);
            panel.Children.Add(comboItem);
            var window = new Window { Content = panel };
            try
            {
                ApplyTheme(window, dark);
                Layout(window);
                Assert.That(top.Role, Is.EqualTo(MenuItemRole.TopLevelHeader));
                Assert.That(topItem.Role, Is.EqualTo(MenuItemRole.TopLevelItem));
                Assert.That(subItem.Role, Is.EqualTo(MenuItemRole.SubmenuItem));
                Assert.That(subHeader.Role, Is.EqualTo(MenuItemRole.SubmenuHeader));
                object focus = window.FindResource("FocusBorderBrush");
                foreach (Control item in new Control[] { top, topItem, subItem, subHeader, comboItem })
                {
                    string name = item is MenuItem ? ((MenuItem)item).Role.ToString() : "ComboBoxItem";
                    var ring = (Border)item.Template.FindName("KeyboardRing", item);
                    Assert.That(ring, Is.Not.Null, name + " has a keyboard ring");
                    Assert.That(ring.IsHitTestVisible, Is.False, name);
                    Assert.That(ring.BorderThickness, Is.EqualTo(new Thickness(1)), name);
                    Assert.That(((SolidColorBrush)ring.BorderBrush).Color.A, Is.EqualTo(0), name + ": invisible when not highlighted");
                    Size before = new Size(item.ActualWidth, item.ActualHeight);

                    SetHighlighted(item, true);
                    Layout(window);
                    Assert.That(ring.BorderBrush, Is.SameAs(focus), name + ": the keyboard position is outlined");
                    Assert.That(ring.ActualWidth, Is.EqualTo(item.ActualWidth).Within(0.01), name + ": around the whole item");
                    Assert.That(new Size(item.ActualWidth, item.ActualHeight), Is.EqualTo(before), name + ": no layout change");

                    SetHighlighted(item, false);
                    Layout(window);
                    Assert.That(((SolidColorBrush)ring.BorderBrush).Color.A, Is.EqualTo(0), name);
                }
            }
            finally
            {
                window.Close();
            }
        }

        // ---- high contrast ----

        private static bool IsSource(ResourceDictionary d, string name)
        {
            return d.Source != null && d.Source.OriginalString.EndsWith("Themes/" + name + ".xaml", StringComparison.Ordinal);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyTo_HighContrast_NoTemplatesNoThemeColours_SystemColoursInstead()
        {
            var merged = new ResourceDictionary().MergedDictionaries;
            var other = new ResourceDictionary();
            other["Marker"] = 1;
            merged.Add(other);

            ThemeService.ApplyTo(merged, true, false);
            Assert.That(merged.Any(d => IsSource(d, "Dark")), Is.True);
            Assert.That(merged.Any(d => IsSource(d, "Controls")), Is.True, "the control templates in normal contrast");

            ThemeService.ApplyTo(merged, true, true);

            Assert.That(merged.Contains(other), Is.True);
            Assert.That(merged.Any(d => IsSource(d, "Dark") || IsSource(d, "Light")), Is.False, "no theme colours");
            Assert.That(merged.Any(d => IsSource(d, "Controls")), Is.False, "no custom control templates");
            ResourceDictionary hc = merged.Single(d => d != other);
            foreach (object key in Load("Dark").Keys)
                Assert.That(hc.Contains(key), Is.True, "high contrast maps " + key);
            Assert.That(((SolidColorBrush)hc["WindowBackground"]).Color, Is.EqualTo(SystemColors.WindowColor));
            Assert.That(((SolidColorBrush)hc["EditorForeground"]).Color, Is.EqualTo(SystemColors.WindowTextColor));
            Assert.That(((SolidColorBrush)hc["EditorSelectionBrush"]).Color, Is.EqualTo(SystemColors.HighlightColor));
            foreach (object key in StyleKeys) Assert.That(hc.Contains(key), Is.False, "no style " + key);

            ThemeService.ApplyTo(merged, false, false);
            Assert.That(merged.Count, Is.EqualTo(3), "other, Light, Controls");
            Assert.That(merged.Any(d => IsSource(d, "Light")), Is.True);
            Assert.That(merged.Any(d => IsSource(d, "Controls")), Is.True);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyResolved_FollowsTheHighContrastSeam_AtStartupAndWhenItChanges()
        {
            Func<bool> saved = ThemeService.HighContrast;
            try
            {
                var merged = new ResourceDictionary().MergedDictionaries;
                bool hc = true;
                ThemeService.HighContrast = () => hc;

                ThemeService.ApplyResolved(merged, null, true);
                Assert.That(merged.Any(d => IsSource(d, "Controls") || IsSource(d, "Dark")), Is.False, "started in HC");

                hc = false;
                ThemeService.ReapplyForHighContrast(merged, null);
                Assert.That(merged.Any(d => IsSource(d, "Dark")), Is.True, "the last theme (Dark) comes back");
                Assert.That(merged.Any(d => IsSource(d, "Controls")), Is.True);

                hc = true;
                ThemeService.ReapplyForHighContrast(merged, null);
                Assert.That(merged.Any(d => IsSource(d, "Controls") || IsSource(d, "Dark")), Is.False, "turned on later");
            }
            finally
            {
                ThemeService.HighContrast = saved;
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void HighContrast_ButtonsAndMenuUseTheSystemStyles()
        {
            Notebook nb = NewNotebook("A");
            var shell = new ShellViewModel(new NotebookStore(), new SettingsStore(_dir.File("s")), new AppSettings(),
                new FakeDialogService(), new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
            var window = new MainWindow { DataContext = shell };
            try
            {
                ThemeService.ApplyTo(window.Resources.MergedDictionaries, true, true);
                shell.ShowEditor(nb);
                Layout(window);
                var menu = (Menu)window.FindName("MainMenu");
                var file = (MenuItem)window.FindName("FileMenu");
                var saveAs = (MenuItem)window.FindName("SaveAsMenuItem");

                // Whatever Windows' own style for the current system theme gives (in high contrast: the system
                // colours); in this test host that is a Menu with no LockPad resources at all.
                var plainItem = new MenuItem { Header = "x" };
                var plainMenu = new Menu();
                plainMenu.Items.Add(plainItem);
                plainItem.Items.Add(new MenuItem { Header = "y" });
                var plainWindow = new Window { Content = plainMenu };
                try
                {
                    Layout(plainWindow);
                    Assert.That(menu.Style, Is.Null, "no custom menu style");
                    Assert.That(file.Style, Is.Null, "no custom menu item style");
                    Assert.That(saveAs.Style, Is.Null);
                    Assert.That(Colour(file.Foreground), Is.EqualTo(Colour(plainItem.Foreground)), "top level: system style");
                    Assert.That(Colour(saveAs.Foreground), Is.EqualTo(Colour(((MenuItem)plainItem.Items[0]).Foreground)),
                        "nested: system style");
                    Assert.That(Colour(menu.Background), Is.EqualTo(Colour(plainMenu.Background)));
                }
                finally
                {
                    plainWindow.Close();
                }
                // TryFindResource(typeof(Button)) would fall through to Windows' own style, so look at what LockPad merged.
                Assert.That(window.Resources.Contains(typeof(Button)), Is.False, "no custom button style");
                Assert.That(window.Resources.MergedDictionaries.Any(d => d.Contains(typeof(Button)) || d.Contains(typeof(Menu))),
                    Is.False, "no custom button or menu style");
                Assert.That(window.TryFindResource("PrimaryButton"), Is.Null);
                Assert.That(((SolidColorBrush)window.FindResource("WindowBackground")).Color,
                    Is.EqualTo(SystemColors.WindowColor), "theme keys show the system colours");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void NormalContrast_TopLevelAndNestedMenuItemsUseTheTheme()
        {
            var shell = new ShellViewModel(new NotebookStore(), new SettingsStore(_dir.File("s")), new AppSettings(),
                new FakeDialogService(), new FakeClipboardService(), new FakeClock(), TestKdf.Fast);
            var window = new MainWindow { DataContext = shell };
            try
            {
                foreach (bool dark in new[] { true, false })
                {
                    ApplyTheme(window, dark);
                    shell.ShowUnlock();
                    Layout(window);
                    object fg = window.FindResource("EditorForeground");
                    var menu = (Menu)window.FindName("MainMenu");
                    Assert.That(menu.Background, Is.SameAs(window.FindResource("PanelBackground")));
                    foreach (MenuItem top in menu.Items.OfType<MenuItem>())
                    {
                        Assert.That(top.Foreground, Is.SameAs(fg), "top level " + top.Header);
                        foreach (MenuItem nested in top.Items.OfType<MenuItem>().Where(m => m.IsEnabled))
                            Assert.That(nested.Foreground, Is.SameAs(fg), "nested " + nested.Header);
                    }
                }
            }
            finally
            {
                window.Close();
            }
        }

        // ---- primary buttons ----

        private static IEnumerable<Button> ButtonsIn(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var b = child as Button;
                if (b != null) yield return b;
                var d = child as DependencyObject;
                if (d == null) continue;
                foreach (Button inner in ButtonsIn(d)) yield return inner;
            }
        }

        private static string Describe(FrameworkElement root, Button b)
        {
            return root.GetType().Name + "." + (string.IsNullOrEmpty(b.Name) ? b.Content as string : b.Name);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void PrimaryStyle_ExactlyOnTheFiveMainActions_NeverOnTheSafeDefaultDialogs()
        {
            Notebook nb = NewNotebook("A");
            var dialogs = new FakeDialogService();
            var changeVm = new ChangePasswordViewModel(nb, dialogs, new PasswordBackOff(new FakeClock()));
            var roots = new List<FrameworkElement>
            {
                new UnlockView
                {
                    DataContext = new UnlockViewModel(new NotebookStore(), new SettingsStore(_dir.File("s")),
                        new AppSettings(), dialogs, n => { }, () => { })
                },
                new CreateNotebookView
                {
                    DataContext = new CreateNotebookViewModel(new NotebookStore(), dialogs, TestKdf.Fast, n => { }, () => { })
                },
                new EditorView
                {
                    DataContext = new EditorViewModel(nb, new NotebookStore(), dialogs, new SettingsStore(_dir.File("s")),
                        new AppSettings())
                },
                new SettingsWindow(new SettingsViewModel(new AppSettings())),
                new PromptWindow("Rename note", "Title:", "x"),
                new ChangePasswordWindow(changeVm),
                new ConfirmDefaultNoWindow("Close without saving", "m"),
                new CloseChoiceWindow("Could not save", "m"),
                new ShortcutsWindow(),
            };
            try
            {
                var primary = new List<string>();
                int buttons = 0;
                foreach (FrameworkElement root in roots)
                {
                    ApplyTheme(root, true);
                    Layout(root);
                    object style = root.FindResource("PrimaryButton");
                    object themed = root.FindResource(typeof(Button));
                    foreach (Button b in ButtonsIn(root))
                    {
                        buttons++;
                        if (ReferenceEquals(b.Style, style)) primary.Add(Describe(root, b));
                        else Assert.That(b.Style, Is.SameAs(themed), Describe(root, b) + " uses the themed implicit style");
                    }
                }
                Assert.That(buttons, Is.GreaterThan(15));
                Assert.That(primary.OrderBy(s => s), Is.EqualTo(new[]
                {
                    "ChangePasswordWindow.ChangeButton",
                    "CreateNotebookView.CreateButton",
                    "PromptWindow.OkButton",
                    "SettingsWindow.OkButton",
                    "UnlockView.UnlockButton",
                }));
            }
            finally
            {
                foreach (Window w in roots.OfType<Window>()) w.Close();
                changeVm.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ChangeButton_StillDisabledWhileWaitingOrAbandoned()
        {
            Notebook nb = NewNotebook("A");
            var vm = new ChangePasswordViewModel(nb, new FakeDialogService(), new PasswordBackOff(new FakeClock()));
            var win = new ChangePasswordWindow(vm);
            try
            {
                Pump();
                var change = (Button)win.FindName("ChangeButton");
                Assert.That(change.IsEnabled, Is.True);
                vm.Abandon();
                Pump();
                Assert.That(change.IsEnabled, Is.False, "abandoned");
            }
            finally
            {
                win.Close();
                vm.Dispose();
            }
        }

        // ---- button order: main action first, Cancel last ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CreatePage_CreateThenCancel()
        {
            var view = new CreateNotebookView
            {
                DataContext = new CreateNotebookViewModel(new NotebookStore(), new FakeDialogService(), TestKdf.Fast,
                    n => { }, () => { })
            };
            var create = (Button)view.FindName("CreateButton");
            var cancel = (Button)view.FindName("CancelButton");
            Assert.That(create.Content, Is.EqualTo("Create"));
            Assert.That(cancel.Content, Is.EqualTo("Cancel"));
            var panel = (Panel)create.Parent;
            Assert.That(cancel.Parent, Is.SameAs(panel));
            Assert.That(panel.Children.IndexOf(create), Is.LessThan(panel.Children.IndexOf(cancel)));
            Assert.That(panel.Children.OfType<Button>().Last(), Is.SameAs(cancel), "Cancel last");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EveryWindow_CancelIsTheLastButton()
        {
            Notebook nb = NewNotebook("A");
            var changeVm = new ChangePasswordViewModel(nb, new FakeDialogService(), new PasswordBackOff(new FakeClock()));
            var roots = new List<FrameworkElement>
            {
                new CreateNotebookView
                {
                    DataContext = new CreateNotebookViewModel(new NotebookStore(), new FakeDialogService(), TestKdf.Fast,
                        n => { }, () => { })
                },
                new SettingsWindow(new SettingsViewModel(new AppSettings())),
                new PromptWindow("Rename note", "Title:", "x"),
                new ChangePasswordWindow(changeVm),
                new CloseChoiceWindow("Could not save", "m"),
            };
            try
            {
                foreach (FrameworkElement root in roots)
                {
                    Button cancel = ButtonsIn(root).Single(b => (b.Content as string) == "Cancel");
                    var panel = (Panel)cancel.Parent;
                    Assert.That(panel.Children.OfType<Button>().Last(), Is.SameAs(cancel), root.GetType().Name);
                    Assert.That(panel.Children.OfType<Button>().Count(), Is.GreaterThanOrEqualTo(2), root.GetType().Name);
                }
            }
            finally
            {
                foreach (Window w in roots.OfType<Window>()) w.Close();
                changeVm.Dispose();
            }
        }

        // ---- strength meter ----

        /// <summary>The prefixes of a long password run through Weak, Fair and Strong.</summary>
        private static string PasswordRated(StrengthRating rating)
        {
            const string source = "q7#Vt9!mZ2@xLp4$Rw8&Kd5^Hs3*Nf6%";
            for (int n = 1; n <= source.Length; n++)
            {
                string candidate = source.Substring(0, n);
                using (SecureString s = Secure(candidate))
                    if (PasswordStrength.Rate(PasswordStrength.EstimateBits(s)) == rating) return candidate;
            }
            Assert.Fail("no prefix is rated " + rating);
            return null;
        }

        private static void AssertMeter(FrameworkElement root, ProgressBar bar, FrameworkElement ratingText,
            FrameworkElement hint, Action<SecureString> update)
        {
            Assert.That(bar.Value, Is.EqualTo(0), "the track is empty before typing");
            Assert.That(ratingText.Visibility, Is.EqualTo(Visibility.Collapsed), "no rating before typing");
            Assert.That(hint.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(((TextBlock)hint).Text, Is.EqualTo("Type a password to see its strength."));
            Assert.That(((TextBlock)hint).Foreground, Is.SameAs(root.FindResource("MutedForeground")), "muted");

            foreach (KeyValuePair<StrengthRating, string> expected in new Dictionary<StrengthRating, string>
            {
                { StrengthRating.Weak, "StrengthWeakBrush" },
                { StrengthRating.Fair, "StrengthFairBrush" },
                { StrengthRating.Strong, "StrengthStrongBrush" },
            })
            {
                using (SecureString s = Secure(PasswordRated(expected.Key))) update(s);
                Pump();
                Assert.That(bar.Value, Is.GreaterThan(0), expected.Key + ": the fill is proportional");
                Assert.That(bar.Foreground, Is.SameAs(root.FindResource(expected.Value)), expected.Key.ToString());
                Assert.That(ratingText.Visibility, Is.EqualTo(Visibility.Visible), expected.Key.ToString());
                Assert.That(hint.Visibility, Is.EqualTo(Visibility.Collapsed), expected.Key.ToString());
            }

            using (SecureString empty = new SecureString()) update(empty);
            Pump();
            Assert.That(bar.Value, Is.EqualTo(0), "emptied again");
            Assert.That(ratingText.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(hint.Visibility, Is.EqualTo(Visibility.Visible));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void StrengthMeter_CreatePage_EmptyThenColouredByRating()
        {
            var vm = new CreateNotebookViewModel(new NotebookStore(), new FakeDialogService(), TestKdf.Fast, n => { }, () => { });
            var view = new CreateNotebookView { DataContext = vm };
            ApplyTheme(view, true);
            Layout(view);
            Assert.That(vm.HasPassword, Is.False);
            AssertMeter(view, (ProgressBar)view.FindName("StrengthBar"), (FrameworkElement)view.FindName("StrengthText"),
                (FrameworkElement)view.FindName("StrengthHint"), vm.UpdateStrength);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void StrengthMeter_ChangePasswordDialog_EmptyThenColouredByRating()
        {
            Notebook nb = NewNotebook("A");
            var vm = new ChangePasswordViewModel(nb, new FakeDialogService(), new PasswordBackOff(new FakeClock()));
            var win = new ChangePasswordWindow(vm);
            try
            {
                ApplyTheme(win, false);
                Layout(win);
                Assert.That(vm.HasPassword, Is.False);
                AssertMeter(win, (ProgressBar)win.FindName("StrengthBar"), (FrameworkElement)win.FindName("StrengthText"),
                    (FrameworkElement)win.FindName("StrengthHint"), vm.UpdateStrength);
            }
            finally
            {
                win.Close();
                vm.Dispose();
            }
        }

        // ---- room for the focus ring between the sidebar buttons (fix round 2, Y3) ----

        private static Thickness ParseMargin(XElement e)
        {
            string margin = (string)e.Attribute("Margin") ?? "0";
            return (Thickness)new ThicknessConverter().ConvertFromInvariantString(margin);
        }

        [Test]
        public void EditorSidebarButtons_AtLeast8PxApart_OuterEdgesFlush()
        {
            XDocument doc = XDocument.Load(Path.Combine(AppDir(), "Views", "EditorView.xaml"));
            XElement grid = doc.Descendants().Single(e => e.Name.LocalName == "UniformGrid");
            Assert.That((string)grid.Attribute("Columns"), Is.EqualTo("2"));
            Thickness[] m = grid.Elements().Where(e => e.Name.LocalName == "Button").Select(ParseMargin).ToArray();
            Assert.That(m.Length, Is.EqualTo(4), "New, Rename / Delete, Undo delete");

            // The focus ring sits 4 px outside a button: 8 px between neighbours keeps two rings apart.
            Assert.That(m[0].Right + m[1].Left, Is.GreaterThanOrEqualTo(8), "New | Rename");
            Assert.That(m[2].Right + m[3].Left, Is.GreaterThanOrEqualTo(8), "Delete | Undo delete");
            Assert.That(m[0].Bottom + m[2].Top, Is.GreaterThanOrEqualTo(8), "New / Delete");
            Assert.That(m[1].Bottom + m[3].Top, Is.GreaterThanOrEqualTo(8), "Rename / Undo delete");
            // The grid's outer edges stay aligned with the list above it.
            Assert.That(new[] { m[0].Left, m[2].Left, m[1].Right, m[3].Right, m[0].Top, m[1].Top, m[2].Bottom, m[3].Bottom },
                Has.All.EqualTo(0));
            // Equal cells: each column / row has the same total margin, so the buttons keep the same size.
            Assert.That(m[0].Left + m[0].Right, Is.EqualTo(m[1].Left + m[1].Right));
            Assert.That(m[0].Top + m[0].Bottom, Is.EqualTo(m[2].Top + m[2].Bottom));
        }

        // ---- editor empty states ----

        [Test]
        public void EmptyStateText_NoNotes_NoSelection_Selected()
        {
            var store = new NotebookStore();
            var dialogs = new FakeDialogService();
            var empty = new EditorViewModel(NewNotebook(), store, dialogs, new SettingsStore(_dir.File("s")), new AppSettings());
            Assert.That(empty.EmptyStateText, Is.EqualTo("No notes yet. Click New (Ctrl+N) to add one."));

            var vm = new EditorViewModel(NewNotebook("A", "B"), store, dialogs, new SettingsStore(_dir.File("s")),
                new AppSettings());
            Assert.That(vm.SelectedNote, Is.Not.Null, "the first note is selected on open");
            Assert.That(vm.EmptyStateText, Is.Null, "hidden while a note is selected");
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
            vm.SelectedNote = null;
            Assert.That(vm.EmptyStateText, Is.EqualTo("Select a note on the left."));
            Assert.That(raised, Does.Contain("EmptyStateText"));

            // Deleting the last note: back to "No notes yet".
            var single = new EditorViewModel(NewNotebook("Only"), store, dialogs, new SettingsStore(_dir.File("s")),
                new AppSettings());
            raised.Clear();
            single.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
            dialogs.Confirms.Enqueue(true);
            single.DeleteNoteCommand.Execute(null);
            Assert.That(single.EmptyStateText, Is.EqualTo("No notes yet. Click New (Ctrl+N) to add one."));
            Assert.That(raised, Does.Contain("EmptyStateText"));

            // A new note is selected: hidden.
            dialogs.Prompts.Enqueue("New");
            single.NewNoteCommand.Execute(null);
            Assert.That(single.EmptyStateText, Is.Null);
        }

        [Test]
        public void EmptyStateText_FilterHidesEveryNote_NoNotesMatch()
        {
            // Fix round 1, X7.
            var vm = new EditorViewModel(NewNotebook("Alpha", "Beta"), new NotebookStore(), new FakeDialogService(),
                new SettingsStore(_dir.File("s")), new AppSettings());
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            vm.FilterText = "zzz";
            Assert.That(vm.VisibleNotes, Is.Empty);
            Assert.That(vm.EmptyStateText, Is.EqualTo("No notes match the filter."));
            Assert.That(raised, Does.Contain("EmptyStateText"));

            raised.Clear();
            vm.FilterText = "alp"; // one note visible, none selected (the filter cleared the selection)
            Assert.That(vm.VisibleNotes.Count, Is.EqualTo(1));
            Assert.That(vm.SelectedNote, Is.Null);
            Assert.That(vm.EmptyStateText, Is.EqualTo("Select a note on the left."));
            Assert.That(raised, Does.Contain("EmptyStateText"));

            vm.FilterText = "";
            Assert.That(vm.EmptyStateText, Is.EqualTo("Select a note on the left."));
        }

        [Test]
        public void EmptyStateText_ReadOnlyNotebook_NoNotes_NeverSuggestsNew()
        {
            var store = new NotebookStore();
            var empty = new Notebook(_dir.File("ro.kdbx"), true, new Note[0]);
            var vm = new EditorViewModel(empty, store, new FakeDialogService(), new SettingsStore(_dir.File("s")),
                new AppSettings());
            Assert.That(vm.IsReadOnly, Is.True);
            Assert.That(vm.EmptyStateText, Is.EqualTo("This notebook has no notes."));

            var withNotes = new Notebook(_dir.File("ro2.kdbx"), true,
                new[] { Note.Create("A1B2", "Only", "text", DateTime.UtcNow) });
            var vm2 = new EditorViewModel(withNotes, store, new FakeDialogService(), new SettingsStore(_dir.File("s")),
                new AppSettings());
            vm2.SelectedNote = null;
            Assert.That(vm2.EmptyStateText, Is.EqualTo("Select a note on the left."));
            vm2.FilterText = "zzz";
            Assert.That(vm2.EmptyStateText, Is.EqualTo("No notes match the filter."));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EmptyState_View_MutedCentredInTheEditorArea_HiddenWhenSelected_NotWhileObscured()
        {
            var vm = new EditorViewModel(NewNotebook("A"), new NotebookStore(), new FakeDialogService(),
                new SettingsStore(_dir.File("s")), new AppSettings());
            var view = new EditorView { DataContext = vm };
            ApplyTheme(view, true);
            Layout(view);
            var text = (TextBlock)view.FindName("EmptyStateText");
            var editor = (TextBox)view.FindName("Editor");
            var content = (FrameworkElement)view.FindName("EditorContent");
            Assert.That(text, Is.Not.Null);
            Assert.That(text.Visibility, Is.EqualTo(Visibility.Collapsed), "a note is selected");

            vm.SelectedNote = null;
            Layout(view);
            Assert.That(text.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(text.Text, Is.EqualTo("Select a note on the left."));
            Assert.That(text.Foreground, Is.SameAs(view.FindResource("MutedForeground")));
            Assert.That(text.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Center));
            Assert.That(text.VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
            Assert.That(text.IsHitTestVisible, Is.False, "never in the way of the editor box");
            Assert.That(text.Parent, Is.SameAs(editor.Parent), "over the editor box");
            Assert.That(content.IsAncestorOf(text), Is.True, "hidden with the note area while obscured");

            vm.Obscure();
            Layout(view);
            Assert.That(content.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        // ---- the theme also covers ToolTip, ComboBox, CheckBox and ScrollBar in real views ----

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SettingsWindow_ThemedComboBoxAndButtons_InstantiateAndLayOut()
        {
            foreach (bool dark in new[] { true, false })
            {
                var win = new SettingsWindow(new SettingsViewModel(new AppSettings()));
                try
                {
                    ApplyTheme(win, dark);
                    Layout(win);
                    var combo = (ComboBox)win.FindName("ThemeBox");
                    Assert.That(combo.Template, Is.SameAs(((Style)win.FindResource(typeof(ComboBox))).Setters
                        .OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value));
                    Assert.That(combo.Background, Is.SameAs(win.FindResource("ControlBackground")));
                    combo.IsDropDownOpen = true;
                    Pump();
                    combo.IsDropDownOpen = false;
                    var ok = (Button)win.FindName("OkButton");
                    Assert.That(ok.Background, Is.SameAs(win.FindResource("PrimaryBackground")));
                    Assert.That(ok.Foreground, Is.SameAs(win.FindResource("PrimaryForeground")));
                    var cancel = (Button)win.FindName("CancelButton");
                    Assert.That(cancel.Background, Is.SameAs(win.FindResource("ControlBackground")));
                    Assert.That(cancel.FocusVisualStyle, Is.SameAs(win.FindResource("FocusVisual")));
                }
                finally
                {
                    win.Close();
                }
            }
        }
    }
}

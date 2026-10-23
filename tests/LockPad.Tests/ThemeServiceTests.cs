using System;
using System.Linq;
using System.Threading;
using System.Windows;
using LockPad.App.Services;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class ThemeServiceTests
    {
        private static readonly string[] BrushKeys =
        {
            "WindowBackground", "PanelBackground", "EditorBackground", "EditorForeground",
            "AccentBrush", "BorderBrush", "MutedForeground", "ErrorForeground", "WarningBackground",
            "DialogBorder"
        };

        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(null, false)]
        [TestCase("x", false)]
        [TestCase(2, false)]
        public void SystemPrefersDark(object v, bool expected)
        {
            Assert.That(ThemeService.SystemPrefersDark(v), Is.EqualTo(expected));
        }

        [TestCase("Dark", AppTheme.Dark)]
        [TestCase("light", AppTheme.Light)]
        [TestCase("System", AppTheme.System)]
        [TestCase("bogus", AppTheme.System)]
        [TestCase(null, AppTheme.System)]
        public void Parse_MapsNamesAndFallsBackToSystem(string name, AppTheme expected)
        {
            Assert.That(ThemeService.Parse(name), Is.EqualTo(expected));
        }

        private static void RegisterPackScheme()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            // Reading a static member runs Application's static constructor, which registers the
            // pack/application web-request handlers, without creating an Application instance.
            GC.KeepAlive(Application.Current);
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [Apartment(ApartmentState.STA)]
        public void ThemeDictionary_DefinesAllBrushKeys(string name)
        {
            // Registers the pack:// scheme so this test passes when run alone.
            // (UriSchemePack is a const and would be inlined, so call a method to run the static init.)
            RegisterPackScheme();
            var dict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LockPad;component/Themes/" + name + ".xaml")
            };
            foreach (string key in BrushKeys)
                Assert.That(dict.Contains(key), Is.True, name + " is missing " + key);
        }

        [Test]
        public void Apply_WithoutApplication_IsNoOp()
        {
            if (Application.Current != null) Assert.Ignore("An Application exists in this host.");
            Assert.DoesNotThrow(() => ThemeService.Apply(AppTheme.Dark));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyTo_ReplacesOnlyTheThemeDictionaries()
        {
            RegisterPackScheme();
            var merged = new ResourceDictionary().MergedDictionaries;
            var other = new ResourceDictionary();
            other["Marker"] = 1;
            merged.Add(other);

            ThemeService.ApplyTo(merged, true);
            ThemeService.ApplyTo(merged, false);
            ThemeService.ApplyTo(merged, true);

            // The colours (Dark.xaml) and, since 2026-10-21, the control templates (Controls.xaml).
            Assert.That(merged.Contains(other), Is.True);
            Assert.That(merged.Count, Is.EqualTo(3));
            ResourceDictionary[] themes = merged.Where(d => d != other).ToArray();
            Assert.That(themes.Length, Is.EqualTo(2));
            Assert.That(themes[0].Source.OriginalString, Does.EndWith("Themes/Dark.xaml"));
            Assert.That(themes[0].Contains("EditorBackground"), Is.True);
            Assert.That(themes[1].Source.OriginalString, Does.EndWith("Themes/Controls.xaml"));
        }
    }
}

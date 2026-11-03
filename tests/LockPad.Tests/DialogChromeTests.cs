using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using LockPad.App;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// Every owned dialog has a 1 px DialogBorder edge, so a dark dialog over the dark main window (or a light one over
    /// the light window) never blends into the background.
    /// </summary>
    [TestFixture]
    public class DialogChromeTests
    {
        private static readonly string[] Themes = { "Light", "Dark" };

        // Every surface a dialog can sit on or in: its own background and the main window's areas.
        private static readonly string[] Backgrounds = { "WindowBackground", "PanelBackground", "EditorBackground" };

        private static string AppDir()
        {
            DirectoryInfo d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "LockPad.sln"))) d = d.Parent;
            Assert.That(d, Is.Not.Null, "LockPad.sln not found");
            return Path.Combine(d.FullName, "src", "LockPad.App");
        }

        private static void RegisterPackScheme()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            GC.KeepAlive(Application.Current);
        }

        private static ResourceDictionary Theme(string name)
        {
            return new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LockPad;component/Themes/" + name + ".xaml")
            };
        }

        /// <summary>WCAG relative luminance of an sRGB colour.</summary>
        private static double Luminance(Color c)
        {
            Func<byte, double> lin = b =>
            {
                double s = b / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            };
            return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
        }

        private static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [Apartment(ApartmentState.STA)]
        public void DialogBorder_ExistsInTheTheme_AndContrastsWithEveryBackground(string name)
        {
            RegisterPackScheme();
            ResourceDictionary dict = Theme(name);
            var border = dict["DialogBorder"] as SolidColorBrush;
            Assert.That(border, Is.Not.Null, name + " defines a DialogBorder SolidColorBrush");
            Assert.That(border.Color.A, Is.EqualTo(255), "opaque");
            foreach (string key in Backgrounds)
            {
                Color bg = ((SolidColorBrush)dict[key]).Color;
                Assert.That(Contrast(border.Color, bg), Is.GreaterThanOrEqualTo(3.0), name + ": DialogBorder vs " + key);
            }
        }

        private static IEnumerable<string> DialogXamlFiles()
        {
            return Directory.GetFiles(Path.Combine(AppDir(), "Views"), "*Window.xaml");
        }

        [Test]
        public void EveryDialogXaml_HasA1pxDialogBorder()
        {
            List<string> files = DialogXamlFiles().ToList();
            Assert.That(files.Count, Is.GreaterThanOrEqualTo(6));
            foreach (string f in files)
            {
                XElement root = XDocument.Load(f).Root;
                Assert.That(root.Name.LocalName, Is.EqualTo("Window"), Path.GetFileName(f));
                Assert.That((string)root.Attribute("BorderBrush"), Is.EqualTo("{DynamicResource DialogBorder}"),
                    Path.GetFileName(f));
                Assert.That((string)root.Attribute("BorderThickness"), Is.EqualTo("1"), Path.GetFileName(f));
            }
        }

        private static Window[] NewDialogs()
        {
            return new Window[]
            {
                new ConfirmDefaultNoWindow("Weak password", "m"),
                new ConfirmWindow("Delete note", "m"),
                new CloseChoiceWindow("t", "m"),
                new PromptWindow("Rename note", "Title:", "x"),
                new SettingsWindow(new SettingsViewModel(new AppSettings())),
                new ShortcutsWindow(),
                new AboutWindow(t => { }),
                new ChangePasswordWindow(null)
            };
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EveryOwnedDialog_RootUsesTheDialogBorderBrush_Thickness1()
        {
            RegisterPackScheme();
            Window[] dialogs = NewDialogs();
            try
            {
                // Every Window type in the app other than the main window is covered here.
                string[] windowTypes = typeof(MainWindow).Assembly.GetTypes()
                    .Where(t => typeof(Window).IsAssignableFrom(t) && t != typeof(MainWindow))
                    .Select(t => t.Name).OrderBy(n => n).ToArray();
                Assert.That(dialogs.Select(d => d.GetType().Name).OrderBy(n => n).ToArray(), Is.EqualTo(windowTypes));

                foreach (Window w in dialogs)
                {
                    string n = w.GetType().Name;
                    Assert.That(w.BorderThickness, Is.EqualTo(new Thickness(1)), n);
                    Assert.That(ReadLocalExpressionKey(w, Control.BorderBrushProperty), Is.EqualTo("DialogBorder"), n);
                }
            }
            finally
            {
                foreach (Window w in dialogs) w.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DialogBorder_FollowsTheThemeLive()
        {
            RegisterPackScheme();
            var dialog = new ConfirmDefaultNoWindow("Weak password", "m");
            try
            {
                foreach (string name in Themes.Concat(Themes))
                {
                    ResourceDictionary dict = Theme(name);
                    dialog.Resources.MergedDictionaries.Clear();
                    dialog.Resources.MergedDictionaries.Add(dict);
                    var shown = dialog.BorderBrush as SolidColorBrush;
                    Assert.That(shown, Is.Not.Null, name);
                    Assert.That(shown.Color, Is.EqualTo(((SolidColorBrush)dict["DialogBorder"]).Color), name);
                }
            }
            finally
            {
                dialog.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        /// <summary>The resource key of a {DynamicResource} set on <paramref name="p"/>.</summary>
        private static object ReadLocalExpressionKey(DependencyObject d, DependencyProperty p)
        {
            object local = d.ReadLocalValue(p);
            var expr = local as Expression;
            Assert.That(expr, Is.Not.Null, "a DynamicResource");
            return expr.GetType().GetProperty("ResourceKey").GetValue(expr);
        }
    }
}

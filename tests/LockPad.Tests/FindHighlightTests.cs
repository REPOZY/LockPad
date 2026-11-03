using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LockPad.App.Services;
using LockPad.App.ViewModels;
using LockPad.App.Views;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// UX round 3, V3 (bug): Enter in the find box must select the match so it is clearly visible while focus stays in
    /// the find box. Root cause: WPF does not render a selection that is set while the TextBox is unfocused (the find
    /// code selected while focus was in the find box), whatever the brush; and a selection that does become inactive is
    /// painted with the system inactive brush (#F0F0F0 at SelectionOpacity 0.4, i.e. #F9F9F9 on the white Light editor).
    /// The editor view is hosted in a real window placed off-screen and never activated, and the test compares renders.
    /// </summary>
    [TestFixture]
    public class FindHighlightTests
    {
        private const string Needle = "needle";

        private TempDir _dir;
        private Notebook _nb;
        private Window _window;

        [SetUp]
        public void SetUp()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            _dir = new TempDir();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null) _window.Close();
            _window = null;
            if (_nb != null) _nb.Dispose();
            _nb = null;
            _dir.Dispose();
            // Each test runs on its own STA thread and shows a real (off-screen) window there; shutting that thread's
            // dispatcher down lets the test AppDomain unload cleanly (as in DialogActivityTests).
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }

        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        }

        private static string LongText(out int index)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 300; i++)
            {
                if (i == 250) sb.Append("the ");
                sb.Append(i == 250 ? Needle + " is here" : "line " + i + " of plain text").Append("\r\n");
            }
            string text = sb.ToString();
            index = text.IndexOf(Needle, StringComparison.Ordinal);
            return text;
        }

        private EditorView ShowEditor(bool dark, out int index)
        {
            string text = LongText(out index);
            var store = new NotebookStore();
            string path = _dir.File("nb.kdbx");
            Credentials pw = new Credentials(Encoding.UTF8.GetBytes("pw"), null);
            using (store.Create(path, pw, TestKdf.Fast())) { }
            _nb = store.Open(path, new Credentials(Encoding.UTF8.GetBytes("pw"), null));
            Note note = _nb.AddNote("Long");
            _nb.UpdateNote(note.Id, "Long", text);
            var vm = new EditorViewModel(_nb, store, new FakeDialogService(), new SettingsStore(_dir.File("settings")),
                new AppSettings());

            var view = new EditorView { DataContext = vm };
            _window = new Window
            {
                Content = view, Width = 900, Height = 500, Left = -20000, Top = -20000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            ThemeService.ApplyTo(_window.Resources.MergedDictionaries, dark);
            _window.Show();
            Pump();
            return view;
        }

        private void Key(UIElement target, Key key)
        {
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window), 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
            Pump();
        }

        private static BitmapSource Render(FrameworkElement e)
        {
            var bmp = new RenderTargetBitmap((int)e.ActualWidth, (int)e.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(e);
            return bmp;
        }

        /// <summary>Pixels in <paramref name="area"/> that differ clearly (max channel difference 40) from
        /// <paramref name="background"/>.</summary>
        private static int VisiblyDifferent(BitmapSource bmp, Int32Rect area, Color background)
        {
            var px = new byte[area.Width * area.Height * 4];
            bmp.CopyPixels(area, px, area.Width * 4, 0);
            int n = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                int d = Math.Max(Math.Abs(px[i] - background.B),
                    Math.Max(Math.Abs(px[i + 1] - background.G), Math.Abs(px[i + 2] - background.R)));
                if (d >= 40) n++;
            }
            return n;
        }

        // Separate methods, not [TestCase]: NUnit runs the cases of one method on the same STA thread, whose dispatcher
        // TearDown shuts down.
        [Test]
        [Apartment(ApartmentState.STA)]
        public void EnterInFindBox_Light_SelectsTheMatch_VisiblyWhileUnfocused_AndScrollsToIt()
        {
            AssertFindHighlightsTheMatch(false);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EnterInFindBox_Dark_SelectsTheMatch_VisiblyWhileUnfocused_AndScrollsToIt()
        {
            AssertFindHighlightsTheMatch(true);
        }

        private void AssertFindHighlightsTheMatch(bool dark)
        {
            int index;
            EditorView view = ShowEditor(dark, out index);
            var editor = (TextBox)view.FindName("Editor");
            var findBox = (TextBox)view.FindName("FindBox");

            view.ShowFind();
            findBox.Text = Needle;
            Key(findBox, System.Windows.Input.Key.Enter);

            Assert.That(editor.SelectionStart, Is.EqualTo(index));
            Assert.That(editor.SelectionLength, Is.EqualTo(Needle.Length));
            Assert.That(FocusManager.GetFocusedElement(_window), Is.SameAs(findBox), "focus stays in the find box");
            Assert.That(editor.VerticalOffset, Is.GreaterThan(0), "scrolled to the match");
            Assert.That(editor.IsKeyboardFocusWithin, Is.False, "the editor is unfocused (inactive selection)");

            Rect start = editor.GetRectFromCharacterIndex(index);
            Rect end = editor.GetRectFromCharacterIndex(index + Needle.Length);
            Point topLeft = editor.TransformToAncestor(view).Transform(start.TopLeft);
            var area = new Int32Rect((int)Math.Ceiling(topLeft.X) + 1, (int)Math.Ceiling(topLeft.Y) + 1,
                (int)(end.Left - start.Left) - 2, (int)start.Height - 2);
            Assert.That(area.Width, Is.GreaterThan(10));
            Assert.That(area.Height, Is.GreaterThan(5));
            Color background = ((SolidColorBrush)_window.FindResource("EditorBackground")).Color;

            int selected = VisiblyDifferent(Render(view), area, background);
            editor.Select(index, 0);
            Pump();
            int unselected = VisiblyDifferent(Render(view), area, background); // the glyphs only

            int areaPixels = area.Width * area.Height;
            Assert.That(selected - unselected, Is.GreaterThan(areaPixels / 2),
                "the selection highlight must be clearly visible (" + (dark ? "Dark" : "Light") + ")");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EscInFindBox_ClosesTheBar_KeepsTheMatchSelected_FocusBackInTheEditor()
        {
            int index;
            EditorView view = ShowEditor(false, out index);
            var editor = (TextBox)view.FindName("Editor");
            var findBox = (TextBox)view.FindName("FindBox");
            var findBar = (FrameworkElement)view.FindName("FindBar");
            view.ShowFind();
            findBox.Text = Needle;
            Key(findBox, System.Windows.Input.Key.Enter);

            Key(findBox, System.Windows.Input.Key.Escape);

            Assert.That(findBar.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(editor.SelectionStart, Is.EqualTo(index));
            Assert.That(editor.SelectionLength, Is.EqualTo(Needle.Length));
            Assert.That(FocusManager.GetFocusedElement(_window), Is.SameAs(editor));
        }
    }
}

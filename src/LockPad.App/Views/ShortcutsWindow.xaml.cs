using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LockPad.App.Services;

namespace LockPad.App.Views
{
    /// <summary>
    /// Read-only list of every keyboard shortcut, grouped, in two columns (Shortcut, Action). Built from
    /// <see cref="ShortcutTable"/>, the single source of truth that a test checks against the real bindings.
    /// </summary>
    public partial class ShortcutsWindow : Window
    {
        public ShortcutsWindow()
        {
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            Loaded += (s, e) => CloseButton.Focus();
            PreviewKeyDown += (s, e) => { if (ScrollWithKey(GroupsScroller, e.Key)) e.Handled = true; };
            string group = null;
            Grid rows = null;
            foreach (ShortcutEntry e in ShortcutTable.Entries)
            {
                if (e.Group != group)
                {
                    group = e.Group;
                    GroupsPanel.Children.Add(Header(group));
                    rows = NewRows();
                    GroupsPanel.Children.Add(rows);
                    if (group == "Security") GroupsPanel.Children.Add(Note(ShortcutTable.LockNote));
                }
                AddRow(rows, e.Keys, e.Action);
            }
        }

        /// <summary>
        /// Never taller than the owner (the main window), so the list fits inside it whatever its size; the list
        /// scrolls when it does not fit. <paramref name="ownerHeight"/> is the owner's ActualHeight; 0 or NaN (no
        /// visible owner, or a minimized one) leaves the height uncapped.
        /// </summary>
        internal void FitInside(double ownerHeight)
        {
            MaxHeight = ownerHeight > 0 ? ownerHeight : double.PositiveInfinity;
        }

        /// <summary>Scrolls <paramref name="scroller"/> for the arrow, Page and Home/End keys; true when
        /// <paramref name="key"/> was one of them. Tab, Enter, Esc and the rest keep their normal meaning.</summary>
        internal static bool ScrollWithKey(ScrollViewer scroller, Key key)
        {
            switch (key)
            {
                case Key.Down: scroller.LineDown(); return true;
                case Key.Up: scroller.LineUp(); return true;
                case Key.PageDown: scroller.PageDown(); return true;
                case Key.PageUp: scroller.PageUp(); return true;
                case Key.Home: scroller.ScrollToHome(); return true;
                case Key.End: scroller.ScrollToEnd(); return true;
                default: return false;
            }
        }

        private static TextBlock Header(string text)
        {
            var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "EditorForeground");
            return t;
        }

        private static TextBlock Note(string text)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeground");
            return t;
        }

        private static Grid NewRows()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private static void AddRow(Grid g, string keys, string action)
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock { Text = keys, Margin = new Thickness(0, 2, 8, 2), TextWrapping = TextWrapping.Wrap };
            k.SetResourceReference(TextBlock.ForegroundProperty, "EditorForeground");
            var a = new TextBlock { Text = action, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
            a.SetResourceReference(TextBlock.ForegroundProperty, "EditorForeground");
            Grid.SetRow(k, row);
            Grid.SetRow(a, row);
            Grid.SetColumn(a, 1);
            g.Children.Add(k);
            g.Children.Add(a);
        }
    }
}

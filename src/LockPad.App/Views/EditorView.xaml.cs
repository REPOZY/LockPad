using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LockPad.App.ViewModels;

namespace LockPad.App.Views
{
    /// <summary>
    /// View-only behaviour: Ctrl+wheel zoom and the find bar. Nothing here touches the clipboard; copy/cut from the
    /// TextBoxes is handled by TextBoxClipboardGuard.
    /// </summary>
    public partial class EditorView : UserControl
    {
        public EditorView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (Editor.IsEnabled) Editor.Focus();
                else NotesList.Focus();
            };
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>Moves keyboard focus to an element. Test seam: real focus needs a shown window.</summary>
        internal Action<UIElement> FocusElement { get; set; } = el => el.Focus();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var old = e.OldValue as EditorViewModel;
            if (old != null)
            {
                old.NoteDeleted -= OnNoteDeleted;
                old.NoteRestored -= OnNoteDeleted;
                old.NoteCreated -= OnNoteCreated;
            }
            var vm = e.NewValue as EditorViewModel;
            if (vm != null)
            {
                vm.NoteDeleted += OnNoteDeleted;
                vm.NoteRestored += OnNoteDeleted;
                vm.NoteCreated += OnNoteCreated;
            }
        }

        /// <summary>After a new note is named and selected: focus the editor text box so typing goes into the note.
        /// Deferred to Input priority so the editor is enabled (bound to HasSelection) first.</summary>
        private void OnNoteCreated(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (Editor.IsEnabled) FocusElement(Editor);
            }));
        }

        /// <summary>After a delete the focused list item is gone: focus the newly selected one (or the list). After an
        /// undo delete: focus the restored note's item, so focus stays in the list (Ctrl+Z again restores the next).</summary>
        private void OnNoteDeleted(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                var vm = DataContext as EditorViewModel;
                var item = vm == null || vm.SelectedNote == null
                    ? null
                    : NotesList.ItemContainerGenerator.ContainerFromItem(vm.SelectedNote) as ListBoxItem;
                if (item != null) FocusElement(item);
                else FocusElement(NotesList);
            }));
        }

        private void OnEditorMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            var vm = DataContext as EditorViewModel;
            if (vm == null) return;
            ICommand command = e.Delta > 0 ? vm.ZoomInCommand : vm.ZoomOutCommand;
            if (command.CanExecute(null)) command.Execute(null);
            e.Handled = true;
        }

        /// <summary>Shows the find bar and focuses it. Called by MainWindow for Ctrl+F.</summary>
        public void ShowFind()
        {
            FindBar.Visibility = Visibility.Visible;
            FindStatus.Text = "";
            FindBox.Focus();
            FindBox.SelectAll();
        }

        private void OnFindBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FindNext();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseFind();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Selects the next case-insensitive match after the current selection, wrapping around, and scrolls it into
        /// view. Focus stays in the find box so Enter repeats; the editor shows its selection while unfocused
        /// (IsInactiveSelectionHighlightEnabled, with the theme's EditorSelectionBrush so it is visible in Light too).
        /// WPF does not render a selection that is set while the TextBox is unfocused (V3 root cause), so the editor
        /// takes focus for the Select and hands it straight back to the find box.
        /// </summary>
        private void FindNext()
        {
            string query = FindBox.Text;
            int from = Editor.SelectionStart + Editor.SelectionLength;
            int index = EditorViewModel.FindNext(Editor.Text, query, from);
            if (index < 0)
            {
                FindStatus.Text = string.IsNullOrEmpty(query) ? "" : "No matches";
                return;
            }
            FindStatus.Text = "";
            int line = Editor.GetLineIndexFromCharacterIndex(index);
            if (line >= 0) Editor.ScrollToLine(line);
            Editor.UpdateLayout(); // the match's line is laid out before the selection is drawn
            Editor.Focus();
            Editor.Select(index, query.Length);
            FindBox.Focus();
        }

        private void CloseFind()
        {
            FindBar.Visibility = Visibility.Collapsed;
            FindStatus.Text = "";
            if (Editor.IsEnabled) Editor.Focus();
            else NotesList.Focus();
        }
    }
}

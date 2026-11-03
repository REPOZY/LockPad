using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LockPad.App.Services
{
    /// <summary>
    /// Routes every Copy and Cut from any <see cref="TextBox"/> in the process through
    /// <see cref="IClipboardService.CopyProtected"/>: keyboard (Ctrl+C/X, Ctrl+Insert, Shift+Delete), the default
    /// context menu, and any other invoker of ApplicationCommands.Copy/Cut. The commands are handled in the Preview
    /// stage, so the built-in TextBox copy (which would put unprotected text on the clipboard) never runs.
    /// Why not CommandManager.RegisterClassCommandBinding: TextBox's static constructor registers the built-in
    /// Copy/Cut bindings on typeof(TextBox) itself, and within one class the first registered binding wins, so a
    /// later class binding is never reached while the built-in one can execute.
    /// It also cancels every WPF-internal TextBox copy (DataObject.Copying): drag-out of selected text, and
    /// TextBox.Copy()/Cut() called from code, both of which bypass the commands. Paste and drop into a TextBox stay
    /// allowed. PasswordBox is not affected (WPF never copies from it).
    /// Cancelling every DataObject.Copying also blocks drag-move of selected text within a TextBox (WPF starts that
    /// drag with the same copying step). This is deliberate: a drag puts the text in an OLE data object that other
    /// applications and clipboard tools can read without the protection formats. Do not narrow this handler to
    /// e.IsDragDrop or to drags that leave the TextBox.
    /// </summary>
    public static class TextBoxClipboardGuard
    {
        internal const string CopyFailedTitle = "Copy failed";
        internal const string CopyFailedMessage = "The text could not be copied. Try again.";

        private static readonly object Sync = new object();
        private static IClipboardService _clipboard;
        private static IDialogService _dialogs;
        private static bool _registered;

        /// <summary>
        /// Call once at startup on the UI thread. Class handlers are permanent for the process; a later call only
        /// replaces the services they use.
        /// </summary>
        public static void Register(IClipboardService clipboard, IDialogService dialogs)
        {
            if (clipboard == null) throw new ArgumentNullException(nameof(clipboard));
            if (dialogs == null) throw new ArgumentNullException(nameof(dialogs));

            lock (Sync)
            {
                _clipboard = clipboard;
                _dialogs = dialogs;
                if (_registered) return;
                _registered = true;
            }

            // Every RoutedCommand execution (key gesture, menu item, code) raises PreviewExecuted on its target before
            // Executed; the built-in TextBox bindings only handle Executed. Handling the preview ends the command.
            EventManager.RegisterClassHandler(typeof(TextBox), CommandManager.PreviewCanExecuteEvent,
                new CanExecuteRoutedEventHandler(OnPreviewCanExecute));
            EventManager.RegisterClassHandler(typeof(TextBox), CommandManager.PreviewExecutedEvent,
                new ExecutedRoutedEventHandler(OnPreviewExecuted));
            EventManager.RegisterClassHandler(typeof(TextBox), DataObject.CopyingEvent,
                new DataObjectCopyingEventHandler(OnCopying));
        }

        private static bool IsGuarded(ICommand command)
        {
            return command == ApplicationCommands.Copy || command == ApplicationCommands.Cut;
        }

        private static void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null || !IsGuarded(e.Command)) return;
            // Cut stays available on a read-only box: it copies and deletes nothing.
            e.CanExecute = box.SelectionLength > 0;
            e.Handled = true;
        }

        private static void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null || !IsGuarded(e.Command)) return;
            e.Handled = true;
            bool copied = TryCopy(box.SelectedText);
            // Cut deletes only after the copy succeeded, and never on a read-only box.
            if (copied && e.Command == ApplicationCommands.Cut && !box.IsReadOnly) box.SelectedText = "";
        }

        private static void OnCopying(object sender, DataObjectCopyingEventArgs e)
        {
            // Drag-out, drag-move within the box (e.IsDragDrop) and code-initiated copies alike: only CopyProtected may
            // place TextBox text on the clipboard, and it does not raise this event. Must not be narrowed (see above).
            e.CancelCommand();
        }

        /// <summary>False when nothing was copied (empty selection or failure). A copy failure is reported with a
        /// message that never contains the text.</summary>
        private static bool TryCopy(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            IClipboardService clipboard;
            IDialogService dialogs;
            lock (Sync)
            {
                clipboard = _clipboard;
                dialogs = _dialogs;
            }
            try
            {
                clipboard.CopyProtected(text);
                return true;
            }
            catch (Exception)
            {
                dialogs.ShowError(CopyFailedTitle, CopyFailedMessage);
                return false;
            }
        }
    }
}

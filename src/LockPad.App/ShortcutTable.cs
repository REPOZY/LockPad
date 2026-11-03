using System.Collections.Generic;
using System.Windows.Input;

namespace LockPad.App
{
    /// <summary>How a shortcut is implemented, which decides how the binding parity test checks it.</summary>
    internal enum ShortcutKind
    {
        /// <summary>A KeyBinding in MainWindow or EditorView XAML; every gesture must be bound there.</summary>
        KeyBinding,
        /// <summary>A routed command's own gesture, handled through a CommandBinding (Ctrl+F = ApplicationCommands.Find).</summary>
        Command,
        /// <summary>Handled in code (a PreviewKeyDown handler), not through an InputBinding.</summary>
        Code,
        /// <summary>Built into a WPF control (TextBox undo), or a mouse gesture.</summary>
        BuiltIn,
    }

    /// <summary>A key and its modifiers.</summary>
    internal sealed class ShortcutGesture
    {
        public ShortcutGesture(Key key, ModifierKeys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public Key Key { get; }
        public ModifierKeys Modifiers { get; }
    }

    /// <summary>One row of the Shortcuts window.</summary>
    internal sealed class ShortcutEntry
    {
        public ShortcutEntry(string group, string keys, string action, ShortcutKind kind, params ShortcutGesture[] gestures)
        {
            Group = group;
            Keys = keys;
            Action = action;
            Kind = kind;
            Gestures = gestures;
        }

        public string Group { get; }
        /// <summary>The keys as shown to the user.</summary>
        public string Keys { get; }
        public string Action { get; }
        public ShortcutKind Kind { get; }
        public IReadOnlyList<ShortcutGesture> Gestures { get; }
    }

    /// <summary>
    /// The one source of truth for LockPad's keyboard shortcuts: the Shortcuts window is built from it, and a test checks
    /// it against the KeyBindings actually defined in MainWindow and EditorView, so the list cannot drift.
    /// </summary>
    internal static class ShortcutTable
    {
        internal const string LockNote =
            "LockPad also locks when Windows locks, when the PC sleeps, and after the idle time set in Settings.";

        private static ShortcutGesture G(Key key, ModifierKeys modifiers = ModifierKeys.None)
        {
            return new ShortcutGesture(key, modifiers);
        }

        internal static readonly IReadOnlyList<ShortcutEntry> Entries = new[]
        {
            new ShortcutEntry("Notes", "Ctrl+N", "New note", ShortcutKind.KeyBinding, G(Key.N, ModifierKeys.Control)),
            new ShortcutEntry("Notes", "F2", "Rename note", ShortcutKind.KeyBinding, G(Key.F2)),
            new ShortcutEntry("Notes", "Del (in the notes list)", "Delete note", ShortcutKind.KeyBinding, G(Key.Delete)),
            new ShortcutEntry("Notes", "Ctrl+Z (in the notes list)", "Undo delete", ShortcutKind.KeyBinding,
                G(Key.Z, ModifierKeys.Control)),
            new ShortcutEntry("Editing", "Ctrl+F", "Find in the note", ShortcutKind.Command, G(Key.F, ModifierKeys.Control)),
            new ShortcutEntry("Editing", "Enter (in the find box)", "Next match", ShortcutKind.Code, G(Key.Enter)),
            new ShortcutEntry("Editing", "Esc (in the find box)", "Close the find bar", ShortcutKind.Code, G(Key.Escape)),
            new ShortcutEntry("Editing", "Alt+Z", "Word wrap on or off", ShortcutKind.KeyBinding, G(Key.Z, ModifierKeys.Alt)),
            new ShortcutEntry("Editing", "Ctrl+Plus / Ctrl+Minus / Ctrl+0", "Zoom in / out / reset", ShortcutKind.KeyBinding,
                G(Key.OemPlus, ModifierKeys.Control), G(Key.Add, ModifierKeys.Control),
                G(Key.OemMinus, ModifierKeys.Control), G(Key.Subtract, ModifierKeys.Control),
                G(Key.D0, ModifierKeys.Control), G(Key.NumPad0, ModifierKeys.Control)),
            new ShortcutEntry("Editing", "Ctrl+mouse wheel", "Zoom", ShortcutKind.BuiltIn),
            new ShortcutEntry("Editing", "Ctrl+Z (in the editor)", "Undo typing", ShortcutKind.BuiltIn),
            new ShortcutEntry("File", "Ctrl+O", "Open another notebook", ShortcutKind.KeyBinding,
                G(Key.O, ModifierKeys.Control)),
            new ShortcutEntry("File", "Ctrl+S", "Save", ShortcutKind.KeyBinding, G(Key.S, ModifierKeys.Control)),
            new ShortcutEntry("File", "Ctrl+Shift+S", "Save As", ShortcutKind.KeyBinding,
                G(Key.S, ModifierKeys.Control | ModifierKeys.Shift)),
            new ShortcutEntry("Security", "Ctrl+L", "Lock now", ShortcutKind.Code, G(Key.L, ModifierKeys.Control)),
            new ShortcutEntry("Help", "F1", "This list of shortcuts", ShortcutKind.KeyBinding, G(Key.F1)),
        };
    }
}

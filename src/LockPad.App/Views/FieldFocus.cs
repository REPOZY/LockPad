using System.Windows.Controls;

namespace LockPad.App.Views
{
    /// <summary>Moves keyboard focus into an input box; shared by the unlock and create pages.</summary>
    internal static class FieldFocus
    {
        /// <summary>Focuses the box and selects its text, so the user can retype it or press its Browse button.</summary>
        public static void FocusAndSelect(Control box)
        {
            box.Focus();
            var text = box as TextBox;
            if (text != null) text.SelectAll();
        }
    }
}

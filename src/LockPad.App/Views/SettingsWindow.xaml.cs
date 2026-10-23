using System;
using System.Windows;
using System.Windows.Input;
using LockPad.App.Services;
using LockPad.App.ViewModels;

namespace LockPad.App.Views
{
    /// <summary>Modal settings dialog. Edits the view model only; the caller applies it when ShowDialog returns true.</summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(SettingsViewModel vm)
        {
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            ThemeBox.ItemsSource = Enum.GetValues(typeof(AppTheme));
            DataContext = vm;
            Loaded += (s, e) => { IdleLockBox.Focus(); IdleLockBox.SelectAll(); };
        }

        /// <summary>True when the text is a non-empty whole number that fits in an int.</summary>
        public static bool IsAcceptable(string text)
        {
            int value;
            return !string.IsNullOrEmpty(text) && IsDigitsOnly(text)
                && int.TryParse(text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private bool Reject(System.Windows.Controls.TextBox box)
        {
            if (!System.Windows.Controls.Validation.GetHasError(box) && IsAcceptable(box.Text)) return false;
            ErrorText.Text = "Enter a whole number in both fields.";
            ErrorText.Visibility = Visibility.Visible;
            box.Focus();
            box.SelectAll();
            return true;
        }

        /// <summary>True when every character is an ASCII digit. The empty string passes this keystroke/paste
        /// filter (the user may clear the box while typing); OK then rejects empty text via IsAcceptable.</summary>
        public static bool IsDigitsOnly(string text)
        {
            if (text == null) return false;
            foreach (char c in text)
                if (c < '0' || c > '9') return false;
            return true;
        }

        private void OnNumberTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsDigitsOnly(e.Text);
        }

        private void OnNumberKeyDown(object sender, KeyEventArgs e)
        {
            // Space does not raise PreviewTextInput.
            if (e.Key == Key.Space) e.Handled = true;
        }

        private void OnNumberPaste(object sender, DataObjectPastingEventArgs e)
        {
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (!IsDigitsOnly(text)) e.CancelCommand();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Reject(IdleLockBox) || Reject(ClipboardBox)) return;
            ErrorText.Visibility = Visibility.Collapsed;
            DialogResult = true;
        }
    }
}

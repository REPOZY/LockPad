using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LockPad.App.ViewModels;

namespace LockPad.App.Views
{
    public partial class UnlockView : UserControl
    {
        public UnlockView()
        {
            InitializeComponent();
            // The cursor starts in "Master password", also when the notebook was pre-filled from the command line.
            Loaded += (s, e) => FocusRequest(PasswordInput);
        }

        /// <summary>Moves keyboard focus into an input box and selects its text. Test seam: an unshown view cannot take
        /// focus.</summary>
        internal System.Action<Control> FocusRequest { get; set; } = FieldFocus.FocusAndSelect;

        /// <summary>Focuses the path box the view model's error belongs to, otherwise the password box (as before).</summary>
        internal void FocusErrorField()
        {
            var vm = DataContext as UnlockViewModel;
            if (vm == null) return;
            Control target;
            switch (vm.ErrorField)
            {
                case ErrorField.NotebookPath: target = NotebookPathBox; break;
                case ErrorField.KeyFilePath: target = KeyFilePathBox; break;
                default: target = PasswordInput; break;
            }
            FocusRequest(target);
        }

        private void OnPasswordKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Unlock();
        }

        private void OnUnlockClick(object sender, RoutedEventArgs e)
        {
            Unlock();
        }

        // The view model never throws; the password copy is disposed and the box cleared on every outcome.
        private async void Unlock()
        {
            var vm = DataContext as UnlockViewModel;
            if (vm == null || vm.IsBusy) return;
            SecureString password = PasswordInput.SecurePassword;
            try
            {
                await vm.UnlockAsync(password);
            }
            finally
            {
                password.Dispose();
                PasswordInput.Clear();
                if (IsLoaded) FocusErrorField(); // after IsBusy cleared: the page is disabled while busy
            }
        }
    }
}

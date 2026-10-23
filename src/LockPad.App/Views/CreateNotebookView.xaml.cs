using System.Security;
using System.Windows;
using System.Windows.Controls;
using LockPad.App.ViewModels;

namespace LockPad.App.Views
{
    public partial class CreateNotebookView : UserControl
    {
        public CreateNotebookView()
        {
            InitializeComponent();
        }

        /// <summary>Moves keyboard focus into an input box and selects its text. Test seam: an unshown view cannot take
        /// focus.</summary>
        internal System.Action<Control> FocusRequest { get; set; } = FieldFocus.FocusAndSelect;

        /// <summary>Focuses the box the view model's error belongs to; without an error, focus stays where it is.</summary>
        internal void FocusErrorField()
        {
            var vm = DataContext as CreateNotebookViewModel;
            if (vm == null) return;
            Control target;
            switch (vm.ErrorField)
            {
                case ErrorField.NotebookPath: target = NotebookPathBox; break;
                case ErrorField.KeyFilePath: target = KeyFilePathBox; break;
                case ErrorField.Password: target = PasswordInput; break;
                default: return;
            }
            FocusRequest(target);
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as CreateNotebookViewModel;
            if (vm == null) return;
            using (SecureString password = PasswordInput.SecurePassword)
                vm.UpdateStrength(password);
        }

        // The view model never throws; both password copies are disposed and both boxes cleared on every outcome. The
        // error's box is focused afterwards: the page is disabled while busy, so it cannot take focus earlier.
        private async void OnCreateClick(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as CreateNotebookViewModel;
            if (vm == null || vm.IsBusy) return;
            SecureString password = PasswordInput.SecurePassword;
            SecureString confirm = ConfirmInput.SecurePassword;
            try
            {
                await vm.CreateAsync(password, confirm);
            }
            finally
            {
                password.Dispose();
                confirm.Dispose();
                PasswordInput.Clear();
                ConfirmInput.Clear();
                if (IsLoaded) FocusErrorField();
            }
        }
    }
}

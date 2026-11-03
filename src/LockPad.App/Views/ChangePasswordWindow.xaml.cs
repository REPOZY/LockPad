using System;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LockPad.App.Services;
using LockPad.App.ViewModels;

namespace LockPad.App.Views
{
    /// <summary>
    /// Modal Change master password dialog. The passwords leave the boxes only as <see cref="SecureString"/> copies,
    /// which are disposed after every attempt. Closes (DialogResult true) only when the view model accepted the input;
    /// otherwise its inline error is shown and the dialog stays open. Cancel and Esc close it without a change.
    /// </summary>
    public partial class ChangePasswordWindow : Window
    {
        /// <param name="vm">The dialog's view model; null only for chrome tests that need no data.</param>
        public ChangePasswordWindow(ChangePasswordViewModel vm)
        {
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            DataContext = vm;
            if (vm != null) vm.Abandoned += OnAbandoned;
            Loaded += (s, e) => CurrentInput.Focus();
            // Caps Lock may have been toggled while another window had the keyboard.
            Activated += (s, e) => RefreshCapsLock(Keyboard.FocusedElement);
        }

        /// <summary>Reads the Caps Lock state; a seam so tests need no real keyboard (fix round 2, X5).</summary>
        internal Func<bool> CapsLockState = () => Keyboard.IsKeyToggled(Key.CapsLock);

        /// <summary>True while the "Caps Lock is on." line is shown.</summary>
        internal bool IsCapsLockWarningShown
        {
            get { return CapsLockWarning.Visibility == Visibility.Visible; }
        }

        /// <summary>
        /// Shows the Caps Lock line only while Caps Lock is on and <paramref name="focused"/> (the element that has, or
        /// is about to get, the keyboard focus) is one of the three password boxes.
        /// </summary>
        internal void RefreshCapsLock(object focused)
        {
            bool inBox = ReferenceEquals(focused, CurrentInput) || ReferenceEquals(focused, NewInput)
                || ReferenceEquals(focused, RepeatInput);
            CapsLockWarning.Visibility = inBox && CapsLockState() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSecretFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        {
            RefreshCapsLock(e.NewFocus);
        }

        private void OnSecretKeyUp(object sender, KeyEventArgs e)
        {
            RefreshCapsLock(sender);
        }

        /// <summary>The editor was obscured while this dialog is open (fix round 2, X1): nothing typed stays in it.</summary>
        private void OnAbandoned(object sender, System.EventArgs e)
        {
            CurrentInput.Clear();
            NewInput.Clear();
            RepeatInput.Clear();
        }

        private ChangePasswordViewModel ViewModel
        {
            get { return DataContext as ChangePasswordViewModel; }
        }

        private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
        {
            ChangePasswordViewModel vm = ViewModel;
            if (vm == null) return;
            using (SecureString password = NewInput.SecurePassword)
                vm.UpdateStrength(password);
        }

        private void OnChange(object sender, RoutedEventArgs e)
        {
            ChangePasswordViewModel vm = ViewModel;
            if (vm == null) return;
            bool accepted;
            using (SecureString current = CurrentInput.SecurePassword)
            using (SecureString newPassword = NewInput.SecurePassword)
            using (SecureString repeat = RepeatInput.SecurePassword)
                accepted = vm.TryAccept(current, newPassword, repeat);

            if (accepted)
            {
                CurrentInput.Clear();
                NewInput.Clear();
                RepeatInput.Clear();
                DialogResult = true;
                return;
            }

            PasswordBox target;
            switch (vm.ErrorField)
            {
                case ChangePasswordField.Current:
                    target = CurrentInput;
                    if (vm.ClearErrorField) CurrentInput.Clear();
                    break;
                case ChangePasswordField.New:
                    target = NewInput;
                    if (vm.ClearErrorField)
                    {
                        NewInput.Clear();
                        RepeatInput.Clear();
                    }
                    break;
                default:
                    return;
            }
            target.Focus();
        }
    }
}

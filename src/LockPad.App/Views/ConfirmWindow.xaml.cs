using System.Windows;
using LockPad.App.Services;

namespace LockPad.App.Views
{
    /// <summary>Yes/No confirmation: Enter answers Yes; Esc and closing the window answer No.</summary>
    public partial class ConfirmWindow : Window
    {
        public ConfirmWindow(string title, string message)
        {
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            Title = title ?? string.Empty;
            MessageText.Text = message ?? string.Empty;
            Loaded += (s, e) => YesButton.Focus();
        }

        public bool Confirmed { get; private set; }

        private void OnYes(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }
    }
}

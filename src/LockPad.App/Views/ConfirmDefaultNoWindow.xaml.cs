using System.Windows;
using LockPad.App.Services;

namespace LockPad.App.Views
{
    /// <summary>Yes/No confirmation whose answer is No unless Yes is clicked (Enter, Esc and closing all answer No).</summary>
    public partial class ConfirmDefaultNoWindow : Window
    {
        public ConfirmDefaultNoWindow(string title, string message)
        {
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            Title = title ?? string.Empty;
            MessageText.Text = message ?? string.Empty;
            Loaded += (s, e) => NoButton.Focus();
        }

        public bool Confirmed { get; private set; }

        private void OnYes(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }
    }
}

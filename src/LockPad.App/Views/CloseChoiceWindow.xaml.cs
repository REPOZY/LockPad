using System.Windows;
using LockPad.App.Services;

namespace LockPad.App.Views
{
    /// <summary>Save As... / Close without saving / Cancel after a failed save on close. <see cref="Choice"/> stays
    /// Cancel unless one of the first two buttons is clicked.</summary>
    public partial class CloseChoiceWindow : Window
    {
        public CloseChoiceWindow(string title, string message)
        {
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            Title = title ?? string.Empty;
            MessageText.Text = message ?? string.Empty;
            Loaded += (s, e) => CancelButton.Focus();
        }

        public CloseSaveFailureChoice Choice { get; private set; } = CloseSaveFailureChoice.Cancel;

        private void OnSaveAs(object sender, RoutedEventArgs e)
        {
            Choice = CloseSaveFailureChoice.SaveAs;
            Close();
        }

        private void OnCloseWithoutSaving(object sender, RoutedEventArgs e)
        {
            Choice = CloseSaveFailureChoice.CloseWithoutSaving;
            Close();
        }
    }
}

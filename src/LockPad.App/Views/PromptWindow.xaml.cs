using System.Windows;
using LockPad.App.Services;

namespace LockPad.App.Views
{
    public partial class PromptWindow : Window
    {
        public PromptWindow(string title, string label, string initialValue)
        {
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            Title = title ?? string.Empty;
            LabelText.Text = label ?? string.Empty;
            ValueBox.Text = initialValue ?? string.Empty;
            Loaded += (s, e) => { ValueBox.Focus(); ValueBox.SelectAll(); };
        }

        public string Value { get; private set; }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Value = ValueBox.Text;
            DialogResult = true;
        }
    }
}

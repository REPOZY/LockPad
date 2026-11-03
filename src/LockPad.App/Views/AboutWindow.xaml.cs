using System;
using System.Windows;
using LockPad.App.Services;

namespace LockPad.App.Views
{
    /// <summary>
    /// Help > About LockPad: what LockPad is, its GitHub address (<see cref="ProjectInfo.RepositoryUrl"/>) as text with
    /// a Copy link button, and the license.
    /// </summary>
    public partial class AboutWindow : Window
    {
        internal const string CopiedText = "Link copied.";
        internal const string CopyFailedText = "Could not copy. Try again.";

        private readonly Action<string> _copyText;

        /// <summary>The real window: Copy link puts the address on the clipboard as ordinary text.</summary>
        public AboutWindow() : this(CopyToClipboard)
        {
        }

        /// <summary><paramref name="copyText"/> puts text on the clipboard (a test seam).</summary>
        internal AboutWindow(Action<string> copyText)
        {
            if (copyText == null) throw new ArgumentNullException(nameof(copyText));
            InitializeComponent();
            DarkTitleBar.Attach(this);
            CaptureProtection.Attach(this);
            _copyText = copyText;
            UrlText.Text = ProjectInfo.RepositoryUrl;
            Loaded += (s, e) => CloseButton.Focus();
        }

        // The address is public, so it is copied as ordinary text: no protection formats and no automatic clear (the
        // user may paste it into a browser minutes later). Replacing the clipboard also replaces any secret copied
        // earlier; LockPad's own clear on lock then finds foreign content and leaves the address alone.
        private static void CopyToClipboard(string text)
        {
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text, false), true);
        }

        private void OnCopyLink(object sender, RoutedEventArgs e)
        {
            try
            {
                _copyText(ProjectInfo.RepositoryUrl);
                CopyStatusText.Text = CopiedText;
            }
            catch (Exception)
            {
                // Typically another program holding the clipboard (WPF already retried). The address stays readable.
                CopyStatusText.Text = CopyFailedText;
            }
        }
    }
}

using System.Windows;
using Wpf.Ui.Controls;

namespace Midnight.Views
{
    public partial class PasteCookieContentDialog : ContentDialog
    {
        /// <summary>The trimmed cookie value entered by the user, or empty string if cancelled.</summary>
        public string Cookie { get; private set; } = string.Empty;

        public PasteCookieContentDialog(ContentDialogHost presenter) : base(presenter)
        {
            InitializeComponent();
            Loaded += (_, _) => TxtCookie.Focus();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            string cookie = TxtCookie.Text.Trim();

            if (string.IsNullOrWhiteSpace(cookie))
            {
                ShowError("Please paste a cookie before continuing.");
                return;
            }

            // Basic sanity check — the real cookie always starts with this prefix
            if (!cookie.Contains("WARNING") && !cookie.StartsWith("_|WARNING") && cookie.Length < 100)
            {
                ShowError("That doesn't look like a valid .ROBLOSECURITY cookie. Make sure you copied the entire value.");
                return;
            }

            Cookie = cookie;
            Hide();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void ShowError(string message)
        {
            TxtErrorMessage.Text = message;
            ErrorBanner.Visibility = Visibility.Visible;
        }
    }
}


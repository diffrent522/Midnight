using System;
using System.Windows;
using System.Windows.Input;
using Midnight.Services;
using Wpf.Ui.Controls;

namespace Midnight.Views
{
    public partial class UsernamePasswordContentDialog : ContentDialog
    {
        private readonly RobloxRequestService _requestService = new();

        /// <summary>The .ROBLOSECURITY cookie obtained after a successful login.</summary>
        public string ScrapedCookie { get; private set; } = string.Empty;

        public UsernamePasswordContentDialog(ContentDialogHost presenter) : base(presenter)
        {
            InitializeComponent();
            Loaded += (_, _) => TxtUsername.Focus();
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            await AttemptLogin();
        }

        private void TxtField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                _ = AttemptLogin();
        }

        private void TxtPassword_Changed(object sender, RoutedEventArgs e) { /* no-op, just keeps XAML valid */ }

        private async System.Threading.Tasks.Task AttemptLogin()
        {
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ShowError("Please enter both a username and a password.");
                return;
            }

            SetBusy(true);
            HideError();

            try
            {
                var (success, result) = await _requestService.LoginWithCredentialsAsync(username, password);

                if (success)
                {
                    ScrapedCookie = result;
                    LogService.Log($"Credentials login succeeded for '{username}'.", LogLevel.Success, "Login");
                    Hide();
                }
                else
                {
                    ShowError(result);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Unexpected error: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            BtnLogin.IsEnabled = !busy;
            BtnCancel.IsEnabled = !busy;
            TxtUsername.IsEnabled = !busy;
            TxtPassword.IsEnabled = !busy;
            LoadingPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            TxtErrorMessage.Text = message;
            ErrorBanner.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorBanner.Visibility = Visibility.Collapsed;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}


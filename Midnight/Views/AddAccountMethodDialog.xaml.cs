using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Midnight.Views
{
    public enum AddAccountMethod
    {
        None,
        BrowserLogin,
        PasteCookie,
        UsernamePassword
    }

    public partial class AddAccountMethodDialog : ContentDialog
    {
        public AddAccountMethod ChosenMethod { get; private set; } = AddAccountMethod.None;

        public AddAccountMethodDialog(ContentDialogHost presenter) : base(presenter)
        {
            InitializeComponent();
        }

        private void CardBrowser_Click(object sender, MouseButtonEventArgs e)
        {
            ChosenMethod = AddAccountMethod.BrowserLogin;
            Hide();
        }

        private void CardCookie_Click(object sender, MouseButtonEventArgs e)
        {
            ChosenMethod = AddAccountMethod.PasteCookie;
            Hide();
        }

        private void CardPassword_Click(object sender, MouseButtonEventArgs e)
        {
            ChosenMethod = AddAccountMethod.UsernamePassword;
            Hide();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ChosenMethod = AddAccountMethod.None;
            Hide();
        }
    }
}


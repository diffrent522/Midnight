using System.Windows;
using Wpf.Ui.Controls;
using Midnight.ViewModels;
using Midnight.Services;

namespace Midnight.Views
{
    public partial class MainWindow : FluentWindow
    {
        private readonly MainViewModel _vm;
        private bool _forceClose;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                this.Icon = new System.Windows.Media.Imaging.BitmapImage(new System.Uri("pack://application:,,,/Midnight;component/Resources/app.ico"));
                ApplicationTitleBar.Icon = new Wpf.Ui.Controls.ImageIcon { Source = this.Icon };
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load app icon: {ex.Message}");
            }

            _vm = new MainViewModel();
            _vm.LaunchRequested += OnLaunchRequested;
            _vm.NavigationHandler = pageType => RootNavigation.Navigate(pageType);
            DataContext = _vm;

            App.InitializeTray(_vm, new SecurityService());

            Loaded += (_, _) =>
            {
                RootNavigation.Navigate(typeof(AccountsPage));
                AdjustNavigationPaneWidth();
            };
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_forceClose && _vm.SettingsVM.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            App.DisposeTray();
            _vm.Dispose();
            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        private void AdjustNavigationPaneWidth()
        {
            double maxTextWidth = 0;
            var typeface = new System.Windows.Media.Typeface(
                new System.Windows.Media.FontFamily("Segoe UI"),
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            foreach (var item in RootNavigation.MenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Content is string text)
                {
                    var ft = new System.Windows.Media.FormattedText(
                        text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        14.0,
                        System.Windows.Media.Brushes.Black,
                        new System.Windows.Media.NumberSubstitution(),
                        1);

                    if (ft.Width > maxTextWidth)
                        maxTextWidth = ft.Width;
                }
            }

            RootNavigation.OpenPaneLength = maxTextWidth + 60;
        }

        private async void OnLaunchRequested(object? sender, System.EventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var dialog = new LaunchOptionsContentDialog(RootContentDialog, vm.GetAvailableRobloxVersions());
                await dialog.ShowAsync();

                if (dialog.Launched)
                {
                    await vm.LaunchSelectedAsync(dialog.PlaceId, dialog.JobId, dialog.AccessCode, dialog.SelectedVersion, dialog.LaunchDelaySeconds);
                }
            }
        }

        public ContentDialogHost GetDialogHost() => RootContentDialog;

        public async System.Threading.Tasks.Task ShowAlertAsync(string title, string message)
        {
            var dialog = new ContentDialog(RootContentDialog)
            {
                Title = title,
                Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }

        public async System.Threading.Tasks.Task<string?> ShowInputDialogAsync(string title, string message, string defaultValue = "")
        {
            var textBox = new Wpf.Ui.Controls.TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var dialog = new ContentDialog(RootContentDialog)
            {
                Title = title,
                Content = new System.Windows.Controls.StackPanel
                {
                    Children =
                    {
                        new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        textBox
                    }
                },
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? textBox.Text : null;
        }

        public async System.Threading.Tasks.Task ShowAddAccountDialogAsync()
        {
            if (DataContext is not MainViewModel vm) return;

            var methodDialog = new AddAccountMethodDialog(RootContentDialog);
            await methodDialog.ShowAsync();

            switch (methodDialog.ChosenMethod)
            {
                case AddAccountMethod.BrowserLogin:
                {
                    var loginDialog = new LoginContentDialog(RootContentDialog);
                    await loginDialog.ShowAsync();
                    if (!string.IsNullOrEmpty(loginDialog.ScrapedCookie))
                        await vm.AddNewAccountAsync(loginDialog.ScrapedCookie, loginDialog.ScrapedCookieExpiration);
                    break;
                }

                case AddAccountMethod.PasteCookie:
                {
                    var pasteDialog = new PasteCookieContentDialog(RootContentDialog);
                    await pasteDialog.ShowAsync();
                    if (!string.IsNullOrEmpty(pasteDialog.Cookie))
                        await vm.AddNewAccountAsync(pasteDialog.Cookie);
                    break;
                }

                case AddAccountMethod.UsernamePassword:
                {
                    var credDialog = new UsernamePasswordContentDialog(RootContentDialog);
                    await credDialog.ShowAsync();
                    if (!string.IsNullOrEmpty(credDialog.ScrapedCookie))
                        await vm.AddNewAccountAsync(credDialog.ScrapedCookie);
                    break;
                }
            }
        }

        public async void ShowLoginDialog() => await ShowAddAccountDialogAsync();
    }
}


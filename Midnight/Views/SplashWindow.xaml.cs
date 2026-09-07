using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Midnight.Services;

namespace Midnight.Views
{
    public partial class SplashWindow : Window
    {
        private const int MIN_DISPLAY_MS = 3500;

        public SplashWindow()
        {
            InitializeComponent();

            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
                }
                else
                {
                    VersionText.Text = "v2.0.0";
                }
            }
            catch
            {
                VersionText.Text = "v2.0.0";
            }

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var fadeIn = (Storyboard)Resources["FadeInStoryboard"];
            fadeIn.Begin();

            ((Storyboard)Resources["PulseStoryboard"]).Begin();
            ((Storyboard)Resources["FloatStoryboard"]).Begin();

            var startTime = DateTime.Now;

            await RunStartupSequenceAsync();

            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            var remaining = MIN_DISPLAY_MS - elapsed;
            if (remaining > 0)
                await Task.Delay((int)remaining);

            await FadeOutAndLaunchAsync();
        }

        private async Task RunStartupSequenceAsync()
        {
            await SetStatus("Loading settings…");
            await Task.Delay(400);

            await SetStatus("Checking for updates…");
            await Task.Delay(350);
            bool updateFound = await CheckForUpdatesAsync();

            if (updateFound) return;

            await SetStatus("Loading accounts…");
            await Task.Delay(500);

            await SetStatus("Initializing services…");
            await Task.Delay(400);

            await SetStatus("Starting Midnight…");
            await Task.Delay(350);
        }

        private async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var updateService = new UpdateService();
                var release = await updateService.CheckForUpdatesAsync(force: false);

                if (release != null)
                {
                    await SetStatus($"Update available: {release.TagName}");
                    await Task.Delay(800);

                    var result = MessageBox.Show(
                        $"Midnight {release.TagName} is available!\n\n{release.Body?.Split('\n')[0]}\n\nUpdate now?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        await SetStatus("Downloading update…");
                        await updateService.DownloadAndInstallAsync(release);
                        return true;
                    }
                }
                else
                {
                    await SetStatus("Up to date ✓");
                    await Task.Delay(400);
                }
            }
            catch
            {
                await SetStatus("Update check skipped.");
                await Task.Delay(300);
            }

            return false;
        }

        private async Task SetStatus(string message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                StatusText.Opacity = 0;
                StatusText.Text = message;
                var fadeStatus = (Storyboard)Resources["StatusFadeStoryboard"];
                fadeStatus.Begin();
            });
            await Task.Delay(100);
        }

        private async Task FadeOutAndLaunchAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            await Dispatcher.InvokeAsync(() =>
            {
                var fadeOut = (Storyboard)Resources["FadeOutStoryboard"];
                fadeOut.Completed += (_, _) => tcs.TrySetResult(true);
                fadeOut.Begin();
            });

            await tcs.Task;

            await Dispatcher.InvokeAsync(() =>
            {
                var main = new MainWindow();
                Application.Current.MainWindow = main;
                main.Show();
                Close();
            });
        }
    }
}
using System;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Midnight.Services;
using Midnight.ViewModels;
using Midnight.Views;

namespace Midnight
{
    public partial class App : Application
    {
        public static TaskbarIcon? TrayIcon { get; private set; }
        public static TrayViewModel? TrayVM { get; private set; }

        public App()
        {
            DispatcherUnhandledException += OnUnhandledException;

            var settings = new SettingsService().CurrentSettings;
            SoundService.IsEnabled = settings.SoundEffectsEnabled;
            AnimationService.IsEnabled = settings.AnimationsEnabled;
            AnimationService.Initialize();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Show the splash screen — it will open MainWindow when ready
            var splash = new SplashWindow();
            splash.Show();
        }

        public static void InitializeTray(MainViewModel mainViewModel, SecurityService securityService)
        {
            TrayVM = new TrayViewModel(mainViewModel, securityService);

            TrayIcon = new TaskbarIcon
            {
                IconSource = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Midnight;component/Resources/app.ico")),
                ToolTipText = "Midnight",
                DataContext = TrayVM,
                ContextMenu = (ContextMenu)Current.Resources["TrayContextMenu"]
            };

            TrayIcon.TrayMouseDoubleClick += (_, _) => TrayVM.OpenUICommand.Execute(null);
        }

        public static void DisposeTray()
        {
            TrayIcon?.Dispose();
            TrayIcon = null;
        }

        private void OnUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                string logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                string msg = $"[{DateTime.Now}] Unhandled Exception:\n{e.Exception}\n\nStack Trace:\n{e.Exception.StackTrace}\n--------------------------\n";
                System.IO.File.AppendAllText(logFile, msg);
                MessageBox.Show($"Midnight crashed. See crash_log.txt for details.\nError: {e.Exception.Message}",
                    "Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DisposeTray();
            base.OnExit(e);
        }
    }
}


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Midnight.Services;
using System.IO;

namespace Midnight.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly MainViewModel _mainViewModel;

        [ObservableProperty] private string _customRobloxPath;
        [ObservableProperty] private string _downloadPath;
        [ObservableProperty] private string _executorPath;
        [ObservableProperty] private bool _autoLaunchExecutor;
        [ObservableProperty] private int _autoRejoinDelaySeconds;
        [ObservableProperty] private string _discordWebhookUrl = "";
        [ObservableProperty] private bool _soundEffectsEnabled;
        [ObservableProperty] private bool _animationsEnabled;
        [ObservableProperty] private bool _discordRpcEnabled;
        [ObservableProperty] private string _customRobloxIconPath = "";
        [ObservableProperty] private bool _minimizeToTray;

        public SettingsViewModel(MainViewModel main, SettingsService settingsService)
        {
            _mainViewModel = main;
            _settingsService = settingsService;

            var s = _settingsService.CurrentSettings;
            _customRobloxPath = s.CustomRobloxPath ?? string.Empty;
            _downloadPath = s.DownloadPath ?? string.Empty;
            _executorPath = s.ExecutorPath ?? string.Empty;
            _autoLaunchExecutor = s.AutoLaunchExecutor;
            _autoRejoinDelaySeconds = s.AutoRejoinDelaySeconds;
            _discordWebhookUrl = s.DiscordWebhookUrl ?? string.Empty;
            _soundEffectsEnabled = s.SoundEffectsEnabled;
            _animationsEnabled = s.AnimationsEnabled;
            _discordRpcEnabled = s.DiscordRpcEnabled;
            _customRobloxIconPath = s.CustomRobloxIconPath ?? string.Empty;
            _minimizeToTray = s.MinimizeToTray;

            SoundService.IsEnabled = _soundEffectsEnabled;
            AnimationService.IsEnabled = _animationsEnabled;
        }

        partial void OnDiscordWebhookUrlChanged(string value)
        {
            _settingsService.CurrentSettings.DiscordWebhookUrl = value;
            _settingsService.SaveSettings();
        }

        partial void OnCustomRobloxPathChanged(string value)
        {
            _settingsService.CurrentSettings.CustomRobloxPath = value;
            _settingsService.SaveSettings();
        }

        partial void OnDownloadPathChanged(string value)
        {
            _settingsService.CurrentSettings.DownloadPath = value;
            _settingsService.SaveSettings();
        }

        partial void OnExecutorPathChanged(string value)
        {
            _settingsService.CurrentSettings.ExecutorPath = value;
            _settingsService.SaveSettings();
        }

        partial void OnAutoLaunchExecutorChanged(bool value)
        {
            _settingsService.CurrentSettings.AutoLaunchExecutor = value;
            _settingsService.SaveSettings();
        }

        partial void OnAutoRejoinDelaySecondsChanged(int value)
        {
            _settingsService.CurrentSettings.AutoRejoinDelaySeconds = value;
            _settingsService.SaveSettings();
        }

        partial void OnSoundEffectsEnabledChanged(bool value)
        {
            _settingsService.CurrentSettings.SoundEffectsEnabled = value;
            _settingsService.SaveSettings();
            SoundService.IsEnabled = value;
        }

        partial void OnAnimationsEnabledChanged(bool value)
        {
            _settingsService.CurrentSettings.AnimationsEnabled = value;
            _settingsService.SaveSettings();
            AnimationService.IsEnabled = value;
        }

        partial void OnDiscordRpcEnabledChanged(bool value)
        {
            _settingsService.CurrentSettings.DiscordRpcEnabled = value;
            _settingsService.SaveSettings();
        }

        partial void OnCustomRobloxIconPathChanged(string value)
        {
            _settingsService.CurrentSettings.CustomRobloxIconPath = value;
            _settingsService.SaveSettings();
        }

        partial void OnMinimizeToTrayChanged(bool value)
        {
            _settingsService.CurrentSettings.MinimizeToTray = value;
            _settingsService.SaveSettings();
        }

        [RelayCommand]
        private void BrowseCustomPath()
        {
            var dialog = new OpenFolderDialog { Title = "Select Roblox Versions Folder", Multiselect = false };
            if (dialog.ShowDialog() == true)
                CustomRobloxPath = dialog.FolderName;
        }

        [RelayCommand]
        private void BrowseDownloadPath()
        {
            var dialog = new OpenFolderDialog { Title = "Select Download Folder", Multiselect = false };
            if (dialog.ShowDialog() == true)
                DownloadPath = dialog.FolderName;
        }

        [RelayCommand]
        private void BrowseExecutorPath()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Executor Executable",
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
                ExecutorPath = dialog.FileName;
        }

        [RelayCommand]
        private void BrowseCustomIconPath()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Custom Roblox Icon",
                Filter = "Icon files (*.ico)|*.ico|All files (*.*)|*.*",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
                CustomRobloxIconPath = dialog.FileName;
        }

        [RelayCommand]
        private void ClearCustomIcon()
        {
            CustomRobloxIconPath = string.Empty;
        }

        [RelayCommand]
        public void NavigateBack() => _mainViewModel.NavigateAccounts();

        [RelayCommand]
        public void NavigateToFastFlags() => _mainViewModel.NavigateFastFlags();
    }
}


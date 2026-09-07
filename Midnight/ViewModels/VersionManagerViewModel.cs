using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Midnight.Services;
using Midnight.Views;
using System.Windows;

namespace Midnight.ViewModels
{
    public partial class VersionManagerViewModel : ObservableObject
    {
        private readonly RobloxVersionService _versionService;
        private readonly SettingsService _settingsService;

        [ObservableProperty]
        private string _installedVersion;

        [ObservableProperty]
        private string _currentVersion;

        [ObservableProperty]
        private string _futureVersion;

        [ObservableProperty]
        private string _pastVersion;

        [ObservableProperty]
        private string _customVersion;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isCurrentDownloaded;

        [ObservableProperty]
        private bool _isFutureDownloaded;

        [ObservableProperty]
        private bool _isPastDownloaded;

        [ObservableProperty]
        private bool _isCustomDownloaded;

        public ObservableCollection<string> DownloadedVersions { get; } = new();

        public string CustomPath => _settingsService.CurrentSettings.CustomRobloxPath;

        private readonly MainViewModel _mainViewModel;

        public VersionManagerViewModel(MainViewModel main, SettingsService settingsService)
        {
            _mainViewModel = main;
            _versionService = new RobloxVersionService();
            _settingsService = settingsService;
            
            _installedVersion = "Searching...";
            _currentVersion = "Loading...";
            _futureVersion = "Loading...";
            _pastVersion = "Loading...";
            _customVersion = "";
            _statusMessage = "Initializing...";

            LoadVersionsAsync();
        }

        public async void LoadVersionsAsync()
        {
            IsLoading = true;
            StatusMessage = "Fetching version data...";

            try
            {
                InstalledVersion = DetectInstalledVersion();

                var current = await _versionService.GetCurrentVersion();
                var future = await _versionService.GetFutureVersion();
                var past = await _versionService.GetPastVersion();

                CurrentVersion = current?.WindowsVersion ?? "Unknown";
                FutureVersion = future?.WindowsVersion ?? "Unknown";
                PastVersion = past?.WindowsVersion ?? "Unknown";

                await Task.Run(() => ScanDownloadedVersions());

                UpdateDownloadedFlags();

                StatusMessage = $"Version data loaded. {DownloadedVersions.Count} version(s) found locally.";
                LogService.Log("Version data loaded.", LogLevel.Info, "Version");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                LogService.Error($"Failed to load version data: {ex.Message}", "Version");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ScanDownloadedVersions()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var searchDirs = new List<string>();

            string customPath = _settingsService.CurrentSettings.CustomRobloxPath;
            if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                searchDirs.Add(customPath);

            string normalDir = GetNormalRobloxVersionsDirectory(_settingsService);
            if (!string.IsNullOrWhiteSpace(normalDir) && Directory.Exists(normalDir))
                searchDirs.Add(normalDir);

            searchDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"));
            searchDirs.Add(@"C:\Program Files (x86)\Roblox\Versions");

            foreach (var baseDir in searchDirs)
            {
                if (!Directory.Exists(baseDir)) continue;
                try
                {
                    var dir = new DirectoryInfo(baseDir);
                    if (File.Exists(Path.Combine(dir.FullName, "RobloxPlayerBeta.exe")))
                        found.Add(dir.Name);

                    foreach (var sub in dir.GetDirectories())
                    {
                        if (File.Exists(Path.Combine(sub.FullName, "RobloxPlayerBeta.exe")))
                            found.Add(sub.Name);
                    }
                }
                catch
                {
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                DownloadedVersions.Clear();
                foreach (var v in found.OrderByDescending(x => x))
                    DownloadedVersions.Add(v);
            });
        }

        private void UpdateDownloadedFlags()
        {
            IsCurrentDownloaded = !string.IsNullOrEmpty(CurrentVersion) && CurrentVersion != "Unknown" && DownloadedVersions.Contains(CurrentVersion);
            IsFutureDownloaded = !string.IsNullOrEmpty(FutureVersion) && FutureVersion != "Unknown" && DownloadedVersions.Contains(FutureVersion);
            IsPastDownloaded = !string.IsNullOrEmpty(PastVersion) && PastVersion != "Unknown" && DownloadedVersions.Contains(PastVersion);
            IsCustomDownloaded = !string.IsNullOrEmpty(CustomVersion) && DownloadedVersions.Contains(CustomVersion);
        }

        private string DetectInstalledVersion()
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("")?.ToString();
                        if (!string.IsNullOrEmpty(val)) 
                        {
                            int firstQuote = val.IndexOf('"');
                            int lastQuote = val.LastIndexOf('"');
                            if (firstQuote != -1 && lastQuote > firstQuote)
                            {
                                string exePath = val.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                                if (File.Exists(exePath))
                                {
                                    string? dirPath = Path.GetDirectoryName(exePath);
                                    if (!string.IsNullOrEmpty(dirPath))
                                        return new DirectoryInfo(dirPath).Name;
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(CustomPath) && Directory.Exists(CustomPath))
                {
                    var dir = new DirectoryInfo(CustomPath);

                    if (File.Exists(Path.Combine(dir.FullName, "RobloxPlayerBeta.exe")))
                    {
                        return dir.Name;
                    }
                    
                    var latestVersionDir = dir.GetDirectories()
                                              .Where(d => d.Name.StartsWith("version-") && File.Exists(Path.Combine(d.FullName, "RobloxPlayerBeta.exe")))
                                              .OrderByDescending(d => d.LastWriteTime)
                                              .FirstOrDefault();

                    if (latestVersionDir != null)
                    {
                        return latestVersionDir.Name;
                    }
                }

                return "Not Found";
            }
            catch
            {
                return "Error Detecting";
            }
        }

        [RelayCommand]
        private void Refresh()
        {
            LoadVersionsAsync();
        }

        [RelayCommand]
        private async Task SearchCustomVersion()
        {
            string defaultInput = string.IsNullOrWhiteSpace(CustomVersion) ? "" : CustomVersion;
            string? input = null;

            if (Application.Current.MainWindow is MainWindow mw)
            {
                input = await mw.ShowInputDialogAsync("Custom Roblox Version", "Enter Roblox Version hash or query (e.g. version-xxxxxxxxxxxxxxxx):", defaultInput);
            }

            if (input == null)
            {
                input = CustomVersion;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                StatusMessage = "Please enter a version hash or query.";
                return;
            }

            StatusMessage = "Searching WEAO API...";
            var result = await _versionService.SearchWeaoVersionAsync(input);
            if (result.Success)
            {
                CustomVersion = result.VersionHash;
                StatusMessage = result.Details;
                UpdateDownloadedFlags();
            }
            else
            {
                StatusMessage = result.Details;
            }
        }

        [RelayCommand]
        private async Task OpenDownload(string type)
        {
            if (type == "Custom" && string.IsNullOrWhiteSpace(CustomVersion))
            {
                await SearchCustomVersion();
                if (string.IsNullOrWhiteSpace(CustomVersion)) return;
            }

            string version = type switch
            {
                "Installed" => InstalledVersion,
                "Current" => CurrentVersion,
                "Future" => FutureVersion,
                "Past" => PastVersion,
                "Custom" => CustomVersion,
                _ => ""
            };

            if (!string.IsNullOrEmpty(version))
            {
                version = version.Trim();
                if (!version.StartsWith("version-", StringComparison.OrdinalIgnoreCase) && version.Length >= 8 && !version.Contains('.'))
                {
                    version = "version-" + version.ToLowerInvariant();
                }
            }

            if (!string.IsNullOrEmpty(version) && version.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
            {
                string url = $"https://rdd.weao.gg/?channel=LIVE&binaryType=WindowsPlayer&version={version}";
                
                try
                {
                    LogService.Log($"Opening RDD browser for version: {version}", LogLevel.Info, "Version");
                    
                    string destinationPath = GetNormalRobloxVersionsDirectory(_settingsService);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var browser = new InternalBrowserWindow(url, destinationPath);
                        browser.Owner = Application.Current.Windows.OfType<Window>().SingleOrDefault(x => x.IsActive);
                        browser.Closed += (s, args) => LoadVersionsAsync();
                        browser.Show();
                    });

                    StatusMessage = $"Opened internal RDD browser for {version}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to open internal browser: {ex.Message}";
                    LogService.Error($"Failed to open internal browser: {ex.Message}", "Version");
                }
            }
            else
            {
                StatusMessage = "Invalid version selected for download.";
            }
        }

        public static string GetNormalRobloxVersionsDirectory(SettingsService? settingsService = null)
        {
            if (settingsService != null)
            {
                string custom = settingsService.CurrentSettings.CustomRobloxPath;
                if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom))
                {
                    string subVersions = Path.Combine(custom, "Versions");
                    if (Directory.Exists(subVersions)) return subVersions;
                    return custom;
                }
            }

            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command");
                if (key != null)
                {
                    var val = key.GetValue("")?.ToString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        int firstQuote = val.IndexOf('"');
                        int lastQuote = val.LastIndexOf('"');
                        if (firstQuote != -1 && lastQuote > firstQuote)
                        {
                            string exePath = val.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                            if (File.Exists(exePath))
                            {
                                string? dirPath = Path.GetDirectoryName(exePath);
                                if (!string.IsNullOrEmpty(dirPath))
                                {
                                    string? parent = Path.GetDirectoryName(dirPath);
                                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                                    {
                                        return parent;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
            if (!Directory.Exists(defaultPath))
            {
                try
                {
                    Directory.CreateDirectory(defaultPath);
                }
                catch
                {
                }
            }
            return defaultPath;
        }

        [RelayCommand]
        public void NavigateBack()
        {
            _mainViewModel?.NavigateAccounts();
        }
    }
}


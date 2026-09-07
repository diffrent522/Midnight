using System;
using System.Windows;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Midnight.Models;
using Midnight.Services;
using System.Linq;
using System.Threading.Tasks;

namespace Midnight.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private string _logOutput = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        private readonly RobloxProcessManager _processManager;
        private readonly SecurityService _securityService;
        private readonly AccountStorageService _storageService;
        private readonly RobloxRequestService _requestService;
        private readonly SettingsService _settingsService;
        private readonly DiscordRpcService _discordRpc;

        public ExploitStatusViewModel ExploitsVM { get; }
        public VersionManagerViewModel VersionsVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public FastFlagsViewModel FastFlagsVM { get; }
        public AccountDetailsViewModel AccountDetailsVM { get; }
        public AddFriendViewModel AddFriendVM { get; }
        public LogsViewModel LogsVM { get; }
        public AboutViewModel AboutVM { get; }
        public AutoJoinViewModel AutoJoinVM { get; }
        public ActiveClientsViewModel ActiveClientsVM { get; }
        public BrowserViewModel BrowserVM { get; }

        public Action<Type>? NavigationHandler { get; set; }

        public ObservableCollection<RobloxAccount> Accounts { get; } = new ObservableCollection<RobloxAccount>();

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            var webhookService = new DiscordWebhookService(_settingsService);

            _processManager = new RobloxProcessManager(_settingsService, webhookService);
            _securityService = new SecurityService();
            _storageService = new AccountStorageService();
            _requestService = new RobloxRequestService();

            _discordRpc = new DiscordRpcService(_settingsService);
            _discordRpc.Initialize();

            _processManager.SessionStatusChanged += OnSessionStatusChanged;

            ExploitsVM = new ExploitStatusViewModel(this);
            VersionsVM = new VersionManagerViewModel(this, _settingsService);
            SettingsVM = new SettingsViewModel(this, _settingsService);
            FastFlagsVM = new FastFlagsViewModel(this, _settingsService, new FastFlagService(_settingsService));

            var autoJoinService = new AutoJoinService(_requestService, webhookService);
            autoJoinService.RelaunchCallback = async (userId, jobId, placeId) =>
            {
                Log($"[AutoJoin] Re-launching account {userId} (Place: {placeId}, Job: {jobId})...");

                RobloxAccount? account = null;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    account = Accounts.FirstOrDefault(a => a.UserId == userId);
                });

                if (account == null)
                {
                    Log($"[AutoJoin] Failed to find account {userId} for relaunch.");
                    return;
                }

                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    string cookie = _securityService.Decrypt(account.CookieCipher);
                    if (string.IsNullOrEmpty(cookie)) return;

                    _processManager.CloseAccountSession(account.UserId);
                    await Task.Delay(1000);

                    string result = await _processManager.LaunchAccount(cookie, account.UserId, account.Username, account.AvatarUrl, placeId, jobId, null, account.ProxyUrl);
                    Log($"[AutoJoin] Relaunch Result: {result}");
                });
            };

            _processManager.SessionJobIdUpdated += (uid, pid, jid) =>
            {
                autoJoinService.UpdateSessionInfo(uid, pid, jid);
            };

            AccountDetailsVM = new AccountDetailsViewModel(this);
            AddFriendVM = new AddFriendViewModel(this, _requestService, _securityService);
            AutoJoinVM = new AutoJoinViewModel(this, Accounts, autoJoinService, _securityService);
            BrowserVM = new BrowserViewModel(this, Accounts, _securityService);
            ActiveClientsVM = new ActiveClientsViewModel(this, _processManager);
            LogsVM = new LogsViewModel(this);
            var updateService = new UpdateService();
            AboutVM = new AboutViewModel(this, updateService);

            var savedAccounts = _storageService.LoadAccounts();
            foreach (var acc in savedAccounts)
                Accounts.Add(acc);

            CheckForUpdates();
        }

        private async void OnSessionStatusChanged(RobloxSession session)
        {
            if (!_settingsService.CurrentSettings.DiscordRpcEnabled) return;

            if (session.Status == "In Game")
            {
                long.TryParse(session.PlaceId, out long placeId);
                string gameName = (!string.IsNullOrEmpty(session.PlaceName) && session.PlaceName != "Unknown")
                    ? session.PlaceName
                    : "Roblox";

                await _discordRpc.SetGamePresenceAsync(
                    session.AccountName,
                    gameName,
                    placeId > 0 ? placeId : null,
                    session.LaunchTime,
                    session.ServerType,
                    session.AvatarUrl);
            }
            else if (session.Status == "Closed")
            {
                var remainingInGame = _processManager.ActiveSessions.FirstOrDefault(s => s.Status == "In Game");
                if (remainingInGame != null)
                {
                    long.TryParse(remainingInGame.PlaceId, out long pid);
                    string gName = (!string.IsNullOrEmpty(remainingInGame.PlaceName) && remainingInGame.PlaceName != "Unknown")
                        ? remainingInGame.PlaceName
                        : "Roblox";

                    await _discordRpc.SetGamePresenceAsync(
                        remainingInGame.AccountName,
                        gName,
                        pid > 0 ? pid : null,
                        remainingInGame.LaunchTime,
                        remainingInGame.ServerType,
                        remainingInGame.AvatarUrl);
                }
                else if (_processManager.ActiveSessions.Count > 0)
                {
                    await _discordRpc.SetIdlePresenceAsync();
                }
                else
                {
                    _discordRpc.ClearPresence();
                }
            }
        }

        private async void CheckForUpdates()
        {
            var updateService = new UpdateService();
            var release = await updateService.CheckForUpdatesAsync();
            if (release != null)
            {
                IsUpdateAvailable = true;
                UpdateVersion = release.TagName;
                _pendingUpdate = release;
                Log($"[Update] New version found: {release.TagName}");
            }
        }

        [ObservableProperty]
        private bool _isUpdateAvailable;

        [ObservableProperty]
        private string _updateVersion = string.Empty;

        private GithubRelease? _pendingUpdate;

        [RelayCommand]
        public async Task PerformUpdate()
        {
            if (_pendingUpdate == null) return;
            var updateService = new UpdateService();
            Log("Downloading update...");
            await updateService.DownloadAndInstallAsync(_pendingUpdate);
        }

        [RelayCommand]
        public void NavigateAccounts() => NavigationHandler?.Invoke(typeof(Views.AccountsPage));

        [RelayCommand]
        public void NavigateBrowser() => NavigationHandler?.Invoke(typeof(Views.BrowserPage));

        [RelayCommand]
        public void NavigateFastFlags() => NavigationHandler?.Invoke(typeof(Views.FastFlagsPage));

        public void NavigateTo(Type pageType) => NavigationHandler?.Invoke(pageType);

        public event EventHandler? LaunchRequested;

        [RelayCommand]
        public void LaunchSelected() => LaunchRequested?.Invoke(this, EventArgs.Empty);

        public void NavigateAccountDetails(RobloxAccount account)
        {
            AccountDetailsVM.LoadAccount(account);
            NavigationHandler?.Invoke(typeof(Views.AccountDetailsPage));
        }

        public async Task AddNewAccountAsync(string rawCookie, DateTime? expiration = null)
        {
            string encryptedCookie = _securityService.Encrypt(rawCookie);
            var newAccount = new RobloxAccount
            {
                Username = "Loading...",
                CookieCipher = encryptedCookie,
                ExpirationDate = expiration,
                IsSelected = true
            };

            Accounts.Add(newAccount);
            _storageService.SaveAccounts(Accounts);
            Log($"Added account with captured cookie. Fetching profile...");

            try
            {
                var userInfo = await _processManager.GetUserInfo(rawCookie);
                if (userInfo != null)
                {
                    newAccount.Username = userInfo.Name ?? "Unknown";
                    newAccount.DisplayName = userInfo.DisplayName ?? "Unknown";
                    newAccount.UserId = userInfo.Id;
                    newAccount.AvatarUrl = userInfo.AvatarUrl ?? "https://tr.rbxcdn.com/53eb9b17fe1432a809c73a132d78f5f1/150/150/AvatarHeadshot/Png";
                    Log($"Identified account: {newAccount.Username} (ID: {newAccount.UserId})");
                    _storageService.SaveAccounts(Accounts);
                }
                else
                {
                    newAccount.Username = "Unknown";
                    Log("[Warning] Failed to fetch user info.");
                }
            }
            catch (Exception ex)
            {
                Log($"[Error] Profile fetch failed: {ex.Message}");
                newAccount.Username = "Error";
            }
        }

        [RelayCommand]
        public void RemoveAccount(RobloxAccount? account)
        {
            if (account != null && Accounts.Contains(account))
            {
                Accounts.Remove(account);
                _storageService.SaveAccounts(Accounts);
                Log($"Removed account: {account.Username}");
            }
        }

        public void SaveAccounts()
        {
            _storageService.SaveAccounts(Accounts);
            Log("Accounts saved.");
        }

        public async Task MoveAccountToGroupAsync(RobloxAccount account, string groupName)
        {
            IsBusy = true;
            try
            {
                await Task.Delay(300);
                account.Group = groupName;
                SaveAccounts();
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task DeleteGroupAsync(string groupName)
        {
            if (groupName == "Default") return;
            IsBusy = true;
            try
            {
                await Task.Delay(500);
                var accountsInGroup = Accounts.Where(a => a.Group == groupName).ToList();
                foreach (var acc in accountsInGroup)
                    acc.Group = "Default";
                SaveAccounts();
                Log($"Deleted group '{groupName}' and moved {accountsInGroup.Count} accounts to Default.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public List<string> GetAvailableRobloxVersions() => _processManager.GetAvailableRobloxVersions();

        public async Task LaunchSelectedAsync(string? placeId, string? jobId, string? accessCode = null, string? selectedVersion = null, int launchDelaySeconds = 3)
        {
            var selectedAccounts = Accounts.Where(a => a.IsSelected).ToList();
            if (!selectedAccounts.Any())
            {
                Log("No accounts selected.");
                return;
            }

            int delayMs = Math.Clamp(launchDelaySeconds, 1, 30) * 1000;

            IsBusy = true;
            try
            {
                Log($"Launching {selectedAccounts.Count} account(s) with {launchDelaySeconds}s delay...");

                foreach (var account in selectedAccounts)
                {
                    Log($"Preparing {account.Username}...");

                    string cookie = _securityService.Decrypt(account.CookieCipher);
                    if (string.IsNullOrEmpty(cookie))
                    {
                        Log($"[Error] Failed to decrypt cookie for {account.Username}");
                        continue;
                    }

                    string result = await _processManager.LaunchAccount(cookie, account.UserId, account.Username, account.AvatarUrl, placeId, jobId, accessCode, account.ProxyUrl, selectedVersion);
                    Log($"[{account.Username}] Result: {result}");

                    await Task.Delay(delayMs);
                }

                Log("Launch sequence completed.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void NavigateAddFriend() => NavigationHandler?.Invoke(typeof(Views.AddFriendPage));

        private void Log(string message)
        {
            StatusMessage = message;
            LogService.Log(message);
        }

        public void Dispose()
        {
            _discordRpc.Dispose();
            _processManager.Dispose();
        }
    }
}


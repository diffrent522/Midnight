using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.Management;
using Midnight.Core;
using Midnight.Models;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;

namespace Midnight.Services
{
    public class RobloxProcessManager : IDisposable
    {
        private const string ROBLOX_MUTEX_NAME = "ROBLOX_singletonMutex";

        public ObservableCollection<RobloxSession> ActiveSessions { get; } = new ObservableCollection<RobloxSession>();

        private readonly RobloxRequestService _requestService;
        private readonly DiscordWebhookService? _webhookService;
        private readonly SettingsService _settingsService;
        private readonly FastFlagService _fastFlagService;
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly System.Threading.Timer _monitoringTimer;

        public event Action<long, long, string>? SessionJobIdUpdated;
        public event Action<RobloxSession>? SessionStatusChanged;

        public RobloxProcessManager(SettingsService settingsService, DiscordWebhookService? webhookService = null, FastFlagService? fastFlagService = null)
        {
            _settingsService = settingsService;
            _requestService = new RobloxRequestService();
            _webhookService = webhookService;
            _fastFlagService = fastFlagService ?? new FastFlagService(settingsService);
            _monitoringTimer = new System.Threading.Timer(MonitorMemoryUsage, null, 1000, 1000);
        }

        private void MonitorMemoryUsage(object? state)
        {
            if (ActiveSessions.Count == 0) return;

            ulong freeRam = 0;
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
                freeRam = memStatus.ullAvailPhys;

            double freeMb = Math.Round(freeRam / 1024.0 / 1024.0, 0);

            var sessions = ActiveSessions.ToList();
            foreach (var session in sessions)
            {
                try
                {
                    var proc = Process.GetProcessById(session.ProcessId);
                    proc.Refresh();
                    double usedMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 0);

                    if (session.RamUsageMb != usedMb) session.RamUsageMb = usedMb;
                    if (session.FreeRamMb != freeMb) session.FreeRamMb = freeMb;
                }
                catch { }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public class RobloxUserInfo
        {
            public long Id { get; set; }
            public string? Name { get; set; }
            public string? DisplayName { get; set; }
            public string? AvatarUrl { get; set; }
        }

        public async Task<RobloxUserInfo?> GetUserInfo(string cookie)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
                request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                var info = new RobloxUserInfo
                {
                    Id = root.GetProperty("id").GetInt64(),
                    Name = root.GetProperty("name").GetString(),
                    DisplayName = root.GetProperty("displayName").GetString()
                };

                using var thumbRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={info.Id}&size=150x150&format=Png&isCircular=false");
                var thumbResp = await _httpClient.SendAsync(thumbRequest);

                if (thumbResp.IsSuccessStatusCode)
                {
                    var thumbJson = await thumbResp.Content.ReadAsStringAsync();
                    using var thumbDoc = System.Text.Json.JsonDocument.Parse(thumbJson);
                    var data = thumbDoc.RootElement.GetProperty("data");
                    if (data.GetArrayLength() > 0)
                        info.AvatarUrl = data[0].GetProperty("imageUrl").GetString();
                }

                if (string.IsNullOrEmpty(info.AvatarUrl))
                    info.AvatarUrl = "https://tr.rbxcdn.com/53eb9b17fe1432a809c73a132d78f5f1/150/150/AvatarHeadshot/Png";

                return info;
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to fetch user info: {ex.Message}", "Process");
                return null;
            }
        }

        public async Task<string> LaunchAccount(string cookie, long userId, string accountName, string? avatarUrl, string? placeId = null, string? jobId = null, string? accessCode = null, string? proxyUrl = null, string? selectedVersion = null)
        {
            try
            {
                LogService.Log($"Preparing to launch account {(accessCode != null ? "(Private Server) " : "")}{(proxyUrl != null ? "(With Proxy) " : "")}...", LogLevel.Info, "Process");

                string authTicket;
                try
                {
                    authTicket = await _requestService.GetAuthenticationTicket(cookie, proxyUrl);
                }
                catch (Exception authEx)
                {
                    var msg = $"Auth Ticket Error: {authEx.Message}";
                    LogService.Error(msg, "Process");
                    return msg;
                }

                if (string.IsNullOrEmpty(authTicket))
                {
                    LogService.Error("Failed to generate authentication ticket (empty response).", "Process");
                    return "Failed to generate authentication ticket (empty response).";
                }

                string? playerPath = GetRobloxVersion(selectedVersion);
                if (string.IsNullOrEmpty(playerPath))
                {
                    LogService.Error("Roblox Player not found.", "Process");
                    return "Roblox Player not found.";
                }

                LogService.Log($"Found Roblox Player at: {playerPath}", LogLevel.Info, "Process");

                // Apply fast flags to ClientAppSettings.json before launching
                await ApplyFastFlagsAsync(playerPath);

                ApplyCustomIcon(playerPath);

                await KillRobloxMutexSafe();
                LogService.Log("Pre-launch mutex cleanup complete.", LogLevel.Info, "Process");

                long launchTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long browserTrackerId = (long)(new Random().NextDouble() * 1_000_000_000_000);

                long actualPlaceId = placeId == null ? 1818 : long.Parse(placeId);

                if (placeId != null)
                {
                    try
                    {
                        var rootPlaceId = await _requestService.GetRootPlaceIdAsync(actualPlaceId);
                        if (rootPlaceId.HasValue && rootPlaceId.Value != actualPlaceId)
                        {
                            LogService.Log($"Sub-place detected ({actualPlaceId}). Falling back to root place ({rootPlaceId.Value}).", LogLevel.Warning, "Process");
                            actualPlaceId = rootPlaceId.Value;
                            jobId = null;
                        }
                    }
                    catch { }
                }

                string placeLauncherUrl;
                if (!string.IsNullOrWhiteSpace(accessCode))
                {
                    placeLauncherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&browserTrackerId={browserTrackerId}&placeId={actualPlaceId}&accessCode={accessCode}&linkCode={accessCode}&isPlayTogetherGame=false";
                    LogService.Log("Joining Private Server via AccessCode.", LogLevel.Info, "Process");
                }
                else if (!string.IsNullOrWhiteSpace(jobId))
                {
                    placeLauncherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob&browserTrackerId={browserTrackerId}&placeId={actualPlaceId}&gameId={jobId}&isPlayTogetherGame=false";
                    LogService.Log($"Joining via Job ID: {jobId}", LogLevel.Info, "Process");
                }
                else
                {
                    placeLauncherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={browserTrackerId}&placeId={actualPlaceId}&isPlayTogetherGame=false";
                    LogService.Log(placeId == null ? "Launching App (Hub)." : $"Joining Place: {actualPlaceId}", LogLevel.Info, "Process");
                }

                string encodedPlaceLauncherUrl = Uri.EscapeDataString(placeLauncherUrl);

                var settings = _settingsService.CurrentSettings;
                if (settings.AutoLaunchExecutor && !string.IsNullOrEmpty(settings.ExecutorPath) && File.Exists(settings.ExecutorPath))
                {
                    string execName = Path.GetFileNameWithoutExtension(settings.ExecutorPath);
                    if (Process.GetProcessesByName(execName).Length == 0)
                    {
                        try
                        {
                            LogService.Log($"Auto-launching executor: {execName}...", LogLevel.Info, "Executor");
                            Process.Start(new ProcessStartInfo(settings.ExecutorPath)
                            {
                                UseShellExecute = true,
                                WorkingDirectory = Path.GetDirectoryName(settings.ExecutorPath)
                            });
                            await Task.Delay(2000);
                        }
                        catch (Exception ex)
                        {
                            LogService.Error($"Failed to launch executor: {ex.Message}", "Executor");
                        }
                    }
                    else
                    {
                        LogService.Log($"{execName} is already running. Skipping.", LogLevel.Warning, "Executor");
                    }
                }

                string protocolUrl = $"roblox-player:1+launchmode:play+gameinfo:{authTicket}+launchtime:{launchTime}+placelauncherurl:{encodedPlaceLauncherUrl}+browsertrackerid:{browserTrackerId}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";

                LogService.Log($"Launching Protocol URL (TrackerID: {browserTrackerId})", LogLevel.Info, "Process");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{protocolUrl}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (!string.IsNullOrEmpty(proxyUrl))
                {
                    startInfo.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
                    startInfo.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
                    startInfo.EnvironmentVariables["ALL_PROXY"] = proxyUrl;
                }

                var ret = Process.Start(startInfo);
                if (ret == null)
                {
                    LogService.Error("Failed to start launch command.", "Process");
                    _webhookService?.SendNotificationAsync("Launch Failed", $"Failed to start process for **{accountName}**.", 16711680, accountName, userId, placeId, null, avatarUrl);
                    return "Failed to start launch command.";
                }

                string? placeName = null;
                if (!string.IsNullOrEmpty(placeId) && long.TryParse(placeId, out long pid))
                    placeName = await _requestService.GetGameNameFromPlaceIdAsync(pid);

                string serverType = "Public Server";
                if (!string.IsNullOrEmpty(accessCode)) serverType = "Private Server";
                else if (!string.IsNullOrEmpty(jobId)) serverType = "Reserved/Job";

                _webhookService?.SendNotificationAsync("Account Launched", $"Successfully launched **{accountName}**.", 65280, accountName, userId, placeId, jobId, avatarUrl, placeName, serverType);

                TrackLaunchedSession(browserTrackerId, userId, accountName, avatarUrl, placeName, placeId, jobId, serverType);

                await Task.Delay(4000);
                await KillRobloxMutexSafe();
                LogService.Log("Post-launch mutex cleanup complete. Next account can now launch.", LogLevel.Info, "Process");

                return $"Launched via Protocol (TrackerID: {browserTrackerId})";
            }
            catch (Exception ex)
            {
                LogService.Error($"Error launching: {ex.Message}", "Process");
                return $"Error launching: {ex.Message}";
            }
        }

        private async Task ApplyFastFlagsAsync(string playerPath)
        {
            var settings = _settingsService.CurrentSettings;

            try
            {
                Dictionary<string, string> flags;

                if (!settings.FastFlagsEnabled)
                {
                    // Fast flags disabled — write an empty object so Roblox uses its own defaults.
                    flags = new Dictionary<string, string>();
                }
                else
                {
                    flags = FastFlagService.MergeFlags(
                        settings.FastFlagsCustom.Select(kv => new Models.FastFlagEntry(kv.Key, kv.Value)),
                        settings.FastFlagsEnabledPresets);
                }

                // Apply across all detected versions
                await _fastFlagService.ApplyFlagsAsync(flags);

                // Ensure the specific folder for the player being launched also has the file
                string? exeDir = System.IO.Path.GetDirectoryName(playerPath);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string clientSettingsDir = System.IO.Path.Combine(exeDir, "ClientSettings");
                    string jsonPath = System.IO.Path.Combine(clientSettingsDir, "ClientAppSettings.json");

                    if (!System.IO.Directory.Exists(clientSettingsDir))
                        System.IO.Directory.CreateDirectory(clientSettingsDir);

                    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    var jsonDict = new Dictionary<string, object>(flags.Count);
                    foreach (var kv in flags)
                    {
                        if (bool.TryParse(kv.Value, out bool b)) jsonDict[kv.Key] = b;
                        else if (long.TryParse(kv.Value, out long l)) jsonDict[kv.Key] = l;
                        else if (double.TryParse(kv.Value,
                                     System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture,
                                     out double d)) jsonDict[kv.Key] = d;
                        else jsonDict[kv.Key] = kv.Value;
                    }

                    await System.IO.File.WriteAllTextAsync(jsonPath,
                        System.Text.Json.JsonSerializer.Serialize(jsonDict, jsonOptions));

                    if (settings.FastFlagsEnabled && flags.Count > 0)
                        LogService.Log($"Fast flags applied ({flags.Count} flag(s)) → {jsonPath}", LogLevel.Info, "FastFlags");
                    else
                        LogService.Log($"Fast flags cleared → {jsonPath}", LogLevel.Info, "FastFlags");
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to apply fast flags: {ex.Message}", "FastFlags");
            }
        }

        private void ApplyCustomIcon(string playerPath)
        {
            string iconPath = _settingsService.CurrentSettings.CustomRobloxIconPath;
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath)) return;

            string? exeDir = Path.GetDirectoryName(playerPath);
            if (string.IsNullOrEmpty(exeDir)) return;

            try
            {
                string backupPath = Path.Combine(exeDir, "RobloxPlayerBeta.exe.original_icon_backup");
                if (!File.Exists(backupPath))
                    File.Copy(playerPath, backupPath);

                IconInjector.InjectIcon(playerPath, iconPath);
                LogService.Log("Custom icon applied to RobloxPlayerBeta.exe.", LogLevel.Info, "Icon");
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to inject custom icon: {ex.Message}", "Icon");
            }
        }

        private async void TrackLaunchedSession(long browserTrackerId, long userId, string accountName, string? avatarUrl, string? placeName, string? placeId, string? jobId, string? serverType)
        {
            LogService.Log($"Tracking session for {accountName} (TrackerID: {browserTrackerId})...", LogLevel.Info, "Process");

            int attempts = 0;
            int maxAttempts = 30;

            while (attempts < maxAttempts)
            {
                await Task.Delay(1000);
                attempts++;

                var processes = Process.GetProcessesByName("RobloxPlayerBeta");

                foreach (var p in processes)
                {
                    if (ActiveSessions.Any(s => s.ProcessId == p.Id)) continue;

                    string? cmdLine = GetCommandLine(p.Id);
                    if (cmdLine != null && cmdLine.Contains(browserTrackerId.ToString()))
                    {
                        RegisterSession(p, browserTrackerId, userId, accountName, avatarUrl, placeName, placeId, jobId, serverType);
                        return;
                    }
                }

                if (attempts >= 8)
                {
                    var untracked = processes
                        .Where(p => !ActiveSessions.Any(s => s.ProcessId == p.Id))
                        .OrderByDescending(p => { try { return p.StartTime; } catch { return DateTime.MinValue; } })
                        .FirstOrDefault();

                    if (untracked != null)
                    {
                        LogService.Log($"[Tracking] Claiming newest untracked PID {untracked.Id} for {accountName}.", LogLevel.Warning, "Process");
                        RegisterSession(untracked, browserTrackerId, userId, accountName, avatarUrl, placeName, placeId, jobId, serverType);
                        return;
                    }
                }
            }

            LogService.Error($"Session tracking failed for {accountName} — no matching RobloxPlayerBeta process found within {maxAttempts}s.", "Process");
        }

        private void RegisterSession(Process p, long browserTrackerId, long userId, string accountName, string? avatarUrl, string? placeName, string? placeId, string? jobId, string? serverType)
        {
            string robloxVersion = "Unknown";
            try
            {
                string? exePath = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    robloxVersion = Path.GetFileName(Path.GetDirectoryName(exePath)) ?? "Unknown";
            }
            catch { }

            var session = new RobloxSession
            {
                ProcessId = p.Id,
                AccountName = accountName,
                UserId = userId,
                LaunchTime = DateTime.Now,
                BrowserTrackerId = browserTrackerId,
                LaunchMode = "Protocol",
                Status = !string.IsNullOrEmpty(placeId) ? "In Game" : "Running",
                PlaceId = placeId,
                JobId = jobId,
                PlaceName = placeName ?? "Unknown",
                AvatarUrl = avatarUrl,
                ServerType = serverType,
                RobloxVersion = robloxVersion
            };

            p.EnableRaisingEvents = true;
            p.Exited += (_, _) =>
            {
                session.Status = "Closed";
                SessionStatusChanged?.Invoke(session);
                System.Windows.Application.Current.Dispatcher.Invoke(() => ActiveSessions.Remove(session));
                _webhookService?.SendNotificationAsync(
                    "Account Disconnected",
                    $"**{session.AccountName}** closed session.",
                    16753920,
                    session.AccountName, session.UserId, session.PlaceId,
                    session.JobId, session.AvatarUrl, session.PlaceName, session.ServerType);
            };

            System.Windows.Application.Current.Dispatcher.Invoke(() => ActiveSessions.Add(session));
            LogService.Log($"Session registered: PID {p.Id} → {accountName} (Version: {robloxVersion})", LogLevel.Success, "Process");

            // Immediately invoke status change so Discord RPC, Active Clients, etc. update right away
            SessionStatusChanged?.Invoke(session);

            _ = Task.Run(() => MonitorLogFile(session));
        }

        private async Task MonitorLogFile(RobloxSession session)
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
            if (!Directory.Exists(logDir)) return;

            FileInfo? logFile = null;
            DateTime minCreationTime = session.LaunchTime.AddSeconds(-15);

            for (int i = 0; i < 30 && session.Status != "Closed"; i++)
            {
                try
                {
                    var files = new DirectoryInfo(logDir).GetFiles("*.log")
                        .Where(f => f.LastWriteTime >= minCreationTime && f.Name.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToList();

                    foreach (var f in files)
                    {
                        if (f.Length > 2048 || f.LastWriteTime > DateTime.Now.AddSeconds(-2))
                        {
                            logFile = f;
                            break;
                        }
                    }

                    if (logFile != null)
                        break;
                }
                catch { }
                await Task.Delay(1000);
            }

            if (logFile == null) return;
            LogService.Log($"Found log: {logFile.Name} for session {session.BrowserTrackerId}", LogLevel.Info, "Monitor");

            await MonitorSpecificLogFile(session, logFile, minCreationTime, logDir);
        }

        private async Task MonitorSpecificLogFile(RobloxSession session, FileInfo logFile, DateTime minCreationTime, string logDir)
        {
            try
            {
                using var stream = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                int emptyReads = 0;

                while (session.Status != "Closed")
                {
                    string? line = await reader.ReadLineAsync();
                    if (line != null)
                    {
                        emptyReads = 0;

                        // Check if this was a starter process log that terminated
                        if (line.Contains("RobloxStarter destroyed") && string.IsNullOrEmpty(session.JobId))
                        {
                            await Task.Delay(1000);
                            var activeLog = new DirectoryInfo(logDir).GetFiles("*.log")
                                .Where(f => f.FullName != logFile.FullName && f.LastWriteTime >= minCreationTime && f.Name.Contains("Player", StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(f => f.LastWriteTime)
                                .FirstOrDefault();

                            if (activeLog != null)
                            {
                                LogService.Log($"Switching from starter log to game log: {activeLog.Name}", LogLevel.Info, "Monitor");
                                await MonitorSpecificLogFile(session, activeLog, minCreationTime, logDir);
                                return;
                            }
                        }

                        // Check for joining game entry: '! Joining game 'jobid' place placeid'
                        var joinMatch = Regex.Match(line, @"! Joining game '(?<jobid>[a-fA-F0-9\-]{36})'(?:\s+place\s+(?<placeid>\d+))?");
                        if (joinMatch.Success)
                        {
                            string jid = joinMatch.Groups["jobid"].Value;
                            if (string.IsNullOrEmpty(session.JobId) || session.JobId != jid)
                            {
                                AssignJobId(session, jid);
                            }

                            if (joinMatch.Groups["placeid"].Success)
                            {
                                string pidStr = joinMatch.Groups["placeid"].Value;
                                UpdateSessionPlace(session, pidStr);
                            }
                        }
                        else if (line.Contains("[FLog::GameJoinLoadTime] Report game_join_loadtime:") || line.Contains("placeid:"))
                        {
                            var pidMatch = Regex.Match(line, @"placeid:(\d+)");
                            if (pidMatch.Success)
                            {
                                UpdateSessionPlace(session, pidMatch.Groups[1].Value);
                            }
                        }
                        else if (string.IsNullOrEmpty(session.JobId))
                        {
                            var match = Regex.Match(line, @"JobId=\s*([a-fA-F0-9\-]{36})");
                            if (match.Success)
                            {
                                AssignJobId(session, match.Groups[1].Value);
                            }
                        }
                        // Check for game leave/disconnect
                        else if (line.Contains("leaveUGCGameInternal") || line.Contains("Time to disconnect replication data:"))
                        {
                            session.Status = "Running";
                            session.PlaceId = null;
                            session.JobId = null;
                            session.PlaceName = "Roblox";
                            SessionStatusChanged?.Invoke(session);
                        }
                    }
                    else
                    {
                        emptyReads++;
                        // If file is tiny (< 3KB) and has received no updates for 3 seconds, check if real game log exists
                        if (emptyReads > 6 && stream.Length < 3072 && string.IsNullOrEmpty(session.JobId))
                        {
                            var alternative = new DirectoryInfo(logDir).GetFiles("*.log")
                                .Where(f => f.FullName != logFile.FullName && f.LastWriteTime >= minCreationTime && f.Name.Contains("Player", StringComparison.OrdinalIgnoreCase) && f.Length > stream.Length)
                                .OrderByDescending(f => f.LastWriteTime)
                                .FirstOrDefault();

                            if (alternative != null)
                            {
                                LogService.Log($"Switching to active game log: {alternative.Name}", LogLevel.Info, "Monitor");
                                await MonitorSpecificLogFile(session, alternative, minCreationTime, logDir);
                                return;
                            }
                        }
                        await Task.Delay(500);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Error reading log: {ex.Message}", "Monitor");
            }
        }

        private async void UpdateSessionPlace(RobloxSession session, string pidStr)
        {
            bool placeChanged = string.IsNullOrEmpty(session.PlaceId) || session.PlaceId != pidStr;
            bool nameUnknown = string.IsNullOrEmpty(session.PlaceName) || session.PlaceName == "Unknown";

            if (placeChanged || nameUnknown)
            {
                session.PlaceId = pidStr;
                session.Status = "In Game";
                if (long.TryParse(pidStr, out long pid))
                {
                    string? newName = await _requestService.GetGameNameFromPlaceIdAsync(pid);
                    if (!string.IsNullOrEmpty(newName))
                    {
                        session.PlaceName = newName;
                    }
                }
                SessionStatusChanged?.Invoke(session);
            }
        }

        private void AssignJobId(RobloxSession session, string jid)
        {
            session.JobId = jid;
            session.Status = "In Game";
            LogService.Log($"Found JobId: {jid}", LogLevel.Success, "Monitor");

            SessionStatusChanged?.Invoke(session);

            long.TryParse(session.PlaceId, out long pid);
            SessionJobIdUpdated?.Invoke(session.UserId, pid, jid);
        }

        private string? GetCommandLine(int processId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
                using var objects = searcher.Get();
                foreach (var obj in objects)
                    return obj["CommandLine"]?.ToString();
            }
            catch (Exception ex)
            {
                LogService.Error($"GetCommandLine failed: {ex.Message}", "Process");
            }
            return null;
        }

        public void CloseAllRobloxMutexes()
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            foreach (var p in processes)
            {
                try { CloseRobloxMutex(p.Id); }
                catch { }
            }
        }

        public List<string> GetAvailableRobloxVersions()
        {
            var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var searchDirs = new List<string>();

            string customPath = _settingsService.CurrentSettings.CustomRobloxPath;
            if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                searchDirs.Add(customPath);

            string downloadPath = _settingsService.CurrentSettings.DownloadPath;
            if (!string.IsNullOrWhiteSpace(downloadPath) && Directory.Exists(downloadPath))
                searchDirs.Add(downloadPath);

            string normalVersions = ViewModels.VersionManagerViewModel.GetNormalRobloxVersionsDirectory(_settingsService);
            if (!string.IsNullOrWhiteSpace(normalVersions) && Directory.Exists(normalVersions))
                searchDirs.Add(normalVersions);

            searchDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"));
            searchDirs.Add(@"C:\Program Files (x86)\Roblox\Versions");

            foreach (var baseDir in searchDirs)
            {
                if (!Directory.Exists(baseDir)) continue;
                try
                {
                    var dir = new DirectoryInfo(baseDir);
                    if (File.Exists(Path.Combine(dir.FullName, "RobloxPlayerBeta.exe")))
                        versions.Add(dir.Name);

                    foreach (var sub in dir.GetDirectories())
                    {
                        if (File.Exists(Path.Combine(sub.FullName, "RobloxPlayerBeta.exe")))
                            versions.Add(sub.Name);
                    }
                }
                catch { }
            }

            return versions.OrderByDescending(v => v).ToList();
        }

        private string? GetRobloxVersion(string? selectedVersion = null)
        {
            if (!string.IsNullOrWhiteSpace(selectedVersion) && !selectedVersion.StartsWith("Default", StringComparison.OrdinalIgnoreCase))
            {
                string target = selectedVersion.Trim();

                if (File.Exists(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return target;

                if (Directory.Exists(target))
                {
                    string directExe = Path.Combine(target, "RobloxPlayerBeta.exe");
                    if (File.Exists(directExe)) return directExe;
                }

                var searchDirs = new List<string>();
                string customPath = _settingsService.CurrentSettings.CustomRobloxPath;
                if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                    searchDirs.Add(customPath);

                string downloadPath = _settingsService.CurrentSettings.DownloadPath;
                if (!string.IsNullOrWhiteSpace(downloadPath) && Directory.Exists(downloadPath))
                    searchDirs.Add(downloadPath);

                string normalVersions = ViewModels.VersionManagerViewModel.GetNormalRobloxVersionsDirectory(_settingsService);
                if (!string.IsNullOrWhiteSpace(normalVersions) && Directory.Exists(normalVersions))
                    searchDirs.Add(normalVersions);

                searchDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"));
                searchDirs.Add(@"C:\Program Files (x86)\Roblox\Versions");

                foreach (var baseDir in searchDirs)
                {
                    if (!Directory.Exists(baseDir)) continue;

                    string candidateExe = Path.Combine(baseDir, target, "RobloxPlayerBeta.exe");
                    if (File.Exists(candidateExe)) return candidateExe;

                    try
                    {
                        foreach (var sub in new DirectoryInfo(baseDir).GetDirectories())
                        {
                            if (sub.Name.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                                (target.StartsWith("version-", StringComparison.OrdinalIgnoreCase) && sub.Name.Contains(target, StringComparison.OrdinalIgnoreCase)))
                            {
                                string exe = Path.Combine(sub.FullName, "RobloxPlayerBeta.exe");
                                if (File.Exists(exe)) return exe;
                            }
                        }
                    }
                    catch { }
                }

                LogService.Log($"Selected version '{selectedVersion}' not found locally. Falling back.", LogLevel.Warning, "Process");
            }

            return GetLatestRobloxVersion();
        }

        private string? GetLatestRobloxVersion()
        {
            try
            {
                string customPath = _settingsService.CurrentSettings.CustomRobloxPath;
                if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                {
                    string directExe = Path.Combine(customPath, "RobloxPlayerBeta.exe");
                    if (File.Exists(directExe)) return directExe;

                    var latest = new DirectoryInfo(customPath)
                        .GetDirectories()
                        .Where(d => d.Name.StartsWith("version-") && File.Exists(Path.Combine(d.FullName, "RobloxPlayerBeta.exe")))
                        .OrderByDescending(d => d.LastWriteTime)
                        .FirstOrDefault();

                    if (latest != null)
                        return Path.Combine(latest.FullName, "RobloxPlayerBeta.exe");
                }

                var candidatePaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"),
                    @"C:\Program Files (x86)\Roblox\Versions"
                };

                foreach (var versionsPath in candidatePaths)
                {
                    if (!Directory.Exists(versionsPath)) continue;

                    var latest = new DirectoryInfo(versionsPath)
                        .GetDirectories()
                        .OrderByDescending(d => d.LastWriteTime)
                        .FirstOrDefault();

                    if (latest != null)
                    {
                        string exePath = Path.Combine(latest.FullName, "RobloxPlayerBeta.exe");
                        if (File.Exists(exePath)) return exePath;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Error finding Roblox version: {ex.Message}");
            }
            return null;
        }

        public bool CloseRobloxMutex(int processId)
        {
            var handles = GetSystemHandles();
            foreach (var handleEntry in handles)
            {
                if (handleEntry.ProcessId != processId) continue;

                string? handleName = GetHandleName(handleEntry, processId);
                if (handleName != null && handleName.Contains(ROBLOX_MUTEX_NAME))
                    return CloseRemoteHandle(handleEntry, processId);
            }
            return false;
        }

        public void CloseAccountSession(long userId)
        {
            try
            {
                var session = ActiveSessions.FirstOrDefault(s => s.UserId == userId);
                if (session != null)
                {
                    try
                    {
                        Process.GetProcessById(session.ProcessId).Kill();
                        LogService.Log($"[ProcessManager] Killed process {session.ProcessId} for user {userId}");
                    }
                    catch (Exception ex)
                    {
                        LogService.Error($"[ProcessManager] Failed to kill process: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[ProcessManager] Error closing session: {ex.Message}");
            }
        }

        private List<NativeHelper.SYSTEM_HANDLE_INFORMATION> GetSystemHandles()
        {
            var handleList = new List<NativeHelper.SYSTEM_HANDLE_INFORMATION>();
            int initSize = 0x10000;
            IntPtr buffer = Marshal.AllocHGlobal(initSize);
            int returnLength = 0;

            try
            {
                while (NativeHelper.NtQuerySystemInformation(
                    NativeHelper.SYSTEM_INFORMATION_CLASS.SystemHandleInformation,
                    buffer, initSize, out returnLength) == NativeHelper.STATUS_INFO_LENGTH_MISMATCH)
                {
                    initSize = returnLength;
                    Marshal.FreeHGlobal(buffer);
                    buffer = Marshal.AllocHGlobal(initSize);
                }

                long handleCount = Marshal.ReadIntPtr(buffer).ToInt64();
                IntPtr ptr = new IntPtr(buffer.ToInt64() + IntPtr.Size);
                int structSize = Marshal.SizeOf(typeof(NativeHelper.SYSTEM_HANDLE_INFORMATION));

                for (int i = 0; i < handleCount; i++)
                {
                    handleList.Add(Marshal.PtrToStructure<NativeHelper.SYSTEM_HANDLE_INFORMATION>(ptr));
                    ptr = new IntPtr(ptr.ToInt64() + structSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return handleList;
        }

        private string? GetHandleName(NativeHelper.SYSTEM_HANDLE_INFORMATION handleInfo, int processId)
        {
            IntPtr sourceProcessHandle = IntPtr.Zero;
            IntPtr duplicatedHandle = IntPtr.Zero;

            try
            {
                sourceProcessHandle = NativeHelper.OpenProcess(NativeHelper.ProcessAccessFlags.DuplicateHandle, false, processId);
                if (sourceProcessHandle == IntPtr.Zero) return null;

                if (!NativeHelper.DuplicateHandle(sourceProcessHandle, (IntPtr)handleInfo.Handle, NativeHelper.GetCurrentProcess(), out duplicatedHandle, 0, false, NativeHelper.DUPLICATE_SAME_ACCESS))
                    return null;

                return QueryObjectName(duplicatedHandle);
            }
            finally
            {
                if (duplicatedHandle != IntPtr.Zero) NativeHelper.CloseHandle(duplicatedHandle);
                if (sourceProcessHandle != IntPtr.Zero) NativeHelper.CloseHandle(sourceProcessHandle);
            }
        }

        private string? QueryObjectName(IntPtr handle)
        {
            int length = 0x1000;
            IntPtr buffer = Marshal.AllocHGlobal(length);

            try
            {
                uint status = NativeHelper.NtQueryObject(handle, NativeHelper.OBJECT_INFORMATION_CLASS.ObjectNameInformation, buffer, length, out _);
                if (status == NativeHelper.STATUS_SUCCESS)
                {
                    IntPtr stringPtr = Marshal.ReadIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4);
                    if (stringPtr != IntPtr.Zero)
                        return Marshal.PtrToStringUni(stringPtr);
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return null;
        }

        private bool CloseRemoteHandle(NativeHelper.SYSTEM_HANDLE_INFORMATION handleInfo, int processId)
        {
            IntPtr sourceProcessHandle = IntPtr.Zero;
            IntPtr targetHandle = IntPtr.Zero;
            try
            {
                sourceProcessHandle = NativeHelper.OpenProcess(NativeHelper.ProcessAccessFlags.DuplicateHandle, false, processId);
                return NativeHelper.DuplicateHandle(sourceProcessHandle, (IntPtr)handleInfo.Handle, NativeHelper.GetCurrentProcess(), out targetHandle, 0, false, NativeHelper.DUPLICATE_CLOSE_SOURCE);
            }
            finally
            {
                if (targetHandle != IntPtr.Zero) NativeHelper.CloseHandle(targetHandle);
                if (sourceProcessHandle != IntPtr.Zero) NativeHelper.CloseHandle(sourceProcessHandle);
            }
        }

        private async Task KillRobloxMutexSafe()
        {
            await Task.Run(() =>
            {
                try
                {
                    var mutexNames = new[] { "ROBLOX_singletonMutex", "ROBLOX_singletonEvent" };
                    var robloxProcesses = Process.GetProcessesByName("RobloxPlayerBeta");

                    if (robloxProcesses.Length == 0)
                    {
                        LogService.Log("No RobloxPlayerBeta processes found (pre-launch). Skipping mutex kill.", LogLevel.Info, "Process");
                        return;
                    }

                    var allHandles = GetSystemHandles();
                    int closed = 0;

                    foreach (var proc in robloxProcesses)
                    {
                        var procHandles = allHandles.Where(h => h.ProcessId == proc.Id).ToList();
                        foreach (var handle in procHandles)
                        {
                            string? name = null;
                            try { name = GetHandleName(handle, proc.Id); } catch { }
                            if (name == null) continue;

                            foreach (var mutexName in mutexNames)
                            {
                                if (name.EndsWith(mutexName, StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        if (CloseRemoteHandle(handle, proc.Id))
                                        {
                                            closed++;
                                            LogService.Log($"Closed handle '{mutexName}' in PID {proc.Id}", LogLevel.Info, "Process");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Error($"Failed to close handle '{mutexName}' in PID {proc.Id}: {ex.Message}", "Process");
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    LogService.Log($"Mutex kill complete. Closed {closed} handle(s) across {robloxProcesses.Length} process(es).", LogLevel.Info, "Process");
                }
                catch (Exception ex)
                {
                    LogService.Error($"Error in KillRobloxMutexSafe: {ex.Message}", "Process");
                }
            });
        }

        public void Dispose()
        {
            _monitoringTimer.Dispose();
            _httpClient.Dispose();
        }
    }
}


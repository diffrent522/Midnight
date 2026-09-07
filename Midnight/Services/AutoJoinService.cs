using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Midnight.Services
{
    public class AutoJoinService
    {
        private readonly RobloxRequestService _requestService;
        private readonly SettingsService _settingsService;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        private class AutoJoinSession
        {
            public long UserId { get; set; }
            public string AuthCookie { get; set; } = string.Empty;
            public Guid? LastGameId { get; set; }
            public long? PlaceId { get; set; }
        }

        private readonly ConcurrentDictionary<long, AutoJoinSession> _monitoredSessions = new();

        public int CheckIntervalSeconds { get; set; } = 5;

        public event Action<string>? OnLog;
        public event Action<bool>? OnStatusChanged;
        public event Action<long, string>? OnSessionStatusChanged;

        public Func<long, string?, string, Task>? RelaunchCallback { get; set; }

        public bool IsRunning => _isRunning;

        public int AutoRejoinDelaySeconds
        {
            get => _settingsService.CurrentSettings.AutoRejoinDelaySeconds;
            set
            {
                _settingsService.CurrentSettings.AutoRejoinDelaySeconds = value;
                _settingsService.SaveSettings();
            }
        }

        public AutoJoinService(RobloxRequestService requestService, DiscordWebhookService? webhookService = null)
        {
            _requestService = requestService;
            _settingsService = new SettingsService();
        }

        public void StartMonitoring(long userId, string cookie)
        {
            if (_monitoredSessions.ContainsKey(userId)) return;

            _monitoredSessions.TryAdd(userId, new AutoJoinSession { UserId = userId, AuthCookie = cookie });
            Log($"Started monitoring user: {userId}");

            if (!_isRunning) StartLoop();
        }

        public void StopMonitoring(long userId)
        {
            if (_monitoredSessions.TryRemove(userId, out _))
                Log($"Stopped monitoring user: {userId}");

            if (_monitoredSessions.IsEmpty)
                StopLoop();
        }

        public bool IsMonitoring(long userId) => _monitoredSessions.ContainsKey(userId);

        private void StartLoop()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            OnStatusChanged?.Invoke(true);
            Log("Auto Rejoiner started.");
            Task.Run(() => LoopAsync(_cts.Token));
        }

        private void StopLoop()
        {
            if (!_isRunning) return;
            _cts?.Cancel();
            _isRunning = false;
            OnStatusChanged?.Invoke(false);
            Log("Auto Rejoiner stopped.");
        }

        private async Task LoopAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(2000, token);

                while (!token.IsCancellationRequested)
                {
                    foreach (var kvp in _monitoredSessions)
                        await CheckSessionAndRejoinIfNeeded(kvp.Value, token);

                    await Task.Delay(CheckIntervalSeconds * 1000, token);
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Log($"Loop error: {ex.Message}");
                StopLoop();
            }
        }

        private async Task CheckSessionAndRejoinIfNeeded(AutoJoinSession session, CancellationToken token)
        {
            try
            {
                var (success, presence) = await _requestService.GetAuthenticatedUserPresenceAsync(session.AuthCookie, session.UserId);

                if (!success || presence == null)
                {
                    Log($"[{session.UserId}] Presence check failed.");
                    return;
                }

                bool isInGame = presence.UserPresenceType == 2;
                Guid? currentGameId = null;

                if (isInGame && !string.IsNullOrEmpty(presence.GameId) && Guid.TryParse(presence.GameId, out var gid))
                {
                    currentGameId = gid;
                    if (presence.PlaceId != null) session.PlaceId = presence.PlaceId;
                }

                if (isInGame)
                {
                    OnSessionStatusChanged?.Invoke(session.UserId, "Playing");
                    if (session.LastGameId != currentGameId)
                        session.LastGameId = currentGameId;
                }
                else
                {
                    if (session.LastGameId.HasValue)
                    {
                        if (session.PlaceId.HasValue)
                        {
                            int delay = _settingsService.CurrentSettings.AutoRejoinDelaySeconds;
                            Log($"[{session.UserId}] Disconnect detected. Waiting {delay}s...");
                            OnSessionStatusChanged?.Invoke(session.UserId, $"Waiting {delay}s...");

                            await Task.Delay(delay * 1000, token);

                            var (retryOk, retryPresence) = await _requestService.GetAuthenticatedUserPresenceAsync(session.AuthCookie, session.UserId);
                            if (retryOk && retryPresence?.UserPresenceType == 2)
                            {
                                Log($"[{session.UserId}] User reconnected. Rejoin cancelled.");
                                OnSessionStatusChanged?.Invoke(session.UserId, "Playing");
                                if (!string.IsNullOrEmpty(retryPresence.GameId) && Guid.TryParse(retryPresence.GameId, out var newGid))
                                    session.LastGameId = newGid;
                                if (retryPresence.PlaceId != null) session.PlaceId = retryPresence.PlaceId;
                                return;
                            }

                            OnSessionStatusChanged?.Invoke(session.UserId, "Rejoining...");

                            if (RelaunchCallback != null)
                            {
                                string? jobId = session.LastGameId.Value.ToString();
                                string placeId = session.PlaceId.Value.ToString();

                                var rootPlaceId = await _requestService.GetRootPlaceIdAsync(session.PlaceId.Value);
                                if (rootPlaceId.HasValue && rootPlaceId.Value != session.PlaceId.Value)
                                {
                                    Log($"[{session.UserId}] Sub-place detected. Falling back to root place {rootPlaceId.Value}.");
                                    placeId = rootPlaceId.Value.ToString();
                                    jobId = null;
                                }

                                await RelaunchCallback.Invoke(session.UserId, jobId, placeId);
                            }
                        }
                        else
                        {
                            Log($"[{session.UserId}] Disconnect detected but Place ID is missing. Cannot rejoin.");
                            OnSessionStatusChanged?.Invoke(session.UserId, "Error: Missing PlaceID");
                            session.LastGameId = null;
                        }
                    }
                    else
                    {
                        OnSessionStatusChanged?.Invoke(session.UserId, "Monitoring (Idle)");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[{session.UserId}] Check error: {ex.Message}");
                OnSessionStatusChanged?.Invoke(session.UserId, "Check Error");
            }
        }

        public void UpdateSessionInfo(long userId, long placeId, string jobId)
        {
            if (_monitoredSessions.TryGetValue(userId, out var session) &&
                Guid.TryParse(jobId, out var gid))
            {
                session.LastGameId = gid;
                session.PlaceId = placeId;
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke(message);
            LogService.Log(message, LogLevel.Info, "AutoJoin");
        }
    }
}


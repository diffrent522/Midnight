using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiscordRPC;
using DiscordRPC.Logging;

namespace Midnight.Services
{
    public class DiscordRpcService : IDisposable
    {
        private const string APPLICATION_ID = "1546508358364954725";

        private DiscordRpcClient? _client;
        private readonly SettingsService _settingsService;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private System.Threading.Timer? _invokeTimer;
        private bool _disposed;
        private bool _initialized;

        private static readonly Button[] _standardButtons = new[]
        {
            new Button { Label = "Join Discord", Url = "https://discord.gg/yourserver" },
            new Button { Label = "Download", Url = "https://github.com/diffrent522/Account-Manager" }
        };

        private static readonly ConcurrentDictionary<long, RobloxGameDetails> _gameDetailsCache = new();
        private static readonly HttpClient _http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "Roblox/WinInet");
            return client;
        }

        public DiscordRpcService(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Initialize()
        {
            if (!_settingsService.CurrentSettings.DiscordRpcEnabled) return;
            if (_initialized) return;

            try
            {
                _client = new DiscordRpcClient(APPLICATION_ID)
                {
                    Logger = new NullLogger(),
                    SkipIdenticalPresence = true
                };

                _client.OnReady += (_, msg) =>
                    LogService.Log($"Discord RPC connected as {msg.User.Username}.", LogLevel.Success, "RPC");

                _client.OnError += (_, msg) =>
                    LogService.Error($"Discord RPC error: {msg.Message}", "RPC");

                _client.OnConnectionFailed += (_, _) =>
                    LogService.Error("Discord RPC: could not connect to Discord. Is Discord running?", "RPC");

                _client.Initialize();
                _initialized = true;

                _invokeTimer = new System.Threading.Timer(_ =>
                {
                    try { _client?.Invoke(); }
                    catch { }
                }, null, 0, 1000);

                LogService.Log("Discord RPC initialized.", LogLevel.Info, "RPC");

                _ = SetIdlePresenceAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"Discord RPC init failed: {ex.Message}", "RPC");
            }
        }

        public async Task SetPresenceAsync(
            string details,
            string state,
            string? largeImageKey = null,
            string? largeImageText = null,
            DateTime? startTime = null,
            string? smallImageKey = null,
            string? smallImageText = null,
            Button[]? buttons = null)
        {
            if (!_initialized || _client == null || _client.IsDisposed) return;

            await _lock.WaitAsync();
            try
            {
                var assets = new Assets
                {
                    LargeImageText = Truncate(largeImageText ?? "Midnight", 128)
                };

                SetImageKey(assets, largeImageKey ?? "roblox", isLarge: true);

                if (!string.IsNullOrEmpty(smallImageKey))
                {
                    assets.SmallImageText = Truncate(smallImageText ?? string.Empty, 128);
                    SetImageKey(assets, smallImageKey, isLarge: false);
                }

                string cleanDetails = EnsureMinLength(Truncate(details, 128), 2);
                string cleanState = EnsureMinLength(Truncate(state, 128), 2);

                var presence = new RichPresence
                {
                    Details = cleanDetails,
                    State = cleanState,
                    Assets = assets,
                    Buttons = buttons ?? _standardButtons
                };

                if (presence.Buttons != null && presence.Buttons.Length > 2)
                {
                    LogService.Log($"Discord RPC: Too many buttons ({presence.Buttons.Length}); only first two will be used.", LogLevel.Warning, "RPC");
                    presence.Buttons = presence.Buttons.Take(2).ToArray();
                }

                LogService.Log($"Discord RPC buttons set: {string.Join(", ", presence.Buttons.Select(b => $"{b.Label}:{b.Url}"))}", LogLevel.Info, "RPC");

                if (startTime.HasValue)
                    presence.Timestamps = new Timestamps(startTime.Value.ToUniversalTime());

                _client.SetPresence(presence);
                try { _client.Invoke(); } catch { }
            }
            catch (Exception ex)
            {
                LogService.Error($"Discord RPC update failed: {ex.Message}", "RPC");
            }
            finally
            {
                _lock.Release();
            }
        }

        private static void SetImageKey(Assets assets, string key, bool isLarge)
        {
            string fieldName = isLarge ? "_largeimagekey" : "_smallimagekey";
            try
            {
                var field = typeof(Assets).GetField(fieldName,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    field.SetValue(assets, key);
                    return;
                }
            }
            catch { }

            if (isLarge)
                assets.LargeImageKey = key.Length <= 32 ? key : "roblox";
            else
                assets.SmallImageKey = key.Length <= 32 ? key : string.Empty;
        }

        public async Task SetGamePresenceAsync(
            string accountName,
            string? gameName,
            long? placeId,
            DateTime launchTime,
            string? serverType = null,
            string? avatarUrl = null)
        {
            string displayGameName = (!string.IsNullOrWhiteSpace(gameName) && gameName != "Unknown") ? gameName : "Roblox";
            string state = string.Empty;
            string largeImageKey = "roblox";
            string largeImageText = displayGameName;

            if (placeId.HasValue && placeId.Value > 0)
            {
                long pid = placeId.Value;
                var details = await FetchGameDetailsAsync(pid);

                if (details != null)
                {
                    if (!string.IsNullOrEmpty(details.Name))
                        displayGameName = details.Name;

                    if (!string.IsNullOrEmpty(details.ThumbnailUrl))
                        largeImageKey = details.ThumbnailUrl;

                    largeImageText = displayGameName;

                    if (serverType == "Private Server")
                        state = "In a private server";
                    else if (serverType == "Reserved/Job")
                        state = "In a reserved server";
                    else if (!string.IsNullOrEmpty(details.CreatorName))
                        state = $"by {details.CreatorName}";
                }
            }

            if (string.IsNullOrEmpty(state))
            {
                state = serverType == "Private Server"
                    ? "In a private server"
                    : $"Playing as {accountName}";
            }

            string smallImageKey = !string.IsNullOrEmpty(avatarUrl) ? avatarUrl : "roblox";
            string smallImageText = $"Playing as {accountName}";

            LogService.Log($"Discord RPC: Setting game presence -> {displayGameName} ({state}), image: {(largeImageKey.Length > 50 ? largeImageKey[..50] + "…" : largeImageKey)}", LogLevel.Info, "RPC");

            await SetPresenceAsync(
                details: displayGameName,
                state: state,
                largeImageKey: largeImageKey,
                largeImageText: largeImageText,
                startTime: launchTime,
                smallImageKey: smallImageKey,
                smallImageText: smallImageText,
                buttons: _standardButtons);
        }

        private static async Task<RobloxGameDetails?> FetchGameDetailsAsync(long placeId)
        {
            if (_gameDetailsCache.TryGetValue(placeId, out var cached))
                return cached;

            try
            {
                var details = new RobloxGameDetails();

                try
                {
                    string iconJson = await _http.GetStringAsync(
                        $"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={placeId}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false");
                    using var iDoc = JsonDocument.Parse(iconJson);
                    if (iDoc.RootElement.TryGetProperty("data", out var iData) && iData.GetArrayLength() > 0)
                    {
                        details.ThumbnailUrl = iData[0].GetProperty("imageUrl").GetString();
                    }
                }
                catch { }

                try
                {
                    string universeJson = await _http.GetStringAsync(
                        $"https://apis.roblox.com/universes/v1/places/{placeId}/universe");
                    using var uDoc = JsonDocument.Parse(universeJson);
                    if (uDoc.RootElement.TryGetProperty("universeId", out var uIdEl))
                    {
                        details.UniverseId = uIdEl.GetInt64();

                        try
                        {
                            string gamesJson = await _http.GetStringAsync(
                                $"https://games.roblox.com/v1/games?universeIds={details.UniverseId}");
                            using var gDoc = JsonDocument.Parse(gamesJson);
                            if (gDoc.RootElement.TryGetProperty("data", out var gData) && gData.GetArrayLength() > 0)
                            {
                                var gObj = gData[0];
                                if (gObj.TryGetProperty("name", out var nameEl))
                                    details.Name = nameEl.GetString() ?? string.Empty;

                                if (gObj.TryGetProperty("creator", out var cObj) && cObj.TryGetProperty("name", out var cNameEl))
                                    details.CreatorName = cNameEl.GetString() ?? string.Empty;
                            }
                        }
                        catch { }

                        if (string.IsNullOrEmpty(details.ThumbnailUrl))
                        {
                            try
                            {
                                string thumbJson = await _http.GetStringAsync(
                                    $"https://thumbnails.roblox.com/v1/games/icons?universeIds={details.UniverseId}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false");
                                using var tDoc = JsonDocument.Parse(thumbJson);
                                if (tDoc.RootElement.TryGetProperty("data", out var tData) && tData.GetArrayLength() > 0)
                                {
                                    details.ThumbnailUrl = tData[0].GetProperty("imageUrl").GetString();
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                _gameDetailsCache[placeId] = details;
                return details;
            }
            catch (Exception ex)
            {
                LogService.Log($"Failed to fetch game details for place {placeId}: {ex.Message}", LogLevel.Warning, "RPC");
                return null;
            }
        }

        public Task SetIdlePresenceAsync()
        {
            return SetPresenceAsync(
                details: "Managing Accounts",
                state: "Idle",
                largeImageKey: "roblox",
                largeImageText: "Midnight",
                startTime: DateTime.UtcNow,
                buttons: _standardButtons);
        }

        public void ClearPresence()
        {
            if (!_initialized || _client == null || _client.IsDisposed) return;
            try { _client.ClearPresence(); }
            catch { }
        }

        public void Shutdown()
        {
            if (!_initialized || _client == null || _client.IsDisposed) return;
            _invokeTimer?.Dispose();
            _invokeTimer = null;
            try
            {
                _client.ClearPresence();
                _client.Deinitialize();
                _initialized = false;
                LogService.Log("Discord RPC shut down.", LogLevel.Info, "RPC");
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Shutdown();
            _client?.Dispose();
            _lock.Dispose();
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];

        private static string EnsureMinLength(string value, int minLength)
        {
            if (string.IsNullOrEmpty(value)) return new string('\u2800', minLength);
            if (value.Length < minLength) return value.PadRight(minLength, '\u2800');
            return value;
        }

        private class RobloxGameDetails
        {
            public long UniverseId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string CreatorName { get; set; } = string.Empty;
            public string? ThumbnailUrl { get; set; }
        }
    }
}
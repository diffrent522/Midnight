using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Midnight.Services
{
    public class RobloxRequestService
    {
        private const string UserAgent = "Midnight/1.0";

        public async Task<(bool success, string result)> LoginWithCredentialsAsync(string username, string password)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    CookieContainer = new CookieContainer(),
                    UseCookies = true,
                    AllowAutoRedirect = true
                };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.Add("User-Agent", "Roblox/WinInet");
                client.DefaultRequestHeaders.Add("Referer", "https://www.roblox.com/");

                const string loginUrl = "https://auth.roblox.com/v2/login";
                var payload = new { ctype = "Username", cvalue = username, password = password };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync(loginUrl, content);

                if (response.Headers.TryGetValues("x-csrf-token", out var tokens))
                {
                    string csrf = string.Join("", tokens);
                    client.DefaultRequestHeaders.Remove("x-csrf-token");
                    client.DefaultRequestHeaders.Add("x-csrf-token", csrf);
                    content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    response = await client.PostAsync(loginUrl, content);
                }

                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var cookies = handler.CookieContainer.GetCookies(new Uri("https://www.roblox.com"));
                    var roblosecurity = cookies[".ROBLOSECURITY"]?.Value;
                    return !string.IsNullOrEmpty(roblosecurity)
                        ? (true, roblosecurity)
                        : (false, "Login succeeded but could not extract session cookie.");
                }

                string errorMsg = $"Login failed ({(int)response.StatusCode})";
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
                    {
                        var msg = errors[0].GetProperty("message").GetString();
                        if (!string.IsNullOrEmpty(msg)) errorMsg = msg;
                    }
                }
                catch { }

                if ((response.StatusCode == HttpStatusCode.Forbidden) &&
                    (responseBody.Contains("TwoStepVerification") || responseBody.Contains("twoStepVerification")))
                    return (false, "Two-step verification is enabled. Please use Browser Login instead.");

                return (false, errorMsg);
            }
            catch (Exception ex)
            {
                return (false, $"Request error: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> SendFriendRequestAsync(string cookie, long targetUserId)
        {
            try
            {
                using var client = CreateAuthenticatedClient(cookie);
                string url = $"https://friends.roblox.com/v1/users/{targetUserId}/request-friendship";
                var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                var response = await ExecuteWithCsrfAsync(client, url, content);

                if (response.IsSuccessStatusCode)
                    return (true, "Friend request sent successfully.");

                string errorBody = await response.Content.ReadAsStringAsync();
                return (false, $"Failed ({(int)response.StatusCode}): {errorBody}");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        public async Task<long?> GetUserIdFromUsernameAsync(string username)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

                var payload = new { usernames = new[] { username }, excludeBannedUsers = true };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://users.roblox.com/v1/usernames/users", content);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseBody);
                    var data = doc.RootElement.GetProperty("data");
                    if (data.GetArrayLength() > 0)
                        return data[0].GetProperty("id").GetInt64();
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"User lookup error: {ex.Message}");
            }
            return null;
        }

        public async Task<RobloxUserInfo?> GetUserInfoAsync(long userId)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

                var response = await client.GetStringAsync($"https://users.roblox.com/v1/users/{userId}");
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var info = new RobloxUserInfo
                {
                    Id = root.GetProperty("id").GetInt64(),
                    Name = root.GetProperty("name").GetString() ?? "",
                    DisplayName = root.GetProperty("displayName").GetString() ?? "",
                    Description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : ""
                };

                info.AvatarUrl = await GetUserAvatarAsync(userId)
                    ?? "https://tr.rbxcdn.com/53eb9b17fe1432a809c73a132d78f5f1/150/150/AvatarHeadshot/Png";

                var presence = await GetUserPresenceAsync(userId);
                if (presence != null)
                {
                    info.Presence = presence.UserPresenceType;
                    info.LastLocation = presence.LastLocation;
                    info.PlaceId = presence.PlaceId;

                    if (info.Presence == 2 && info.PlaceId.HasValue)
                    {
                        var universeId = await GetUniverseIdAsync(info.PlaceId.Value);
                        if (universeId.HasValue)
                        {
                            var gameName = await GetGameNameAsync(universeId.Value);
                            if (!string.IsNullOrEmpty(gameName))
                                info.LastLocation = gameName;
                        }
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                LogService.Error($"Error fetching user info for {userId}: {ex.Message}");
                return null;
            }
        }

        public async Task<long?> GetUniverseIdAsync(long placeId)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response = await client.GetStringAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe");
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("universeId", out var uId))
                    return uId.GetInt64();
            }
            catch { }
            return null;
        }

        public async Task<string?> GetGameNameAsync(long universeId)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response = await client.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={universeId}");
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                    return data[0].GetProperty("name").GetString();
            }
            catch { }
            return null;
        }

        public async Task<long?> GetRootPlaceIdAsync(long placeId)
        {
            try
            {
                var universeId = await GetUniverseIdAsync(placeId);
                if (universeId == null) return null;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response = await client.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={universeId}");
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                    return data[0].GetProperty("rootPlaceId").GetInt64();
            }
            catch { }
            return null;
        }

        public async Task<string?> GetGameNameFromPlaceIdAsync(long placeId)
        {
            var universeId = await GetUniverseIdAsync(placeId);
            return universeId.HasValue ? await GetGameNameAsync(universeId.Value) : null;
        }

        public async Task<RobloxUserPresence?> GetUserPresenceAsync(long userId)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

                var payload = new { userIds = new[] { userId } };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://presence.roblox.com/v1/presence/users", content);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("userPresences", out var presences) && presences.GetArrayLength() > 0)
                        return ParsePresence(presences[0]);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Error fetching presence: {ex.Message}");
            }
            return null;
        }

        public async Task<(bool success, RobloxUserPresence? presence)> GetAuthenticatedUserPresenceAsync(string cookie, long userId)
        {
            try
            {
                using var client = CreateAuthenticatedClient(cookie);
                var payload = new { userIds = new[] { userId } };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://presence.roblox.com/v1/presence/users", content);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("userPresences", out var presences) && presences.GetArrayLength() > 0)
                        return (true, ParsePresence(presences[0]));
                }
                return (false, null);
            }
            catch
            {
                return (false, null);
            }
        }

        private static RobloxUserPresence ParsePresence(JsonElement p)
        {
            return new RobloxUserPresence
            {
                UserPresenceType = p.GetProperty("userPresenceType").GetInt32(),
                LastLocation = p.GetProperty("lastLocation").GetString() ?? "Unknown",
                PlaceId = p.TryGetProperty("placeId", out var pid) && pid.ValueKind == JsonValueKind.Number ? pid.GetInt64() : null,
                GameId = p.TryGetProperty("gameId", out var gid) ? gid.GetString() : null
            };
        }

        public async Task<string?> GetUserAvatarAsync(long userId)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response = await client.GetStringAsync(
                    $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false");
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                    return data[0].GetProperty("imageUrl").GetString();
            }
            catch { }
            return null;
        }

        public async Task<string> GetAuthenticationTicket(string cookie, string? proxyUrl = null)
        {
            try
            {
                using var client = CreateAuthenticatedClient(cookie, proxyUrl);
                client.DefaultRequestHeaders.Remove("User-Agent");
                client.DefaultRequestHeaders.Add("User-Agent", "Roblox/WinInet");

                var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://auth.roblox.com/v1/authentication-ticket", content);

                if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.Contains("x-csrf-token"))
                {
                    string csrf = System.Linq.Enumerable.First(response.Headers.GetValues("x-csrf-token"));
                    client.DefaultRequestHeaders.Remove("x-csrf-token");
                    client.DefaultRequestHeaders.Add("x-csrf-token", csrf);
                    content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                    response = await client.PostAsync("https://auth.roblox.com/v1/authentication-ticket", content);
                }

                if (response.IsSuccessStatusCode)
                {
                    if (response.Headers.TryGetValues("rbx-authentication-ticket", out var values))
                        return System.Linq.Enumerable.First(values);
                    return await response.Content.ReadAsStringAsync();
                }

                string errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Auth failed: {response.StatusCode} — {errorBody}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Request error: {ex.Message}");
            }
        }

        private static HttpClient CreateAuthenticatedClient(string cookie, string? proxyUrl = null)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };

            if (!string.IsNullOrEmpty(proxyUrl))
            {
                try
                {
                    handler.Proxy = new WebProxy(proxyUrl);
                    handler.UseProxy = true;
                }
                catch { }
            }

            handler.CookieContainer.Add(new Cookie(".ROBLOSECURITY", cookie)
            {
                Domain = ".roblox.com",
                Path = "/"
            });

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            client.DefaultRequestHeaders.Add("Referer", "https://www.roblox.com/");
            return client;
        }

        private static async Task<HttpResponseMessage> ExecuteWithCsrfAsync(HttpClient client, string url, HttpContent content)
        {
            var response = await client.PostAsync(url, content);

            if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.Contains("x-csrf-token"))
            {
                string csrfToken = string.Join("", response.Headers.GetValues("x-csrf-token"));
                client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
                client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);
                response = await client.PostAsync(url, content);
            }

            return response;
        }
    }

    public class RobloxUserInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public int Presence { get; set; }
        public string LastLocation { get; set; } = string.Empty;
        public long? PlaceId { get; set; }

        public string PresenceText => Presence switch
        {
            1 => "Online",
            2 => string.IsNullOrEmpty(LastLocation) ? "In Game" : $"In Game: {LastLocation}",
            3 => "In Studio",
            _ => "Offline"
        };

        public string PresenceColor => Presence switch
        {
            1 => "#00B0FF",
            2 => "#00CC66",
            3 => "#FF9800",
            _ => "#999999"
        };
    }

    public class RobloxUserPresence
    {
        public int UserPresenceType { get; set; }
        public string LastLocation { get; set; } = "";
        public long? PlaceId { get; set; }
        public string? GameId { get; set; }
    }
}


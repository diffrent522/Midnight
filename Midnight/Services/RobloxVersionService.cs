using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Midnight.Services
{
    public class RobloxVersionService
    {
        private readonly HttpClient _httpClient;

        public RobloxVersionService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WEAO-3PService");
        }

        public async Task<RobloxVersionInfo?> GetCurrentVersion()
        {
            return await FetchVersion("https://weao.xyz/api/versions/current");
        }

        public async Task<RobloxVersionInfo?> GetFutureVersion()
        {
            return await FetchVersion("https://weao.xyz/api/versions/future");
        }

        public async Task<RobloxVersionInfo?> GetPastVersion()
        {
            return await FetchVersion("https://weao.xyz/api/versions/past");
        }

        private async Task<RobloxVersionInfo?> FetchVersion(string url)
        {
            try
            {
                LogService.Log($"Fetching version info from: {url}", LogLevel.Info, "Version");
                var response = await _httpClient.GetStringAsync(url);
                using (var doc = JsonDocument.Parse(response))
                {
                    var root = doc.RootElement;
                    LogService.Log($"Successfully fetched version data.", LogLevel.Info, "Version");
                    return new RobloxVersionInfo
                    {
                        WindowsVersion = GetStringSafe(root, "Windows"),
                        WindowsDate = GetStringSafe(root, "WindowsDate"),
                        MacVersion = GetStringSafe(root, "Mac"),
                        MacDate = GetStringSafe(root, "MacDate"),
                        AndroidVersion = GetStringSafe(root, "Android"),
                        AndroidDate = GetStringSafe(root, "AndroidDate"),
                        iOSVersion = GetStringSafe(root, "iOS"),
                        iOSDate = GetStringSafe(root, "iOSDate")
                    };
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Error fetching version from {url}: {ex.Message}", "Version");
                return null;
            }
        }

        private string GetStringSafe(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? "N/A";
            }
            return "N/A";
        }

        public async Task<WeaoSearchResult> SearchWeaoVersionAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new WeaoSearchResult { Success = false, Details = "Version query cannot be empty." };
            }

            string cleanQuery = query.Trim();
            if (!cleanQuery.StartsWith("version-", StringComparison.OrdinalIgnoreCase) && cleanQuery.Length >= 8 && !cleanQuery.Contains('.'))
            {
                cleanQuery = "version-" + cleanQuery.ToLowerInvariant();
            }

            try
            {
                var past = await GetPastVersion();
                if (past?.WindowsVersion != null && (past.WindowsVersion.Equals(cleanQuery, StringComparison.OrdinalIgnoreCase) || cleanQuery.Equals("past", StringComparison.OrdinalIgnoreCase)))
                {
                    return new WeaoSearchResult
                    {
                        Success = true,
                        VersionHash = past.WindowsVersion,
                        Details = $"Verified from WEAO Past (Downgrade): {past.WindowsDate}"
                    };
                }

                var current = await GetCurrentVersion();
                if (current?.WindowsVersion != null && (current.WindowsVersion.Equals(cleanQuery, StringComparison.OrdinalIgnoreCase) || cleanQuery.Equals("current", StringComparison.OrdinalIgnoreCase)))
                {
                    return new WeaoSearchResult
                    {
                        Success = true,
                        VersionHash = current.WindowsVersion,
                        Details = $"Verified from WEAO Current: {current.WindowsDate}"
                    };
                }

                var future = await GetFutureVersion();
                if (future?.WindowsVersion != null && (future.WindowsVersion.Equals(cleanQuery, StringComparison.OrdinalIgnoreCase) || cleanQuery.Equals("future", StringComparison.OrdinalIgnoreCase)))
                {
                    return new WeaoSearchResult
                    {
                        Success = true,
                        VersionHash = future.WindowsVersion,
                        Details = $"Verified from WEAO Future: {future.WindowsDate}"
                    };
                }

                try
                {
                    var exploitsJson = await _httpClient.GetStringAsync("https://weao.xyz/api/status/exploits");
                    using var doc = JsonDocument.Parse(exploitsJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            string rbxVersion = GetStringSafe(item, "rbxversion");
                            string title = GetStringSafe(item, "title");
                            if (rbxVersion.Equals(cleanQuery, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(title) && cleanQuery.Contains(title, StringComparison.OrdinalIgnoreCase)))
                            {
                                return new WeaoSearchResult
                                {
                                    Success = true,
                                    VersionHash = rbxVersion,
                                    Details = $"Found in WEAO exploit status (Used by {title})"
                                };
                            }
                        }
                    }
                }
                catch
                {
                }

                if (cleanQuery.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
                {
                    return new WeaoSearchResult
                    {
                        Success = true,
                        VersionHash = cleanQuery,
                        Details = $"Target version format: {cleanQuery} (Ready for WEAO RDD download)"
                    };
                }

                return new WeaoSearchResult
                {
                    Success = false,
                    Details = "Could not resolve a valid Roblox version hash from WEAO API."
                };
            }
            catch (Exception ex)
            {
                return new WeaoSearchResult
                {
                    Success = false,
                    Details = $"WEAO API error: {ex.Message}"
                };
            }
        }
    }

    public class RobloxVersionInfo
    {
        public string? WindowsVersion { get; set; }
        public string? WindowsDate { get; set; }
        public string? MacVersion { get; set; }
        public string? MacDate { get; set; }
        public string? AndroidVersion { get; set; }
        public string? AndroidDate { get; set; }
        public string? iOSVersion { get; set; }
        public string? iOSDate { get; set; }
    }

    public class WeaoSearchResult
    {
        public bool Success { get; set; }
        public string VersionHash { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
}


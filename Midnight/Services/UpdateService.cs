using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading.Tasks;
using Midnight.Models;

namespace Midnight.Services
{
    public class UpdateService
    {
        private const string REPO_OWNER = "diffrent522";
        private const string REPO_NAME = "Midnight";
        private const string LAST_CHECK_FILE = "last_update_check.json";

        private readonly string _lastCheckFilePath;

        public UpdateService()
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Midnight");
                Directory.CreateDirectory(folder);
                _lastCheckFilePath = Path.Combine(folder, LAST_CHECK_FILE);
            }
            catch
            {
                _lastCheckFilePath = LAST_CHECK_FILE;
            }
        }

        public async Task<GithubRelease?> CheckForUpdatesAsync(bool force = false)
        {
            try
            {
                if (!force && ShouldSkipCheck()) return null;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Midnight");

                var release = await client.GetFromJsonAsync<GithubRelease>(
                    $"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/latest");

                if (release == null) return null;

                File.WriteAllText(_lastCheckFilePath, DateTime.Now.ToString("o"));

                string currentVersionStr = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
                string remoteTag = release.TagName.TrimStart('v').Split('-')[0];

                if (Version.TryParse(currentVersionStr, out var current) &&
                    Version.TryParse(remoteTag, out var remote) &&
                    remote > current)
                {
                    return release;
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        private bool ShouldSkipCheck()
        {
            try
            {
                if (!File.Exists(_lastCheckFilePath)) return false;
                string content = File.ReadAllText(_lastCheckFilePath);
                if (DateTime.TryParse(content, out DateTime lastCheck))
                    return (DateTime.Now - lastCheck).TotalHours < 24;
            }
            catch { }
            return false;
        }

        public async Task DownloadAndInstallAsync(GithubRelease release)
        {
            try
            {
                var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe"));
                if (asset == null) return;

                string tempFile = Path.Combine(Path.GetTempPath(), asset.Name);

                using var client = new HttpClient();
                var data = await client.GetByteArrayAsync(asset.BrowserDownloadUrl);
                await File.WriteAllBytesAsync(tempFile, data);

                Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                LogService.Error($"Update install failed: {ex.Message}", "Update");
            }
        }
    }
}


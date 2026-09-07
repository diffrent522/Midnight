using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Midnight.Models;

namespace Midnight.Services
{
    public class AccountStorageService
    {
        private readonly string _filePath;

        public AccountStorageService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "Midnight");

            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                _filePath = Path.Combine(folder, "accounts.json");

                if (!File.Exists(_filePath))
                    TryImportLegacyAccounts(appData, _filePath);
            }
            catch
            {
                _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json");
            }
        }

        public void SaveAccounts(IEnumerable<RobloxAccount> accounts)
        {
            try
            {
                var json = JsonSerializer.Serialize(accounts,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to save accounts: {ex.Message}", "Storage");
            }
        }

        public List<RobloxAccount> LoadAccounts()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new List<RobloxAccount>();

                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<RobloxAccount>>(json)
                       ?? new List<RobloxAccount>();
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to load accounts: {ex.Message}", "Storage");
                return new List<RobloxAccount>();
            }
        }

        private static void TryImportLegacyAccounts(string appData, string targetPath)
        {
            var candidates = new[]
            {
                Path.Combine(appData, "Lunar", "accounts.json"),
                Path.Combine(appData, "RobloxAccountManager", "accounts.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json"),
            };

            foreach (var src in candidates)
            {
                if (!File.Exists(src)) continue;

                try
                {
                    var raw = File.ReadAllText(src);
                    var list = JsonSerializer.Deserialize<List<RobloxAccount>>(raw);
                    if (list != null && list.Count > 0)
                    {
                        File.Copy(src, targetPath, overwrite: false);
                        LogService.Log(
                            $"Imported {list.Count} account(s) from legacy path: {src}",
                            LogLevel.Success, "Storage");
                        return;
                    }
                }
                catch
                {
                }
            }
        }
    }
}
using System;
using System.IO;
using System.Text.Json;
using Midnight.Models;

namespace Midnight.Services
{
    /// <summary>
    /// Stores and loads app settings from %APPDATA%\Midnight\settings.json.
    ///
    /// On first run it automatically imports settings from older app versions
    /// in the following priority order:
    ///   1. %APPDATA%\Lunar\settings.json
    ///   2. %APPDATA%\RobloxAccountManager\settings.json
    ///   3. settings.json next to the exe  (old flat-file layout)
    /// </summary>
    public class SettingsService
    {
        private const string FILE_NAME = "settings.json";
        private readonly string _filePath;

        public AppSettings CurrentSettings { get; private set; } = new();

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder   = Path.Combine(appData, "Midnight");

            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                _filePath = Path.Combine(folder, FILE_NAME);

                // ── Legacy import (runs once, only when no Midnight settings exist yet) ──
                if (!File.Exists(_filePath))
                    TryImportLegacySettings(appData, _filePath);
            }
            catch
            {
                _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FILE_NAME);
            }

            CurrentSettings = new AppSettings();
            LoadSettings();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(CurrentSettings,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                var json = File.ReadAllText(_filePath);
                CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        private static void TryImportLegacySettings(string appData, string targetPath)
        {
            var candidates = new[]
            {
                Path.Combine(appData, "Lunar",                FILE_NAME),
                Path.Combine(appData, "RobloxAccountManager", FILE_NAME),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FILE_NAME),
            };

            foreach (var src in candidates)
            {
                if (!File.Exists(src)) continue;

                try
                {
                    // Validate JSON before importing
                    var raw = File.ReadAllText(src);
                    var settings = JsonSerializer.Deserialize<AppSettings>(raw);
                    if (settings != null)
                    {
                        File.Copy(src, targetPath, overwrite: false);
                        System.Diagnostics.Debug.WriteLine(
                            $"[Midnight] Imported settings from legacy path: {src}");
                        return;
                    }
                }
                catch { }
            }
        }
    }
}

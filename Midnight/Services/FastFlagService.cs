using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Midnight.Models;

namespace Midnight.Services
{
    public class FastFlagService
    {
        private readonly SettingsService _settingsService;

        public static readonly IReadOnlyList<FastFlagPreset> BuiltInPresets = new List<FastFlagPreset>
        {
            new FastFlagPreset
            {
                Id = "fps_unlock",
                DisplayName = "FPS Unlocker",
                Description = "Removes the 60 FPS frame cap so Roblox runs at your monitor's full refresh rate.",
                Flags = new Dictionary<string, string>
                {
                    ["DFIntTaskSchedulerTargetFps"] = "9999"
                }
            },
            new FastFlagPreset
            {
                Id = "show_fps",
                DisplayName = "Show In-Game FPS Counter",
                Description = "Displays a native, live FPS and performance counter in the top-right corner of the Roblox screen.",
                Flags = new Dictionary<string, string>
                {
                    ["FFlagDebugDisplayFPS"] = "true"
                }
            },
            new FastFlagPreset
            {
                Id = "exclusive_fullscreen",
                DisplayName = "Exclusive Fullscreen (Alt+Enter)",
                Description = "Enables true exclusive fullscreen mode when pressing Alt+Enter for lowest input latency.",
                Flags = new Dictionary<string, string>
                {
                    ["FFlagHandleAltEnterFullscreenManually"] = "false"
                }
            },
            new FastFlagPreset
            {
                Id = "dpi_fix",
                DisplayName = "DPI Scaling Fix",
                Description = "Fixes blurry rendering when Windows display scaling is set above 100%.",
                Flags = new Dictionary<string, string>
                {
                    ["DFFlagDisableDPIScale"] = "true"
                }
            },
            new FastFlagPreset
            {
                Id = "texture_quality",
                DisplayName = "Maximum Texture Quality",
                Description = "Forces Roblox to render all textures at maximum fidelity without downscaling.",
                Flags = new Dictionary<string, string>
                {
                    ["DFFlagTextureQualityOverrideEnabled"] = "true",
                    ["DFIntTextureQualityOverride"] = "3"
                }
            },
            new FastFlagPreset
            {
                Id = "msaa_4x",
                DisplayName = "Anti-Aliasing (MSAA 4x)",
                Description = "Forces 4x Multi-Sample Anti-Aliasing for smoother edges and reduced jaggedness.",
                Flags = new Dictionary<string, string>
                {
                    ["FIntDebugForceMSAASamples"] = "4"
                }
            },
            new FastFlagPreset
            {
                Id = "low_graphics",
                DisplayName = "Low Graphics / Potato Mode",
                Description = "Forces the lowest rendering fidelity. Maximises performance on weak hardware.",
                Flags = new Dictionary<string, string>
                {
                    ["DFIntDebugFRMQualityLevelOverride"] = "1"
                }
            },
            new FastFlagPreset
            {
                Id = "disable_telemetry",
                DisplayName = "Disable Telemetry",
                Description = "Turns off Roblox's diagnostic telemetry and crash reporter.",
                Flags = new Dictionary<string, string>
                {
                    ["FFlagDebugDisableTelemetryEphemeralCounter"] = "true",
                    ["FFlagDebugDisableTelemetryEphemeralStat"] = "true",
                    ["FFlagDebugDisableTelemetryEventIngest"] = "true",
                    ["FFlagDebugDisableTelemetryPoint"] = "true",
                    ["FFlagDebugDisableTelemetryV2Counter"] = "true",
                    ["FFlagDebugDisableTelemetryV2Event"] = "true",
                    ["FFlagDebugDisableTelemetryV2Stat"] = "true"
                }
            }
        };

        public FastFlagService(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public List<string> GetClientSettingsPaths()
        {
            var paths = new List<string>();

            foreach (string versionsDir in GetVersionDirectories())
            {
                if (!Directory.Exists(versionsDir)) continue;

                TryAddIfHasRoblox(versionsDir, paths);

                foreach (var sub in Directory.GetDirectories(versionsDir))
                    TryAddIfHasRoblox(sub, paths);
            }

            return paths;
        }

        private static void TryAddIfHasRoblox(string dir, List<string> list)
        {
            string exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            string settingsDir = Path.Combine(dir, "ClientSettings");
            string jsonPath = Path.Combine(settingsDir, "ClientAppSettings.json");

            if (File.Exists(exe) || Directory.Exists(settingsDir) || dir.EndsWith("Modifications", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(jsonPath);
            }
        }

        private IEnumerable<string> GetVersionDirectories()
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string custom = _settingsService.CurrentSettings.CustomRobloxPath;
            if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom))
                dirs.Add(custom);

            string download = _settingsService.CurrentSettings.DownloadPath;
            if (!string.IsNullOrWhiteSpace(download) && Directory.Exists(download))
                dirs.Add(download);

            string bloxstrapMods = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bloxstrap", "Modifications");
            if (Directory.Exists(bloxstrapMods))
                dirs.Add(bloxstrapMods);

            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"));
            dirs.Add(@"C:\Program Files (x86)\Roblox\Versions");
            dirs.Add(@"C:\Program Files\Roblox\Versions");

            return dirs;
        }

        public Dictionary<string, string> ReadFlags()
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string path in GetClientSettingsPaths())
            {
                if (!File.Exists(path)) continue;

                try
                {
                    string json = File.ReadAllText(path);
                    var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (raw == null) continue;

                    foreach (var kv in raw)
                    {
                        string val = kv.Value.ValueKind == JsonValueKind.String
                            ? kv.Value.GetString() ?? string.Empty
                            : kv.Value.ToString();
                        merged[kv.Key] = val;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"Failed to read {path}: {ex.Message}", "FastFlags");
                }
            }

            return merged;
        }

        public async Task ApplyFlagsAsync(Dictionary<string, string> flags)
        {
            if (flags.Count == 0)
            {
                ClearAllFiles();
                return;
            }

            var options = new JsonSerializerOptions { WriteIndented = true };

            var jsonDict = new Dictionary<string, object>(flags.Count);
            foreach (var kv in flags)
            {
                if (bool.TryParse(kv.Value, out bool b))
                    jsonDict[kv.Key] = b;
                else if (long.TryParse(kv.Value, out long l))
                    jsonDict[kv.Key] = l;
                else if (double.TryParse(kv.Value, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double d))
                    jsonDict[kv.Key] = d;
                else
                    jsonDict[kv.Key] = kv.Value;
            }

            string json = JsonSerializer.Serialize(jsonDict, options);

            foreach (string filePath in GetClientSettingsPaths())
            {
                try
                {
                    string? dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    await File.WriteAllTextAsync(filePath, json);
                    LogService.Log($"Fast flags written to: {filePath}", LogLevel.Success, "FastFlags");
                }
                catch (Exception ex)
                {
                    LogService.Error($"Failed to write {filePath}: {ex.Message}", "FastFlags");
                }
            }
        }

        public static Dictionary<string, string> MergeFlags(
            IEnumerable<FastFlagEntry> customFlags,
            IEnumerable<string> enabledPresetIds)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string id in enabledPresetIds)
            {
                var preset = BuiltInPresets.FirstOrDefault(p => p.Id == id);
                if (preset == null) continue;
                foreach (var kv in preset.Flags)
                    result[kv.Key] = kv.Value;
            }

            foreach (var entry in customFlags)
            {
                if (!string.IsNullOrWhiteSpace(entry.Name))
                    result[entry.Name] = entry.Value;
            }

            return result;
        }

        private void ClearAllFiles()
        {
            foreach (string path in GetClientSettingsPaths())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.WriteAllText(path, "{}");
                        LogService.Log($"Fast flags cleared: {path}", LogLevel.Info, "FastFlags");
                    }
                }
                catch { }
            }
        }
    }
}
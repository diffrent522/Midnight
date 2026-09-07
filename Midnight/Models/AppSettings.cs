using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Midnight.Models
{
    public class AppSettings
    {
        [JsonPropertyName("customRobloxPath")]
        public string CustomRobloxPath { get; set; } = string.Empty;

        [JsonPropertyName("downloadPath")]
        public string DownloadPath { get; set; } = string.Empty;

        [JsonPropertyName("executorPath")]
        public string ExecutorPath { get; set; } = string.Empty;

        [JsonPropertyName("autoLaunchExecutor")]
        public bool AutoLaunchExecutor { get; set; } = false;

        [JsonPropertyName("autoRejoinDelaySeconds")]
        public int AutoRejoinDelaySeconds { get; set; } = 15;

        [JsonPropertyName("discordWebhookUrl")]
        public string DiscordWebhookUrl { get; set; } = string.Empty;

        [JsonPropertyName("soundEffectsEnabled")]
        public bool SoundEffectsEnabled { get; set; } = true;

        [JsonPropertyName("animationsEnabled")]
        public bool AnimationsEnabled { get; set; } = true;

        [JsonPropertyName("discordRpcEnabled")]
        public bool DiscordRpcEnabled { get; set; } = true;

        [JsonPropertyName("customRobloxIconPath")]
        public string CustomRobloxIconPath { get; set; } = string.Empty;

        [JsonPropertyName("minimizeToTray")]
        public bool MinimizeToTray { get; set; } = true;

        [JsonPropertyName("fastFlagsEnabled")]
        public bool FastFlagsEnabled { get; set; } = false;

        [JsonPropertyName("fastFlagsCustom")]
        public Dictionary<string, string> FastFlagsCustom { get; set; } = new();

        [JsonPropertyName("fastFlagsEnabledPresets")]
        public List<string> FastFlagsEnabledPresets { get; set; } = new();
    }
}


using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Midnight.Models;
using Midnight.Services;

namespace Midnight.ViewModels
{
    public partial class FastFlagsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly FastFlagService _fastFlagService;
        private readonly MainViewModel _mainViewModel;

        // ── Custom flags shown in the DataGrid ─────────────────────────────
        public ObservableCollection<FastFlagEntry> CustomFlags { get; } = new();

        // ── Preset wrapper so each preset carries its own IsEnabled toggle ──
        public ObservableCollection<PresetViewModel> Presets { get; } = new();

        [ObservableProperty]
        private bool _fastFlagsEnabled;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        // New-flag input fields bound from the UI
        [ObservableProperty]
        private string _newFlagName = string.Empty;

        [ObservableProperty]
        private string _newFlagValue = string.Empty;

        // JSON import/export textarea
        [ObservableProperty]
        private string _jsonImportText = string.Empty;

        public FastFlagsViewModel(MainViewModel main, SettingsService settingsService, FastFlagService fastFlagService)
        {
            _mainViewModel = main;
            _settingsService = settingsService;
            _fastFlagService = fastFlagService;

            LoadFromSettings();
        }

        // ── Load ────────────────────────────────────────────────────────────

        private void LoadFromSettings()
        {
            var s = _settingsService.CurrentSettings;

            FastFlagsEnabled = s.FastFlagsEnabled;

            // Custom flags
            CustomFlags.Clear();
            foreach (var kv in s.FastFlagsCustom)
                CustomFlags.Add(new FastFlagEntry(kv.Key, kv.Value));

            // Presets
            Presets.Clear();
            foreach (var preset in FastFlagService.BuiltInPresets)
            {
                var pvm = new PresetViewModel(preset)
                {
                    IsEnabled = s.FastFlagsEnabledPresets.Contains(preset.Id)
                };
                // Persist when toggled
                pvm.IsEnabledChanged += () => SaveSettings();
                Presets.Add(pvm);
            }
        }

        // ── Commands ────────────────────────────────────────────────────────

        [RelayCommand]
        private void AddFlag()
        {
            string name = NewFlagName.Trim();
            string value = NewFlagValue.Trim();

            if (string.IsNullOrEmpty(name))
            {
                StatusMessage = "Flag name cannot be empty.";
                return;
            }

            // Update existing entry if the key already exists
            var existing = CustomFlags.FirstOrDefault(f => f.Name == name);
            if (existing != null)
            {
                existing.Value = value;
                StatusMessage = $"Updated \"{name}\".";
            }
            else
            {
                CustomFlags.Add(new FastFlagEntry(name, value));
                StatusMessage = $"Added \"{name}\".";
            }

            NewFlagName = string.Empty;
            NewFlagValue = string.Empty;
            SaveSettings();
        }

        [RelayCommand]
        private void RemoveFlag(FastFlagEntry? entry)
        {
            if (entry == null) return;
            CustomFlags.Remove(entry);
            StatusMessage = $"Removed \"{entry.Name}\".";
            SaveSettings();
        }

        [RelayCommand]
        private void ClearAllFlags()
        {
            CustomFlags.Clear();
            StatusMessage = "All custom flags cleared.";
            SaveSettings();
        }

        /// <summary>
        /// Parses the JSON in JsonImportText and merges every key/value pair into
        /// CustomFlags. Supports string, bool, number, and null values.
        /// Existing keys are overwritten; new keys are appended.
        /// </summary>
        [RelayCommand]
        private void ImportJson()
        {
            string raw = JsonImportText.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                StatusMessage = "Paste JSON into the box first.";
                return;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    StatusMessage = "JSON must be an object { \"FlagName\": value, … }";
                    return;
                }

                int added = 0, updated = 0;
                foreach (var prop in root.EnumerateObject())
                {
                    string name = prop.Name.Trim();
                    if (string.IsNullOrEmpty(name)) continue;

                    // Convert the JSON value to its string representation.
                    // Null becomes empty string; booleans are Title-cased to match
                    // what Roblox expects ("True" / "False").
                    string value = prop.Value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.Null    => string.Empty,
                        System.Text.Json.JsonValueKind.True    => "True",
                        System.Text.Json.JsonValueKind.False   => "False",
                        System.Text.Json.JsonValueKind.String  => prop.Value.GetString() ?? string.Empty,
                        _                                       => prop.Value.ToString()
                    };

                    var existing = CustomFlags.FirstOrDefault(f => f.Name == name);
                    if (existing != null)
                    {
                        existing.Value = value;
                        updated++;
                    }
                    else
                    {
                        CustomFlags.Add(new FastFlagEntry(name, value));
                        added++;
                    }
                }

                JsonImportText = string.Empty;
                SaveSettings();
                StatusMessage = $"Imported {added} new, updated {updated} existing flag(s).";
            }
            catch (System.Text.Json.JsonException ex)
            {
                StatusMessage = $"Invalid JSON: {ex.Message}";
            }
        }

        /// <summary>
        /// Serialises all current custom flags to pretty-printed JSON and puts it
        /// in JsonImportText so the user can copy it out.
        /// </summary>
        [RelayCommand]
        private void ExportJson()
        {
            if (CustomFlags.Count == 0)
            {
                StatusMessage = "No custom flags to export.";
                return;
            }

            var dict = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var entry in CustomFlags)
            {
                if (string.IsNullOrWhiteSpace(entry.Name)) continue;

                // Re-apply the same type coercion used when writing ClientAppSettings.json
                if (bool.TryParse(entry.Value, out bool b))
                    dict[entry.Name] = b;
                else if (long.TryParse(entry.Value, out long l))
                    dict[entry.Name] = l;
                else if (double.TryParse(entry.Value,
                             System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture,
                             out double d))
                    dict[entry.Name] = d;
                else if (string.IsNullOrEmpty(entry.Value))
                    dict[entry.Name] = (object?)null!;
                else
                    dict[entry.Name] = entry.Value;
            }

            JsonImportText = System.Text.Json.JsonSerializer.Serialize(
                dict,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            StatusMessage = $"Exported {dict.Count} flag(s) — copy from the box below.";
        }

        [RelayCommand]
        private async Task ApplyNow()
        {
            IsBusy = true;
            StatusMessage = "Applying fast flags…";
            try
            {
                SaveSettings();

                if (!FastFlagsEnabled)
                {
                    await _fastFlagService.ApplyFlagsAsync(new System.Collections.Generic.Dictionary<string, string>());
                    StatusMessage = "Fast flags disabled — ClientAppSettings.json cleared.";
                    return;
                }

                var enabledPresets = Presets
                    .Where(p => p.IsEnabled)
                    .Select(p => p.Preset.Id);

                var merged = FastFlagService.MergeFlags(CustomFlags, enabledPresets);
                await _fastFlagService.ApplyFlagsAsync(merged);

                int total = merged.Count;
                StatusMessage = $"Applied {total} flag{(total == 1 ? "" : "s")} successfully.";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void NavigateBack() => _mainViewModel.NavigateAccounts();

        /// <summary>Called from FastFlagsPage.xaml.cs CellEditEnding to persist inline edits.</summary>
        public void NotifyFlagsEdited() => SaveSettings();

        // ── Persistence ─────────────────────────────────────────────────────

        private void SaveSettings()
        {
            var s = _settingsService.CurrentSettings;

            s.FastFlagsEnabled = FastFlagsEnabled;

            s.FastFlagsCustom.Clear();
            foreach (var entry in CustomFlags)
                if (!string.IsNullOrWhiteSpace(entry.Name))
                    s.FastFlagsCustom[entry.Name] = entry.Value;

            s.FastFlagsEnabledPresets.Clear();
            foreach (var pvm in Presets.Where(p => p.IsEnabled))
                s.FastFlagsEnabledPresets.Add(pvm.Preset.Id);

            _settingsService.SaveSettings();

            // Auto-apply flags to ClientAppSettings.json immediately so changes take effect without waiting
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!FastFlagsEnabled)
                    {
                        await _fastFlagService.ApplyFlagsAsync(new System.Collections.Generic.Dictionary<string, string>());
                    }
                    else
                    {
                        var enabledPresets = Presets.Where(p => p.IsEnabled).Select(p => p.Preset.Id);
                        var merged = FastFlagService.MergeFlags(CustomFlags, enabledPresets);
                        await _fastFlagService.ApplyFlagsAsync(merged);
                    }
                }
                catch { }
            });
        }

        partial void OnFastFlagsEnabledChanged(bool value) => SaveSettings();
    }

    // ── Preset wrapper ───────────────────────────────────────────────────────

    public partial class PresetViewModel : ObservableObject
    {
        public FastFlagPreset Preset { get; }

        [ObservableProperty]
        private bool _isEnabled;

        public event System.Action? IsEnabledChanged;

        public PresetViewModel(FastFlagPreset preset)
        {
            Preset = preset;
        }

        partial void OnIsEnabledChanged(bool value) => IsEnabledChanged?.Invoke();
    }
}


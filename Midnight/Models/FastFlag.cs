using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Midnight.Models
{
    public class FastFlagEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        public FastFlagEntry() { }

        public FastFlagEntry(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    public class FastFlagPreset
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public Dictionary<string, string> Flags { get; set; } = new();
    }
}


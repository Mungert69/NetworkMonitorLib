using System.Text.Json.Serialization;

namespace NetworkMonitor.Objects
{
    public sealed class ToolBuilderSpec
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("systemPrompt")]
        public required string SystemPrompt { get; init; }

        [JsonPropertyName("functions")]
        public required string[] Functions { get; init; }   // ["run_nmap", ...]
    }
}

using System.Text.Json.Serialization;
using System.Collections.Generic;
using System;

namespace NetworkMonitor.Objects
{
    public sealed class ToolBuilderSpec
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("systemPrompt")]
        public string SystemPrompt { get; init; } = "";

        // Static built-in functions
        [JsonPropertyName("functions")]
        public string[] Functions { get; init; } = Array.Empty<string>();

        // Dynamic processor-backed functions
        [JsonPropertyName("cmdProcessorFunctions")]
        public List<CmdProcessorFunctionSpec> CmdProcessorFunctions { get; init; } = new();
    }

    public sealed class CmdProcessorFunctionSpec
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("parameters")]
        public List<CmdProcessorFunctionParameter> Parameters { get; init; } = new();
    }

    public sealed class CmdProcessorFunctionParameter
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}

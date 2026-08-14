// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>Selects entities by exact match on fields the SDK actually exposes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EntitySelector
{
    /// <summary>Exact entity names.</summary>
    [JsonPropertyName("names")]
    public IReadOnlyList<string>? Names { get; set; }

    /// <summary>Exact <c>properties.displayName</c>.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>Every listed tag must be present with the given value.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyDictionary<string, string>? Tags { get; set; }

    /// <summary>Exact <c>properties.discoveredBy</c>, so discovery-rule-owned entities can be included or excluded.</summary>
    [JsonPropertyName("discoveredBy")]
    public string? DiscoveredBy { get; set; }

    /// <summary>Select every entity in the model.</summary>
    [JsonPropertyName("all")]
    public bool? All { get; set; }
}

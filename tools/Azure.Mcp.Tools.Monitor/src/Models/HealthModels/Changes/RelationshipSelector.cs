// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>Selects relationships by exact match on fields the SDK actually exposes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RelationshipSelector
{
    /// <summary>Exact relationship names.</summary>
    [JsonPropertyName("names")]
    public IReadOnlyList<string>? Names { get; set; }

    /// <summary>Exact <c>properties.parentEntityName</c>.</summary>
    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    /// <summary>Exact <c>properties.childEntityName</c>.</summary>
    [JsonPropertyName("child")]
    public string? Child { get; set; }

    /// <summary>Exact <c>properties.discoveredBy</c>.</summary>
    [JsonPropertyName("discoveredBy")]
    public string? DiscoveredBy { get; set; }
}

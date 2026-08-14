// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>Selects signal definitions by exact match on fields the SDK actually exposes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SignalDefinitionSelector
{
    /// <summary>Exact signal definition names.</summary>
    [JsonPropertyName("names")]
    public IReadOnlyList<string>? Names { get; set; }

    /// <summary>Exact <c>properties.signalKind</c>, the polymorphic discriminator.</summary>
    [JsonPropertyName("signalKind")]
    public string? SignalKind { get; set; }

    /// <summary>Exact <c>properties.displayName</c>.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

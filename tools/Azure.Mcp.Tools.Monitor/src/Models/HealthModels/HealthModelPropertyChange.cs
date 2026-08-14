// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// One property-level difference between a target's current body and its desired body. Server-owned
/// properties are excluded, so a diff never shows churn the caller did not ask for.
/// </summary>
public sealed class HealthModelPropertyChange
{
    /// <summary>The dotted path from the resource body root, for example <c>properties.displayName</c>.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>The current value, or null when the property is being added.</summary>
    [JsonPropertyName("before")]
    public JsonNode? Before { get; set; }

    /// <summary>The desired value, or null when the property is being removed.</summary>
    [JsonPropertyName("after")]
    public JsonNode? After { get; set; }
}

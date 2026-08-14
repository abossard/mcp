// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// The closed resource-body union for a create: exactly one of <see cref="Entity"/>,
/// <see cref="Relationship"/> or <see cref="SignalDefinition"/>, each carrying the official PUT body
/// (<c>{"properties":{...}}</c>) verbatim.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HealthModelResourceBody
{
    [JsonPropertyName("entity")]
    public HealthModelResourceDocument? Entity { get; set; }

    [JsonPropertyName("relationship")]
    public HealthModelResourceDocument? Relationship { get; set; }

    [JsonPropertyName("signalDefinition")]
    public HealthModelResourceDocument? SignalDefinition { get; set; }
}

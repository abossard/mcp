// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// The closed target union: exactly one of <see cref="Entity"/>, <see cref="Relationship"/> or
/// <see cref="SignalDefinition"/>, which is what makes the resource kind unambiguous without multiplying
/// the operation union by three.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HealthModelSelector
{
    [JsonPropertyName("entity")]
    public EntitySelector? Entity { get; set; }

    [JsonPropertyName("relationship")]
    public RelationshipSelector? Relationship { get; set; }

    [JsonPropertyName("signalDefinition")]
    public SignalDefinitionSelector? SignalDefinition { get; set; }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.ResourceManager.CloudHealth;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// The MCP-owned envelope node for one item of a model-scope collection — a relationship or a signal
/// definition. It mirrors <see cref="HealthModelEntityResult"/>: the envelope carries only the correlation
/// key MCP owns (<see cref="Name"/>) plus exactly one strongly-typed
/// <c>Azure.ResourceManager.CloudHealth</c> resource payload, projected compactly by default and delegated
/// to the SDK's generated writer under the <c>full</c> field group. These items have no per-item failure
/// mode: a model-scope list either returns its page or fails the whole query.
/// </summary>
[JsonConverter(typeof(HealthModelCollectionItemResultConverter))]
public sealed class HealthModelCollectionItemResult
{
    /// <summary>The resource name of the item, which is also its handle in every other query.</summary>
    public string? Name { get; set; }

    /// <summary>Relationship payload for the relationship-list kind.</summary>
    public HealthModelRelationshipData? Relationship { get; set; }

    /// <summary>Signal-definition payload for the signal-definition-list kind.</summary>
    public HealthModelSignalDefinitionData? SignalDefinition { get; set; }

    internal HealthModelResultShape Shape { get; set; } = HealthModelResultShape.Compact;
}

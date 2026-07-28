// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// The universal MCP-owned envelope node for a single entity in a <see cref="HealthModelQueryResult"/>.
/// The envelope carries only the batching/correlation metadata MCP owns — <see cref="EntityName"/>
/// (all kinds), <see cref="Success"/>/<see cref="Error"/> (per-entity partial-failure isolation), and
/// <see cref="SignalName"/> (signal history only) — plus exactly one strongly-typed, single-page
/// <c>Azure.ResourceManager.CloudHealth</c> SDK response matching the query kind. Payloads use the
/// compact projection by default; the <c>full</c> field group delegates that response page to the SDK's
/// generated <see cref="System.ClientModel.Primitives.IJsonModel{T}.Write"/> pipeline with JSON format
/// <c>"J"</c>. API continuation remains visible separately through MCP-owned <see cref="Page"/> metadata,
/// and no SDK type enters the System.Text.Json source-generated graph.
/// </summary>
[JsonConverter(typeof(HealthModelEntityResultConverter))]
public sealed class HealthModelEntityResult
{
    /// <summary>The entity this node describes. Supplies the correlation key the SDK payload may lack.</summary>
    public string? EntityName { get; set; }

    /// <summary>False when this entity's per-entity read failed while sibling entities succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>The per-entity failure message when <see cref="Success"/> is false.</summary>
    public string? Error { get; set; }

    /// <summary>The signal correlation key; set only for signal-history nodes.</summary>
    public string? SignalName { get; set; }

    /// <summary>Entity payload for entity list / entity get kinds.</summary>
    public HealthModelEntityData? Entity { get; set; }

    /// <summary>One health-state history API page for the entity-history kind.</summary>
    public EntityHistoryResult? History { get; set; }

    /// <summary>One signal-history API page for the signal-history kind.</summary>
    public EntitySignalHistoryResult? SignalHistory { get; set; }

    /// <summary>Recommended signals/configurations payload for the signal-recommendations kind.</summary>
    public EntityGetSignalRecommendationsResult? Recommendations { get; set; }

    /// <summary>One data-annotations API page for the data-annotations kind.</summary>
    public EntityGetDataAnnotationsResult? Annotations { get; set; }

    /// <summary>Pagination metadata for successful history, signal-history, and annotation nodes.</summary>
    public HealthModelEntityPage? Page { get; set; }

    internal HealthModelResultShape Shape { get; set; } = HealthModelResultShape.Compact;
}

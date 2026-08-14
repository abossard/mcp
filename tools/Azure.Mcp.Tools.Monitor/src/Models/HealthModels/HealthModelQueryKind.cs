// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// The read operation a single <see cref="HealthModelQuery"/> requests against an Azure Monitor Health Model.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthModelQueryKind>))]
public enum HealthModelQueryKind
{
    /// <summary>List all entities of a health model (optionally as-of a point in time).</summary>
    EntityList,

    /// <summary>Get a single entity of a health model by name.</summary>
    EntityGet,

    /// <summary>Get the health-state history of an entity.</summary>
    EntityHistory,

    /// <summary>Get the signal history of an entity (requires <see cref="HealthModelQuery.SignalName"/>).</summary>
    SignalHistory,

    /// <summary>Get the recommended signals/configurations for an entity.</summary>
    SignalRecommendations,

    /// <summary>Get the data annotations of an entity.</summary>
    DataAnnotations,

    /// <summary>List the parent/child relationships of a health model (optionally as-of a point in time).</summary>
    RelationshipList,

    /// <summary>List the signal definitions of a health model (optionally as-of a point in time).</summary>
    SignalDefinitionList,
}

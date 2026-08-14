// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// Lists a health model's parent/child relationship edges. Joined with an entity's
/// <c>signalGroups.dependencies</c> aggregation rule, these edges are what make a rollup explainable.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RelationshipListQuery : HealthModelQuery
{
    /// <summary>Return the collection as it stood at this instant instead of now.</summary>
    [JsonPropertyName("asOf")]
    public DateTimeOffset? AsOf { get; set; }

    /// <summary>One page per request; echo the previous response's <c>page.cursor</c> to continue.</summary>
    [JsonPropertyName("page")]
    public HealthModelCursorPage? Page { get; set; }

    /// <summary>Selects the exact SDK payload instead of the compact projection.</summary>
    [JsonPropertyName("select")]
    public IReadOnlyList<FullSelection>? Select { get; set; }

    internal override HealthModelQueryKind QueryKind => HealthModelQueryKind.RelationshipList;
}

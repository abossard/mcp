// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>Lists a health model's entities, optionally as of a point in time.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EntityListQuery : HealthModelQuery
{
    /// <summary>Return the collection as it stood at this instant instead of now.</summary>
    [JsonPropertyName("asOf")]
    public DateTimeOffset? AsOf { get; set; }

    /// <summary>Keep only entities in this health state, filtered from the page already fetched.</summary>
    [JsonPropertyName("whereHealth")]
    public HealthModelHealthFilter? WhereHealth { get; set; }

    /// <summary>One page per request; echo the previous response's <c>page.cursor</c> to continue.</summary>
    [JsonPropertyName("page")]
    public HealthModelCursorPage? Page { get; set; }

    /// <summary>Field groups added to the compact entity payload.</summary>
    [JsonPropertyName("select")]
    public IReadOnlyList<EntitySelection>? Select { get; set; }

    internal override HealthModelQueryKind QueryKind => HealthModelQueryKind.EntityList;
}

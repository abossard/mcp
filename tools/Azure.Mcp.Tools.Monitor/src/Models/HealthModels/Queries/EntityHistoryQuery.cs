// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>Reads the health-state transition history of one or more entities.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EntityHistoryQuery : HealthModelQuery
{
    /// <summary>Which entities to read: one named entity, or every entity in a health state.</summary>
    [JsonPropertyName("target")]
    public HealthModelTarget? Target { get; set; }

    /// <summary>Inclusive time range. <c>from</c> must be within 30 days of the current time.</summary>
    [JsonPropertyName("window")]
    public HealthModelWindow? Window { get; set; }

    /// <summary>One page per request; echo the previous response's <c>page.cursor</c> to continue.</summary>
    [JsonPropertyName("page")]
    public HealthModelSizedPage? Page { get; set; }

    /// <summary>Selects the exact SDK payload instead of the compact projection.</summary>
    [JsonPropertyName("select")]
    public IReadOnlyList<FullSelection>? Select { get; set; }

    internal override HealthModelQueryKind QueryKind => HealthModelQueryKind.EntityHistory;
}

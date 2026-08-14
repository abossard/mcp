// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>Reads the recommended signals for one or more entities.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SignalRecommendationsQuery : HealthModelQuery
{
    /// <summary>Which entities to read: one named entity, or every entity in a health state.</summary>
    [JsonPropertyName("target")]
    public HealthModelTarget? Target { get; set; }

    /// <summary>Field groups added to the compact recommendations payload.</summary>
    [JsonPropertyName("select")]
    public IReadOnlyList<RecommendationSelection>? Select { get; set; }

    internal override HealthModelQueryKind QueryKind => HealthModelQueryKind.SignalRecommendations;
}

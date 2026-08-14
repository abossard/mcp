// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// Reads one signal's history for one or more entities. <c>signal</c> is scoped to the target entity: read
/// it from that entity's own signal groups with <c>select: ["signals"]</c> on an entity query. An
/// unrecognized name is not an error, it returns an empty history.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SignalHistoryQuery : HealthModelQuery
{
    /// <summary>The entity signal instance name, as reported by the target entity itself.</summary>
    [JsonPropertyName("signal")]
    public string? Signal { get; set; }

    /// <summary>Which entities to read: one named entity, or every entity in a health state.</summary>
    [JsonPropertyName("target")]
    public HealthModelTarget? Target { get; set; }

    /// <summary>Inclusive time range. <c>from</c> must be within 30 days of the current time.</summary>
    [JsonPropertyName("window")]
    public HealthModelWindow? Window { get; set; }

    /// <summary>One page per request; echo the previous response's <c>page.cursor</c> to continue.</summary>
    [JsonPropertyName("page")]
    public HealthModelSizedPage? Page { get; set; }

    /// <summary>Field groups added to the compact signal-history payload.</summary>
    [JsonPropertyName("select")]
    public IReadOnlyList<SignalHistorySelection>? Select { get; set; }

    internal override HealthModelQueryKind QueryKind => HealthModelQueryKind.SignalHistory;
}

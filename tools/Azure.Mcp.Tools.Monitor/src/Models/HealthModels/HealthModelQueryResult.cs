// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// The result of a single <see cref="HealthModelQuery"/>, correlated back to the caller by its
/// system-assigned <see cref="QueryIndex"/> (the query's zero-based input position). Results are returned
/// in input order.
///
/// <see cref="Success"/> is false only for whole-query failures — a planner validation/normalization
/// diagnostic, a failed shared entity-list gate, or a failed entity-list call. Once fan-out begins the
/// query is <see cref="Success"/> true and individual entity failures are isolated on their own
/// <see cref="HealthModelEntityResult"/> node, so one failing entity never erases its successful siblings.
/// </summary>
public sealed class HealthModelQueryResult
{
    /// <summary>The query's stable zero-based input position; results are returned in this order.</summary>
    [JsonPropertyName("queryIndex")]
    public int QueryIndex { get; set; }

    /// <summary>The optional caller-supplied label, echoed verbatim. Omitted from JSON when absent.</summary>
    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>
    /// The per-entity envelope nodes produced by this query. Every kind populates this uniform collection:
    /// entity list/get yield one node per entity carrying the entity payload; per-entity kinds yield one
    /// node per resolved entity carrying that kind's SDK payload (or a per-entity error).
    /// </summary>
    [JsonPropertyName("entities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HealthModelEntityResult>? Entities { get; set; }

    /// <summary>Entity-list or health-filter discovery page metadata.</summary>
    [JsonPropertyName("page")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HealthModelQueryPage? Page { get; set; }
}

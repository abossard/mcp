// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// A single typed health-model read request in a batch. Each query carries a <see cref="Kind"/> and the
/// scope/filters needed to fulfil it, plus an optional echo-only <see cref="Label"/>. Queries are correlated
/// to their results by their zero-based input position (assigned as <c>queryIndex</c> on the result), so no
/// caller-supplied id is required. Multiple queries are submitted together and planned into the minimum set
/// of Azure Resource Manager calls.
/// </summary>
public sealed class HealthModelQuery
{
    /// <summary>
    /// Optional caller-supplied label, echoed verbatim on the result and never used for planning, dedupe,
    /// routing, or slot assignment. Duplicate and absent labels are valid.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>The read operation to perform.</summary>
    [JsonPropertyName("kind")]
    public HealthModelQueryKind Kind { get; set; }

    /// <summary>The resource group that contains the health model.</summary>
    [JsonPropertyName("resourceGroup")]
    public string ResourceGroup { get; set; } = string.Empty;

    /// <summary>The health model name.</summary>
    [JsonPropertyName("healthModel")]
    public string HealthModel { get; set; } = string.Empty;

    /// <summary>
    /// The target entity name for per-entity kinds. Mutually exclusive with <see cref="HealthFilter"/>:
    /// supply an entity name to target one entity, or a health filter to target a resolved set of entities.
    /// </summary>
    [JsonPropertyName("entityName")]
    public string? EntityName { get; set; }

    /// <summary>Required for <see cref="HealthModelQueryKind.SignalHistory"/>.</summary>
    [JsonPropertyName("signalName")]
    public string? SignalName { get; set; }

    /// <summary>Inclusive start of the history window (history/annotation kinds).</summary>
    [JsonPropertyName("startTime")]
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>Inclusive end of the history window (history/annotation kinds).</summary>
    [JsonPropertyName("endTime")]
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>Maximum number of items to return per page (history/annotation kinds).</summary>
    [JsonPropertyName("top")]
    public int? Top { get; set; }

    /// <summary>
    /// Point-in-time for <see cref="HealthModelQueryKind.EntityList"/>. When set, the entity set is
    /// returned as-of this instant using a single point-in-time list call.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// When set on a per-entity kind without an <see cref="EntityName"/>, the query runs against every
    /// entity whose health state matches the filter, resolved from a single shared entity list.
    /// </summary>
    [JsonPropertyName("healthFilter")]
    public HealthModelHealthFilter? HealthFilter { get; set; }

    /// <summary>Optional closed field groups added to the compact payload for this query kind.</summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<HealthModelFieldGroup>? Fields { get; set; }

    /// <summary>
    /// Opaque CloudHealth marker for resuming one concrete entity history, signal-history, or annotation page.
    /// </summary>
    [JsonPropertyName("nextMarker")]
    public string? NextMarker { get; set; }

    /// <summary>
    /// Opaque entity-list continuation for resuming an entity-list query or health-filter discovery page.
    /// </summary>
    [JsonPropertyName("continuationToken")]
    public string? ContinuationToken { get; set; }
}

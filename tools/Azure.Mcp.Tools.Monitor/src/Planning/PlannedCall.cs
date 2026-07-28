// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// One immutable Azure Resource Manager call the planner decided to emit. A single call can satisfy
/// multiple input queries (see <see cref="QueryIndexes"/>) when they deduplicate to the same call.
/// </summary>
internal sealed record PlannedCall(
    HealthModelCallKind Kind,
    PlanScope Scope,
    string? EntityName,
    string? SignalName,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    int? Top,
    DateTimeOffset? Timestamp,
    HealthModelHealthFilter? HealthFilter,
    string? NextMarker,
    string? ContinuationToken,
    IReadOnlyList<int> QueryIndexes)
{
    /// <summary>
    /// True when this is a per-entity call whose target entity is not fixed but resolved at execution
    /// time from the shared entity list (filtered by <see cref="HealthFilter"/>).
    /// </summary>
    internal bool IsDeferredPerEntity => EntityName is null && Kind != HealthModelCallKind.ListEntities;
}

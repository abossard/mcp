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
    DateTimeOffset? From,
    DateTimeOffset? To,
    int? Size,
    DateTimeOffset? AsOf,
    HealthModelHealthFilter? HealthFilter,
    string? Cursor,
    IReadOnlyList<int> QueryIndexes)
{
    /// <summary>
    /// True when this is a per-entity call whose target entity is not fixed but resolved at execution
    /// time from the shared entity list (filtered by <see cref="HealthFilter"/>). Model-scope list calls
    /// have no entity target at all and are never deferred.
    /// </summary>
    /// <summary>
    /// The cursor that resumes this one entity's own page. A deferred call's cursor belongs to the shared
    /// discovery list instead, so it must not be forwarded to the per-entity request.
    /// </summary>
    internal string? EntityCursor => IsDeferredPerEntity ? null : Cursor;

    internal bool IsDeferredPerEntity =>
        EntityName is null &&
        Kind != HealthModelCallKind.ListEntities &&
        !HealthModelCallKinds.IsModelScopeList(Kind);
}

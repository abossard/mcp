// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// The immutable output of <see cref="HealthModelQueryPlanner"/>: the grouped, deduplicated, ordered
/// calls to execute, plus diagnostics for queries that could not be planned. <see cref="QueryCount"/> is
/// the number of input queries, so the executor can pre-size an input-ordered result slot array.
/// <see cref="ListFilters"/> is indexed by query position and lets several entity-list queries that differ
/// only by health filter share one list call while each receives its own filtered entities.
/// </summary>
internal sealed record ExecutionPlan(
    IReadOnlyList<PlanGroup> Groups,
    IReadOnlyList<PlanDiagnostic> Diagnostics,
    int QueryCount,
    IReadOnlyList<HealthModelResultShape> ResultShapes,
    IReadOnlyList<HealthModelHealthFilter?> ListFilters);

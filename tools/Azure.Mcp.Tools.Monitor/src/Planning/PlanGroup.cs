// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// The ordered set of calls for a single <see cref="PlanScope"/>. Calls are ordered so any entity
/// list runs before the per-entity calls that depend on it.
/// </summary>
internal sealed record PlanGroup(PlanScope Scope, IReadOnlyList<PlannedCall> Calls);

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// A query the planner could not turn into a call (e.g. missing required fields). The executor turns
/// each diagnostic into a whole-query error result so every input query still yields exactly one result,
/// correlated by its zero-based <see cref="QueryIndex"/>.
/// </summary>
internal sealed record PlanDiagnostic(int QueryIndex, string Kind, string Error);

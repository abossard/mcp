// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// A query that could not be read into its concrete type, carrying the reason. It occupies its input slot
/// so a malformed query fails on its own result node instead of rejecting the whole batch, which is the
/// same per-boundary isolation the executor already applies to a failing entity.
/// </summary>
public sealed class MalformedQuery : HealthModelQuery
{
    /// <summary>Why this element could not be read, phrased for the caller that sent it.</summary>
    public string Error { get; set; } = string.Empty;

    internal override HealthModelQueryKind QueryKind =>
        throw new InvalidOperationException("A malformed query is never planned.");
}

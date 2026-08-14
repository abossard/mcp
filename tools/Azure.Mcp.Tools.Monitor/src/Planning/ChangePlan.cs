// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>The whole change set, resolved but not yet executed.</summary>
internal sealed record ChangePlan(IReadOnlyList<PlannedChange> Changes)
{
    /// <summary>
    /// How many targets the plan would actually touch. A caller echoes this back as
    /// <c>expect.affectedCount</c> to authorise an apply, so it counts targets rather than calls: it is the
    /// number a human reads off the what-if.
    /// </summary>
    internal int AffectedCount => Changes
        .Where(change => change.Success)
        .SelectMany(change => change.Targets)
        .Count(target => target.Action != HealthModelChangeAction.NoOp);
}

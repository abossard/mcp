// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// One resource the change set touches. A target is not one call: a <c>replace</c> carries a delete and a
/// put, because the endpoints of a relationship are fixed at create time.
/// </summary>
/// <param name="Restore">
/// The body to put back if the writes fail partway. A <c>replace</c> deletes before it creates, so a
/// failure between the two would otherwise leave nothing where a live resource used to be; every other
/// action either writes once or removes on purpose, and carries no restore. This is the body the batch
/// started from, not the state earlier elements planned to leave behind: only the pre-batch body is known
/// to have existed on the service.
/// </param>
internal sealed record PlannedTarget(
    PlanScope Scope,
    HealthModelResourceKind Kind,
    string Name,
    HealthModelChangeAction Action,
    IReadOnlyList<HealthModelPropertyChange> Changes,
    IReadOnlyList<PlannedWrite> Writes,
    IReadOnlyList<PlannedDependency> DependsOn,
    JsonObject? Restore = null)
{
    /// <summary>Writes run in two passes so a delete never strands a resource that still points at it.</summary>
    internal int Phase => Action == HealthModelChangeAction.Delete ? 1 : 0;

    internal int KindRank => Phase == 0
        ? Kind switch
        {
            HealthModelResourceKind.SignalDefinition => 0,
            HealthModelResourceKind.Entity => 1,
            _ => 2,
        }
        : Kind switch
        {
            HealthModelResourceKind.Relationship => 0,
            HealthModelResourceKind.Entity => 1,
            _ => 2,
        };
}

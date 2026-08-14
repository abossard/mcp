// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Every child resource of one health model, read to exhaustion. A selector that chose targets from a
/// partial page would silently edit a different set than the caller asked for, so the graph edit reads all
/// pages rather than the single page the query command returns by design.
/// </summary>
internal sealed record HealthModelGraphSnapshot(
    PlanScope Scope,
    IReadOnlyList<HealthModelResourceSnapshot> Entities,
    IReadOnlyList<HealthModelResourceSnapshot> Relationships,
    IReadOnlyList<HealthModelResourceSnapshot> SignalDefinitions)
{
    internal HealthModelScanCounts Counts =>
        new(Entities.Count, Relationships.Count, SignalDefinitions.Count);

    internal IReadOnlyList<HealthModelResourceSnapshot> Collection(HealthModelResourceKind kind) => kind switch
    {
        HealthModelResourceKind.Entity => Entities,
        HealthModelResourceKind.Relationship => Relationships,
        HealthModelResourceKind.SignalDefinition => SignalDefinitions,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown health-model resource kind."),
    };

    internal HealthModelResourceSnapshot? Find(HealthModelResourceKind kind, string name) =>
        Collection(kind).FirstOrDefault(resource => string.Equals(resource.Name, name, StringComparison.Ordinal));
}

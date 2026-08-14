// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// A resource in the same scope that has to exist before a target can be written: the entities a
/// relationship's endpoints name, and the signal definitions an entity's signals reference.
/// </summary>
internal sealed record PlannedDependency(HealthModelResourceKind Kind, string Name)
{
    internal string Description => Kind switch
    {
        HealthModelResourceKind.Entity => $"entity '{Name}'",
        HealthModelResourceKind.SignalDefinition => $"signal definition '{Name}'",
        _ => $"relationship '{Name}'",
    };
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// The write side of the health-model surface, kept separate from <see cref="IHealthModelCallRunner"/> so
/// the read-only query path cannot acquire a write method by accident.
/// </summary>
internal interface IHealthModelWriteRunner
{
    /// <summary>Reads one page of a child collection; paging is driven by the caller so it can prove exhaustion.</summary>
    Task<HealthModelResourcePage> ListAsync(
        PlanScope scope, HealthModelResourceKind kind, string? continuationToken, CancellationToken cancellationToken);

    /// <summary>Upserts a resource from its wire body. The service offers no partial update.</summary>
    Task PutAsync(
        PlanScope scope, HealthModelResourceKind kind, string name, JsonObject body, CancellationToken cancellationToken);

    Task DeleteAsync(
        PlanScope scope, HealthModelResourceKind kind, string name, CancellationToken cancellationToken);
}

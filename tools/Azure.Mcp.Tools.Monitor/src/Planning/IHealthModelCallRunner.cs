// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Abstraction over the Azure Resource Manager calls a <see cref="PlannedCall"/> can require. The
/// production implementation lives in the service layer and performs the actual SDK I/O; the pure
/// <see cref="HealthModelQueryExecutor"/> depends only on this interface so its orchestration
/// (dependency gating, health filtering, per-entity error isolation) is testable without ARM.
/// Each paginated method returns exactly one <c>Azure.ResourceManager.CloudHealth</c> SDK response page
/// with its opaque API continuation untouched. The executor wraps that page in an MCP envelope node,
/// applies compact-default projection (or full SDK <c>IJsonModel&lt;T&gt;.Write("J")</c> serialization),
/// supplies correlation keys the SDK response may lack, and exposes continuation through MCP page metadata.
/// </summary>
internal interface IHealthModelCallRunner
{
    Task<HealthModelEntityListPage> ListEntitiesAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken);

    Task<HealthModelListPage<HealthModelRelationshipData>> ListRelationshipsAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken);

    Task<HealthModelListPage<HealthModelSignalDefinitionData>> ListSignalDefinitionsAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken);

    Task<HealthModelEntityData> GetEntityAsync(
        PlanScope scope, string entityName, CancellationToken cancellationToken);

    Task<EntityHistoryResult> GetHistoryAsync(
        PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
        string? nextMarker, CancellationToken cancellationToken);

    Task<EntitySignalHistoryResult> GetSignalHistoryAsync(
        PlanScope scope, string entityName, string signalName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
        string? nextMarker, CancellationToken cancellationToken);

    Task<EntityGetSignalRecommendationsResult> GetSignalRecommendationsAsync(
        PlanScope scope, string entityName, CancellationToken cancellationToken);

    Task<EntityGetDataAnnotationsResult> GetDataAnnotationsAsync(
        PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
        string? nextMarker, CancellationToken cancellationToken);
}

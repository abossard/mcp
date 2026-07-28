// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Planning;
using Azure.ResourceManager;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;
using Azure.ResourceManager.Resources;

namespace Azure.Mcp.Tools.Monitor.Services;

/// <summary>
/// ARM-backed <see cref="IHealthModelCallRunner"/>. This is the only I/O layer for the health-model query
/// facility: it resolves each health model once per scope (shared across a group's calls), issues the
/// CloudHealth SDK requests a <see cref="PlannedCall"/> maps to, and returns exactly one SDK page per call.
/// </summary>
internal sealed class ArmHealthModelCallRunner(SubscriptionResource subscription, ArmClient armClient) : IHealthModelCallRunner
{
    private readonly string _subscriptionId = subscription.Id.SubscriptionId!;
    private readonly Dictionary<PlanScope, HealthModelResource> _modelCache = [];

    public async Task<HealthModelEntityListPage> ListEntitiesAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(scope, cancellationToken);
        var page = await HealthModelPaginator.ReadPageAsync(
            model.GetHealthModelEntities().GetAllAsync(timestamp, cancellationToken),
            continuationToken,
            cancellationToken);
        return new HealthModelEntityListPage(
            page.Items.Select(entity => entity.Data).ToList(),
            page.ContinuationToken);
    }

    public async Task<HealthModelEntityData> GetEntityAsync(
        PlanScope scope, string entityName, CancellationToken cancellationToken)
    {
        var entity = await Entity(scope, entityName).GetAsync(cancellationToken);
        return entity.Value.Data;
    }

    public async Task<EntityHistoryResult> GetHistoryAsync(
        PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
        string? nextMarker, CancellationToken cancellationToken)
    {
        var content = HealthModelRequestContent.History(startTime, endTime, top, nextMarker);
        var result = await Entity(scope, entityName).GetHistoryAsync(content, cancellationToken);
        return result.Value;
    }

    public async Task<EntitySignalHistoryResult> GetSignalHistoryAsync(
        PlanScope scope, string entityName, string signalName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
        string? nextMarker, CancellationToken cancellationToken)
    {
        var content = HealthModelRequestContent.SignalHistory(signalName, startTime, endTime, top, nextMarker);
        var result = await Entity(scope, entityName).GetSignalHistoryAsync(content, cancellationToken);
        return result.Value;
    }

    public async Task<EntityGetSignalRecommendationsResult> GetSignalRecommendationsAsync(
        PlanScope scope, string entityName, CancellationToken cancellationToken)
    {
        var result = await Entity(scope, entityName).GetSignalRecommendationsAsync(cancellationToken);
        return result.Value;
    }

    public async Task<EntityGetDataAnnotationsResult> GetDataAnnotationsAsync(
        PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
        string? nextMarker, CancellationToken cancellationToken)
    {
        var content = HealthModelRequestContent.DataAnnotations(startTime, endTime, top, nextMarker);
        var result = await Entity(scope, entityName).GetDataAnnotationsAsync(content, cancellationToken);
        return result.Value;
    }

    private async Task<HealthModelResource> ResolveModelAsync(PlanScope scope, CancellationToken cancellationToken)
    {
        if (_modelCache.TryGetValue(scope, out var cached))
        {
            return cached;
        }

        var resourceGroup = await subscription.GetResourceGroupAsync(scope.ResourceGroup, cancellationToken);
        var model = await resourceGroup.Value.GetHealthModels().GetAsync(scope.HealthModel, cancellationToken);
        _modelCache[scope] = model.Value;
        return model.Value;
    }

    private HealthModelEntityResource Entity(PlanScope scope, string entityName)
    {
        var id = HealthModelEntityResource.CreateResourceIdentifier(_subscriptionId, scope.ResourceGroup, scope.HealthModel, entityName);
        return armClient.GetHealthModelEntityResource(id);
    }
}

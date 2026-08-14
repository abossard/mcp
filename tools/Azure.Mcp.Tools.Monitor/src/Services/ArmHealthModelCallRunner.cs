// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.Mcp.Tools.Monitor.Sandbox;
using Azure.ResourceManager;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;
using Azure.ResourceManager.Resources;

namespace Azure.Mcp.Tools.Monitor.Services;

/// <summary>
/// ARM-backed <see cref="IHealthModelCallRunner"/>. This is the only I/O layer for the health-model query
/// facility: it resolves each health model once per scope (shared across a group's calls), issues the
/// CloudHealth SDK requests a <see cref="PlannedCall"/> maps to, and returns exactly one SDK page per call.
/// It also serves the write side (<see cref="IHealthModelWriteRunner"/>), reusing that same model cache.
/// </summary>
internal sealed class ArmHealthModelCallRunner(SubscriptionResource subscription, ArmClient armClient)
    : IHealthModelCallRunner, IHealthModelWriteRunner, IHealthModelSdkRunner
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

    public async Task<HealthModelListPage<HealthModelRelationshipData>> ListRelationshipsAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(scope, cancellationToken);
        var page = await HealthModelPaginator.ReadPageAsync(
            model.GetHealthModelRelationships().GetAllAsync(timestamp, cancellationToken),
            continuationToken,
            cancellationToken);
        return new HealthModelListPage<HealthModelRelationshipData>(
            page.Items.Select(relationship => relationship.Data).ToList(),
            page.ContinuationToken);
    }

    public async Task<HealthModelListPage<HealthModelSignalDefinitionData>> ListSignalDefinitionsAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(scope, cancellationToken);
        var page = await HealthModelPaginator.ReadPageAsync(
            model.GetHealthModelSignalDefinitions().GetAllAsync(timestamp, cancellationToken),
            continuationToken,
            cancellationToken);
        return new HealthModelListPage<HealthModelSignalDefinitionData>(
            page.Items.Select(definition => definition.Data).ToList(),
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

    public async Task<HealthModelResourcePage> ListAsync(
        PlanScope scope, HealthModelResourceKind kind, string? continuationToken, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(scope, cancellationToken);
        return kind switch
        {
            HealthModelResourceKind.Entity => await ReadPageAsync(
                kind, model.GetHealthModelEntities().GetAllAsync(null, cancellationToken),
                entity => (entity.Data.Name, Wire(entity.Data)), continuationToken, cancellationToken),
            HealthModelResourceKind.Relationship => await ReadPageAsync(
                kind, model.GetHealthModelRelationships().GetAllAsync(null, cancellationToken),
                relationship => (relationship.Data.Name, Wire(relationship.Data)), continuationToken, cancellationToken),
            _ => await ReadPageAsync(
                kind, model.GetHealthModelSignalDefinitions().GetAllAsync(null, cancellationToken),
                definition => (definition.Data.Name, Wire(definition.Data)), continuationToken, cancellationToken),
        };
    }

    public async Task PutAsync(
        PlanScope scope, HealthModelResourceKind kind, string name, JsonObject body, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(scope, cancellationToken);
        var payload = BinaryData.FromString(body.ToJsonString());

        switch (kind)
        {
            case HealthModelResourceKind.Entity:
                await model.GetHealthModelEntities().CreateOrUpdateAsync(
                    WaitUntil.Completed, name, Read<HealthModelEntityData>(payload), cancellationToken);
                break;
            case HealthModelResourceKind.Relationship:
                await model.GetHealthModelRelationships().CreateOrUpdateAsync(
                    WaitUntil.Completed, name, Read<HealthModelRelationshipData>(payload), cancellationToken);
                break;
            default:
                await model.GetHealthModelSignalDefinitions().CreateOrUpdateAsync(
                    WaitUntil.Completed, name, Read<HealthModelSignalDefinitionData>(payload), cancellationToken);
                break;
        }
    }

    public async Task DeleteAsync(
        PlanScope scope, HealthModelResourceKind kind, string name, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case HealthModelResourceKind.Entity:
                await Entity(scope, name).DeleteAsync(WaitUntil.Completed, cancellationToken);
                break;
            case HealthModelResourceKind.Relationship:
                await armClient.GetHealthModelRelationshipResource(
                    HealthModelRelationshipResource.CreateResourceIdentifier(
                        _subscriptionId, scope.ResourceGroup, scope.HealthModel, name))
                    .DeleteAsync(WaitUntil.Completed, cancellationToken);
                break;
            default:
                await armClient.GetHealthModelSignalDefinitionResource(
                    HealthModelSignalDefinitionResource.CreateResourceIdentifier(
                        _subscriptionId, scope.ResourceGroup, scope.HealthModel, name))
                    .DeleteAsync(WaitUntil.Completed, cancellationToken);
                break;
        }
    }

    public async Task<HealthModelListPage<HealthModelData>> ListHealthModelsAsync(
        string? resourceGroup, string? continuationToken, CancellationToken cancellationToken)
    {
        var pageable = string.IsNullOrEmpty(resourceGroup)
            ? subscription.GetHealthModelsAsync(cancellationToken)
            : (await subscription.GetResourceGroupAsync(resourceGroup, cancellationToken))
                .Value.GetHealthModels().GetAllAsync(cancellationToken: cancellationToken);

        var page = await HealthModelPaginator.ReadPageAsync(pageable, continuationToken, cancellationToken);
        return new HealthModelListPage<HealthModelData>(
            page.Items.Select(model => model.Data).ToList(),
            page.ContinuationToken);
    }

    public async Task<HealthModelData> GetHealthModelAsync(
        string resourceGroup, string healthModelName, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(new PlanScope(resourceGroup, healthModelName), cancellationToken);
        return model.Data;
    }

    public async Task<JsonNode> AddDataAnnotationAsync(
        PlanScope scope, string entityName, JsonObject body, CancellationToken cancellationToken)
    {
        var content = Read<EntityAddDataAnnotationContent>(BinaryData.FromString(body.ToJsonString()));
        var result = await Entity(scope, entityName).AddDataAnnotationAsync(content, cancellationToken);
        return Wire(result.Value);
    }

    public async Task IngestHealthReportAsync(
        PlanScope scope, string entityName, JsonObject body, CancellationToken cancellationToken)
    {
        var content = Read<EntityHealthReportContent>(BinaryData.FromString(body.ToJsonString()));
        await Entity(scope, entityName).IngestHealthReportAsync(content, cancellationToken);
    }

    private static async Task<HealthModelResourcePage> ReadPageAsync<TResource>(
        HealthModelResourceKind kind,
        AsyncPageable<TResource> pageable,
        Func<TResource, (string Name, JsonObject Body)> project,
        string? continuationToken,
        CancellationToken cancellationToken)
        where TResource : notnull
    {
        var page = await HealthModelPaginator.ReadPageAsync(pageable, continuationToken, cancellationToken);
        HealthModelPaginator.EnsureMarkerAdvanced(continuationToken, page.ContinuationToken);

        var items = page.Items
            .Select(project)
            .Select(projected => new HealthModelResourceSnapshot(kind, projected.Name, projected.Body))
            .ToList();

        return new HealthModelResourcePage(items, page.ContinuationToken);
    }

    /// <summary>
    /// Projects an SDK model to the exact JSON the service round-trips, which is what the planner patches:
    /// the typed graph cannot carry a signal-definition property this build has never heard of.
    /// </summary>
    private static JsonObject Wire<T>(T data) where T : IPersistableModel<T> =>
        HealthModelWireCodec.Wire(data);

    private static T Read<T>(BinaryData payload) => HealthModelWireCodec.Read<T>(payload);

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

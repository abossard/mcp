// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
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
internal sealed class ArmHealthModelCallRunner(
    SubscriptionResource subscription,
    ArmClient armClient,
    string armEndpoint,
    Func<CancellationToken, Task<JsonNode>>? listOperationsAsync = null)
    : IHealthModelCallRunner, IHealthModelWriteRunner, IHealthModelSdkRunner, IHealthModelReadCodeRunner
{
    private readonly string _subscriptionId = subscription.Id.SubscriptionId!;
    private readonly Func<CancellationToken, Task<JsonNode>>? _listOperationsAsync = listOperationsAsync;
    private readonly ConcurrentDictionary<PlanScope, Lazy<Task<HealthModelResource>>> _modelCache = [];
    private readonly string _armEndpoint = armEndpoint.TrimEnd('/');

    public string SubscriptionId => _subscriptionId;
    public string ArmEndpoint => _armEndpoint;

    public async Task<HealthModelEntityListPage> ListEntitiesAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(scope, cancellationToken);
        var page = await HealthModelPaginator.ReadPageAsync(
            model.GetHealthModelEntities().GetAllAsync(timestamp, cancellationToken),
            continuationToken,
            cancellationToken);
        HealthModelPaginator.EnsureMarkerAdvanced(continuationToken, page.ContinuationToken);
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
        HealthModelPaginator.EnsureMarkerAdvanced(continuationToken, page.ContinuationToken);
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
        HealthModelPaginator.EnsureMarkerAdvanced(continuationToken, page.ContinuationToken);
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
        HealthModelPaginator.EnsureMarkerAdvanced(continuationToken, page.ContinuationToken);
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

    public async Task<HealthModelData> GetHealthModelReadAsync(
        string resourceGroup, string healthModelName, CancellationToken cancellationToken)
    {
        var id = HealthModelResource.CreateResourceIdentifier(_subscriptionId, resourceGroup, healthModelName);
        var model = await armClient.GetHealthModelResource(id).GetAsync(cancellationToken);
        return model.Value.Data;
    }

    public async Task<HealthModelRelationshipData> GetRelationshipAsync(
        PlanScope scope, string relationshipName, CancellationToken cancellationToken)
    {
        var relationship = await armClient.GetHealthModelRelationshipResource(
            HealthModelRelationshipResource.CreateResourceIdentifier(
                _subscriptionId, scope.ResourceGroup, scope.HealthModel, relationshipName))
            .GetAsync(cancellationToken);
        return relationship.Value.Data;
    }

    public async Task<HealthModelSignalDefinitionData> GetSignalDefinitionAsync(
        PlanScope scope, string signalDefinitionName, CancellationToken cancellationToken)
    {
        var definition = await armClient.GetHealthModelSignalDefinitionResource(
            HealthModelSignalDefinitionResource.CreateResourceIdentifier(
                _subscriptionId, scope.ResourceGroup, scope.HealthModel, signalDefinitionName))
            .GetAsync(cancellationToken);
        return definition.Value.Data;
    }

    public async Task<HealthModelAuthenticationSettingData> GetAuthenticationSettingAsync(
        PlanScope scope, string authenticationSettingName, CancellationToken cancellationToken)
    {
        var setting = await armClient.GetHealthModelAuthenticationSettingResource(
            HealthModelAuthenticationSettingResource.CreateResourceIdentifier(
                _subscriptionId, scope.ResourceGroup, scope.HealthModel, authenticationSettingName))
            .GetAsync(cancellationToken);
        return setting.Value.Data;
    }

    public async Task<HealthModelListPage<HealthModelAuthenticationSettingData>> ListAuthenticationSettingsAsync(
        PlanScope scope, string? continuationToken, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(scope, cancellationToken);
        var page = await HealthModelPaginator.ReadPageAsync(
            model.GetHealthModelAuthenticationSettings().GetAllAsync(cancellationToken),
            continuationToken,
            cancellationToken);
        HealthModelPaginator.EnsureMarkerAdvanced(continuationToken, page.ContinuationToken);
        return new HealthModelListPage<HealthModelAuthenticationSettingData>(
            page.Items.Select(setting => setting.Data).ToList(),
            page.ContinuationToken);
    }

    public async Task<HealthModelDiscoveryRuleData> GetDiscoveryRuleAsync(
        PlanScope scope, string discoveryRuleName, CancellationToken cancellationToken)
    {
        var rule = await armClient.GetHealthModelDiscoveryRuleResource(
            HealthModelDiscoveryRuleResource.CreateResourceIdentifier(
                _subscriptionId, scope.ResourceGroup, scope.HealthModel, discoveryRuleName))
            .GetAsync(cancellationToken);
        return rule.Value.Data;
    }

    public async Task<HealthModelListPage<HealthModelDiscoveryRuleData>> ListDiscoveryRulesAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(scope, cancellationToken);
        var page = await HealthModelPaginator.ReadPageAsync(
            model.GetHealthModelDiscoveryRules().GetAllAsync(timestamp, cancellationToken),
            continuationToken,
            cancellationToken);
        HealthModelPaginator.EnsureMarkerAdvanced(continuationToken, page.ContinuationToken);
        return new HealthModelListPage<HealthModelDiscoveryRuleData>(
            page.Items.Select(rule => rule.Data).ToList(),
            page.ContinuationToken);
    }

    public Task<JsonNode> ListOperationsAsync(CancellationToken cancellationToken) =>
        _listOperationsAsync is not null
            ? _listOperationsAsync(cancellationToken)
            : throw new InvalidOperationException("Operations reader is not configured.");

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
        while (true)
        {
            var resolution = _modelCache.GetOrAdd(
                scope,
                currentScope => new Lazy<Task<HealthModelResource>>(
                    () => ResolveModelCoreAsync(currentScope, cancellationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await resolution.Value.ConfigureAwait(false);
            }
            catch
            {
                _modelCache.TryRemove(new KeyValuePair<PlanScope, Lazy<Task<HealthModelResource>>>(scope, resolution));
                throw;
            }
        }
    }

    private async Task<HealthModelResource> ResolveModelCoreAsync(PlanScope scope, CancellationToken cancellationToken)
    {
        var resourceGroup = await subscription.GetResourceGroupAsync(scope.ResourceGroup, cancellationToken);
        var model = await resourceGroup.Value.GetHealthModels().GetAsync(scope.HealthModel, cancellationToken);
        return model.Value;
    }

    private HealthModelEntityResource Entity(PlanScope scope, string entityName)
    {
        var id = HealthModelEntityResource.CreateResourceIdentifier(_subscriptionId, scope.ResourceGroup, scope.HealthModel, entityName);
        return armClient.GetHealthModelEntityResource(id);
    }
}

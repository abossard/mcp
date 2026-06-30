// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Mcp.Core.Services.Azure;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Core.Services.Azure.Tenant;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;
using Azure.ResourceManager.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.HealthModels.Services;

public sealed class HealthModelsService(ISubscriptionService subscriptionService, ITenantService tenantService)
    : BaseAzureService(tenantService), IHealthModelsService
{
    private readonly ISubscriptionService _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));

    private static readonly ModelReaderWriterContext s_context = AzureResourceManagerCloudHealthContext.Default;
    private static readonly ModelReaderWriterOptions s_jsonOptions = ModelReaderWriterOptions.Json;

    private static JsonNode ToJson(object model) =>
        JsonNode.Parse(ModelReaderWriter.Write(model, s_jsonOptions, s_context).ToString())
            ?? throw new InvalidOperationException("Failed to serialize the CloudHealth response.");

    private static T ReadFromProperties<T>(string propertiesJson)
    {
        var body = $"{{\"properties\":{propertiesJson}}}";
        return ModelReaderWriter.Read<T>(BinaryData.FromString(body), s_jsonOptions, s_context)
            ?? throw new InvalidOperationException($"Failed to parse the supplied properties for {typeof(T).Name}.");
    }

    private async Task<SubscriptionResource> GetSubscriptionAsync(string subscription, string? tenant, RetryPolicyOptions? retryPolicy, CancellationToken cancellationToken) =>
        await _subscriptionService.GetSubscription(subscription, tenant, retryPolicy, cancellationToken);

    private async Task<HealthModelCollection> GetHealthModelCollectionAsync(string subscription, string resourceGroup, string? tenant, RetryPolicyOptions? retryPolicy, CancellationToken cancellationToken)
    {
        var subscriptionResource = await GetSubscriptionAsync(subscription, tenant, retryPolicy, cancellationToken);
        var resourceGroupResource = await subscriptionResource.GetResourceGroupAsync(resourceGroup, cancellationToken);
        return resourceGroupResource.Value.GetHealthModels();
    }

    private async Task<HealthModelResource> GetHealthModelResourceAsync(string subscription, string resourceGroup, string healthModelName, string? tenant, RetryPolicyOptions? retryPolicy, CancellationToken cancellationToken)
    {
        var collection = await GetHealthModelCollectionAsync(subscription, resourceGroup, tenant, retryPolicy, cancellationToken);
        var response = await collection.GetAsync(healthModelName, cancellationToken);
        return response.Value;
    }

    private async Task<HealthModelEntityResource> GetEntityResourceAsync(string subscription, string resourceGroup, string healthModelName, string entityName, string? tenant, RetryPolicyOptions? retryPolicy, CancellationToken cancellationToken)
    {
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelEntityAsync(entityName, cancellationToken);
        return response.Value;
    }

    // Health model (root)

    public async Task<List<JsonNode>> ListHealthModelsAsync(string subscription, string? resourceGroup = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription));

        var results = new List<JsonNode>();
        if (string.IsNullOrEmpty(resourceGroup))
        {
            var subscriptionResource = await GetSubscriptionAsync(subscription, tenant, retryPolicy, cancellationToken);
            await foreach (var model in subscriptionResource.GetHealthModelsAsync(cancellationToken))
            {
                results.Add(ToJson(model.Data));
            }
        }
        else
        {
            var collection = await GetHealthModelCollectionAsync(subscription, resourceGroup, tenant, retryPolicy, cancellationToken);
            await foreach (var model in collection.GetAllAsync(cancellationToken: cancellationToken))
            {
                results.Add(ToJson(model.Data));
            }
        }

        return results;
    }

    public async Task<JsonNode> GetHealthModelAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        return ToJson(model.Data);
    }

    public async Task<JsonNode> CreateHealthModelAsync(string subscription, string resourceGroup, string healthModelName, string location, IReadOnlyDictionary<string, string>? tags = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(location), location));

        var collection = await GetHealthModelCollectionAsync(subscription, resourceGroup, tenant, retryPolicy, cancellationToken);
        var data = new HealthModelData(new AzureLocation(location));
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                data.Tags[tag.Key] = tag.Value;
            }
        }

        var operation = await collection.CreateOrUpdateAsync(WaitUntil.Completed, healthModelName, data, cancellationToken);
        return ToJson(operation.Value.Data);
    }

    public async Task<JsonNode> UpdateHealthModelAsync(string subscription, string resourceGroup, string healthModelName, IReadOnlyDictionary<string, string>? tags = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));

        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var patch = new HealthModelPatch();
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                patch.Tags[tag.Key] = tag.Value;
            }
        }

        var operation = await model.UpdateAsync(WaitUntil.Completed, patch, cancellationToken);
        return ToJson(operation.Value.Data);
    }

    public async Task<JsonNode> DeleteHealthModelAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        await model.DeleteAsync(WaitUntil.Completed, cancellationToken);
        return BuildDeletedNode(healthModelName);
    }

    // Entities

    public async Task<List<JsonNode>> ListEntitiesAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var results = new List<JsonNode>();
        await foreach (var entity in model.GetHealthModelEntities().GetAllAsync(cancellationToken: cancellationToken))
        {
            results.Add(ToJson(entity.Data));
        }

        return results;
    }

    public async Task<JsonNode> GetEntityAsync(string subscription, string resourceGroup, string healthModelName, string entityName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName));
        var entity = await GetEntityResourceAsync(subscription, resourceGroup, healthModelName, entityName, tenant, retryPolicy, cancellationToken);
        return ToJson(entity.Data);
    }

    public async Task<JsonNode> CreateOrUpdateEntityAsync(string subscription, string resourceGroup, string healthModelName, string entityName, string propertiesJson, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName), (nameof(propertiesJson), propertiesJson));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var data = ReadFromProperties<HealthModelEntityData>(propertiesJson);
        var operation = await model.GetHealthModelEntities().CreateOrUpdateAsync(WaitUntil.Completed, entityName, data, cancellationToken);
        return ToJson(operation.Value.Data);
    }

    public async Task<JsonNode> DeleteEntityAsync(string subscription, string resourceGroup, string healthModelName, string entityName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName));
        var entity = await GetEntityResourceAsync(subscription, resourceGroup, healthModelName, entityName, tenant, retryPolicy, cancellationToken);
        await entity.DeleteAsync(WaitUntil.Completed, cancellationToken);
        return BuildDeletedNode(entityName);
    }

    public async Task<JsonNode> GetEntityHistoryAsync(string subscription, string resourceGroup, string healthModelName, string entityName, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, int? top = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName));
        var entity = await GetEntityResourceAsync(subscription, resourceGroup, healthModelName, entityName, tenant, retryPolicy, cancellationToken);
        var content = new EntityHistoryContent { StartOn = startTime, EndOn = endTime, Top = top };
        var response = await entity.GetHistoryAsync(content, cancellationToken);
        return ToJson(response.Value);
    }

    public async Task<JsonNode> GetEntitySignalHistoryAsync(string subscription, string resourceGroup, string healthModelName, string entityName, string signalName, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, int? top = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName), (nameof(signalName), signalName));
        var entity = await GetEntityResourceAsync(subscription, resourceGroup, healthModelName, entityName, tenant, retryPolicy, cancellationToken);
        var content = new EntitySignalHistoryContent(signalName) { StartOn = startTime, EndOn = endTime, Top = top };
        var response = await entity.GetSignalHistoryAsync(content, cancellationToken);
        return ToJson(response.Value);
    }

    public async Task<JsonNode> GetEntitySignalRecommendationsAsync(string subscription, string resourceGroup, string healthModelName, string entityName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName));
        var entity = await GetEntityResourceAsync(subscription, resourceGroup, healthModelName, entityName, tenant, retryPolicy, cancellationToken);
        var response = await entity.GetSignalRecommendationsAsync(cancellationToken);
        return ToJson(response.Value);
    }

    public async Task<JsonNode> AddEntityDataAnnotationAsync(string subscription, string resourceGroup, string healthModelName, string entityName, IReadOnlyDictionary<string, string> annotationDetails, string? description = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName));
        var entity = await GetEntityResourceAsync(subscription, resourceGroup, healthModelName, entityName, tenant, retryPolicy, cancellationToken);
        var content = new EntityAddDataAnnotationContent(new Dictionary<string, string>(annotationDetails)) { Description = description };
        var response = await entity.AddDataAnnotationAsync(content, cancellationToken);
        return ToJson(response.Value);
    }

    public async Task<JsonNode> GetEntityDataAnnotationsAsync(string subscription, string resourceGroup, string healthModelName, string entityName, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, int? top = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName));
        var entity = await GetEntityResourceAsync(subscription, resourceGroup, healthModelName, entityName, tenant, retryPolicy, cancellationToken);
        var content = new EntityGetDataAnnotationsContent { StartOn = startTime, EndOn = endTime, Top = top };
        var response = await entity.GetDataAnnotationsAsync(content, cancellationToken);
        return ToJson(response.Value);
    }

    public async Task<JsonNode> IngestEntityHealthReportAsync(string subscription, string resourceGroup, string healthModelName, string entityName, string signalName, string healthState, double? value = null, int? expiresInMinutes = null, string? additionalContext = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(entityName), entityName), (nameof(signalName), signalName), (nameof(healthState), healthState));
        var entity = await GetEntityResourceAsync(subscription, resourceGroup, healthModelName, entityName, tenant, retryPolicy, cancellationToken);
        var content = new EntityHealthReportContent(signalName, new EntityHealthState(healthState))
        {
            Value = value,
            ExpiresInMinutes = expiresInMinutes,
            AdditionalContext = additionalContext,
        };
        await entity.IngestHealthReportAsync(content, cancellationToken);
        var node = new JsonObject
        {
            ["entityName"] = entityName,
            ["signalName"] = signalName,
            ["ingested"] = true,
        };
        return node;
    }

    // Signal definitions

    public async Task<List<JsonNode>> ListSignalDefinitionsAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var results = new List<JsonNode>();
        await foreach (var item in model.GetHealthModelSignalDefinitions().GetAllAsync(cancellationToken: cancellationToken))
        {
            results.Add(ToJson(item.Data));
        }

        return results;
    }

    public async Task<JsonNode> GetSignalDefinitionAsync(string subscription, string resourceGroup, string healthModelName, string signalDefinitionName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(signalDefinitionName), signalDefinitionName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelSignalDefinitionAsync(signalDefinitionName, cancellationToken);
        return ToJson(response.Value.Data);
    }

    public async Task<JsonNode> CreateOrUpdateSignalDefinitionAsync(string subscription, string resourceGroup, string healthModelName, string signalDefinitionName, string propertiesJson, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(signalDefinitionName), signalDefinitionName), (nameof(propertiesJson), propertiesJson));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var data = ReadFromProperties<HealthModelSignalDefinitionData>(propertiesJson);
        var operation = await model.GetHealthModelSignalDefinitions().CreateOrUpdateAsync(WaitUntil.Completed, signalDefinitionName, data, cancellationToken);
        return ToJson(operation.Value.Data);
    }

    public async Task<JsonNode> DeleteSignalDefinitionAsync(string subscription, string resourceGroup, string healthModelName, string signalDefinitionName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(signalDefinitionName), signalDefinitionName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelSignalDefinitionAsync(signalDefinitionName, cancellationToken);
        await response.Value.DeleteAsync(WaitUntil.Completed, cancellationToken);
        return BuildDeletedNode(signalDefinitionName);
    }

    // Relationships

    public async Task<List<JsonNode>> ListRelationshipsAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var results = new List<JsonNode>();
        await foreach (var item in model.GetHealthModelRelationships().GetAllAsync(cancellationToken: cancellationToken))
        {
            results.Add(ToJson(item.Data));
        }

        return results;
    }

    public async Task<JsonNode> GetRelationshipAsync(string subscription, string resourceGroup, string healthModelName, string relationshipName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(relationshipName), relationshipName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelRelationshipAsync(relationshipName, cancellationToken);
        return ToJson(response.Value.Data);
    }

    public async Task<JsonNode> CreateOrUpdateRelationshipAsync(string subscription, string resourceGroup, string healthModelName, string relationshipName, string propertiesJson, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(relationshipName), relationshipName), (nameof(propertiesJson), propertiesJson));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var data = ReadFromProperties<HealthModelRelationshipData>(propertiesJson);
        var operation = await model.GetHealthModelRelationships().CreateOrUpdateAsync(WaitUntil.Completed, relationshipName, data, cancellationToken);
        return ToJson(operation.Value.Data);
    }

    public async Task<JsonNode> DeleteRelationshipAsync(string subscription, string resourceGroup, string healthModelName, string relationshipName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(relationshipName), relationshipName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelRelationshipAsync(relationshipName, cancellationToken);
        await response.Value.DeleteAsync(WaitUntil.Completed, cancellationToken);
        return BuildDeletedNode(relationshipName);
    }

    // Discovery rules

    public async Task<List<JsonNode>> ListDiscoveryRulesAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var results = new List<JsonNode>();
        await foreach (var item in model.GetHealthModelDiscoveryRules().GetAllAsync(cancellationToken: cancellationToken))
        {
            results.Add(ToJson(item.Data));
        }

        return results;
    }

    public async Task<JsonNode> GetDiscoveryRuleAsync(string subscription, string resourceGroup, string healthModelName, string discoveryRuleName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(discoveryRuleName), discoveryRuleName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelDiscoveryRuleAsync(discoveryRuleName, cancellationToken);
        return ToJson(response.Value.Data);
    }

    public async Task<JsonNode> CreateOrUpdateDiscoveryRuleAsync(string subscription, string resourceGroup, string healthModelName, string discoveryRuleName, string propertiesJson, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(discoveryRuleName), discoveryRuleName), (nameof(propertiesJson), propertiesJson));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var data = ReadFromProperties<HealthModelDiscoveryRuleData>(propertiesJson);
        var operation = await model.GetHealthModelDiscoveryRules().CreateOrUpdateAsync(WaitUntil.Completed, discoveryRuleName, data, cancellationToken);
        return ToJson(operation.Value.Data);
    }

    public async Task<JsonNode> DeleteDiscoveryRuleAsync(string subscription, string resourceGroup, string healthModelName, string discoveryRuleName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(discoveryRuleName), discoveryRuleName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelDiscoveryRuleAsync(discoveryRuleName, cancellationToken);
        await response.Value.DeleteAsync(WaitUntil.Completed, cancellationToken);
        return BuildDeletedNode(discoveryRuleName);
    }

    // Authentication settings

    public async Task<List<JsonNode>> ListAuthenticationSettingsAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var results = new List<JsonNode>();
        await foreach (var item in model.GetHealthModelAuthenticationSettings().GetAllAsync(cancellationToken: cancellationToken))
        {
            results.Add(ToJson(item.Data));
        }

        return results;
    }

    public async Task<JsonNode> GetAuthenticationSettingAsync(string subscription, string resourceGroup, string healthModelName, string authenticationSettingName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(authenticationSettingName), authenticationSettingName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelAuthenticationSettingAsync(authenticationSettingName, cancellationToken);
        return ToJson(response.Value.Data);
    }

    public async Task<JsonNode> CreateOrUpdateAuthenticationSettingAsync(string subscription, string resourceGroup, string healthModelName, string authenticationSettingName, string propertiesJson, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(authenticationSettingName), authenticationSettingName), (nameof(propertiesJson), propertiesJson));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var data = ReadFromProperties<HealthModelAuthenticationSettingData>(propertiesJson);
        var operation = await model.GetHealthModelAuthenticationSettings().CreateOrUpdateAsync(WaitUntil.Completed, authenticationSettingName, data, cancellationToken);
        return ToJson(operation.Value.Data);
    }

    public async Task<JsonNode> DeleteAuthenticationSettingAsync(string subscription, string resourceGroup, string healthModelName, string authenticationSettingName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(authenticationSettingName), authenticationSettingName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var response = await model.GetHealthModelAuthenticationSettingAsync(authenticationSettingName, cancellationToken);
        await response.Value.DeleteAsync(WaitUntil.Completed, cancellationToken);
        return BuildDeletedNode(authenticationSettingName);
    }

    // Identity

    public async Task<JsonNode> GetIdentityAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        return ExtractIdentity(model.Data);
    }

    public async Task<JsonNode> AssignIdentityAsync(string subscription, string resourceGroup, string healthModelName, string identityType, IEnumerable<string>? userAssignedIdentityIds = null, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName), (nameof(identityType), identityType));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);

        var identity = new ManagedServiceIdentity(ParseIdentityType(identityType));
        if (userAssignedIdentityIds != null)
        {
            foreach (var id in userAssignedIdentityIds)
            {
                identity.UserAssignedIdentities[new ResourceIdentifier(id)] = new UserAssignedIdentity();
            }
        }

        var patch = new HealthModelPatch { Identity = identity };
        var operation = await model.UpdateAsync(WaitUntil.Completed, patch, cancellationToken);
        return ExtractIdentity(operation.Value.Data);
    }

    public async Task<JsonNode> RemoveIdentityAsync(string subscription, string resourceGroup, string healthModelName, string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(healthModelName), healthModelName));
        var model = await GetHealthModelResourceAsync(subscription, resourceGroup, healthModelName, tenant, retryPolicy, cancellationToken);
        var patch = new HealthModelPatch { Identity = new ManagedServiceIdentity(ManagedServiceIdentityType.None) };
        var operation = await model.UpdateAsync(WaitUntil.Completed, patch, cancellationToken);
        return ExtractIdentity(operation.Value.Data);
    }

    private static ManagedServiceIdentityType ParseIdentityType(string identityType) =>
        identityType.Replace(" ", string.Empty).ToLowerInvariant() switch
        {
            "systemassigned" => ManagedServiceIdentityType.SystemAssigned,
            "userassigned" => ManagedServiceIdentityType.UserAssigned,
            "systemassigned,userassigned" or "systemassigneduserassigned" => ManagedServiceIdentityType.SystemAssignedUserAssigned,
            "none" => ManagedServiceIdentityType.None,
            _ => new ManagedServiceIdentityType(identityType),
        };

    private static JsonNode ExtractIdentity(HealthModelData data) =>
        ToJson(data)["identity"] ?? new JsonObject { ["type"] = "None" };

    private static JsonNode BuildDeletedNode(string name) => new JsonObject
    {
        ["name"] = name,
        ["deleted"] = true,
    };
}

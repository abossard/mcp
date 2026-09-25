// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;

namespace Azure.Mcp.Tools.Monitor.Sandbox;

internal interface IHealthModelReadCodeRunner
{
    string SubscriptionId { get; }
    string ArmEndpoint { get; }

    Task<HealthModelData> GetHealthModelReadAsync(
        string resourceGroup, string healthModelName, CancellationToken cancellationToken);

    Task<HealthModelListPage<HealthModelData>> ListHealthModelsAsync(
        string? resourceGroup, string? continuationToken, CancellationToken cancellationToken);

    Task<HealthModelEntityData> GetEntityAsync(
        PlanScope scope, string entityName, CancellationToken cancellationToken);

    Task<HealthModelEntityListPage> ListEntitiesAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken);

    Task<EntityHistoryResult> GetHistoryAsync(
        PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime,
        int? top, string? nextMarker, CancellationToken cancellationToken);

    Task<EntitySignalHistoryResult> GetSignalHistoryAsync(
        PlanScope scope, string entityName, string signalName, DateTimeOffset? startTime, DateTimeOffset? endTime,
        int? top, string? nextMarker, CancellationToken cancellationToken);

    Task<EntityGetSignalRecommendationsResult> GetSignalRecommendationsAsync(
        PlanScope scope, string entityName, CancellationToken cancellationToken);

    Task<EntityGetDataAnnotationsResult> GetDataAnnotationsAsync(
        PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime,
        int? top, string? nextMarker, CancellationToken cancellationToken);

    Task<HealthModelRelationshipData> GetRelationshipAsync(
        PlanScope scope, string relationshipName, CancellationToken cancellationToken);

    Task<HealthModelListPage<HealthModelRelationshipData>> ListRelationshipsAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken);

    Task<HealthModelSignalDefinitionData> GetSignalDefinitionAsync(
        PlanScope scope, string signalDefinitionName, CancellationToken cancellationToken);

    Task<HealthModelListPage<HealthModelSignalDefinitionData>> ListSignalDefinitionsAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken);

    Task<HealthModelAuthenticationSettingData> GetAuthenticationSettingAsync(
        PlanScope scope, string authenticationSettingName, CancellationToken cancellationToken);

    Task<HealthModelListPage<HealthModelAuthenticationSettingData>> ListAuthenticationSettingsAsync(
        PlanScope scope, string? continuationToken, CancellationToken cancellationToken);

    Task<HealthModelDiscoveryRuleData> GetDiscoveryRuleAsync(
        PlanScope scope, string discoveryRuleName, CancellationToken cancellationToken);

    Task<HealthModelListPage<HealthModelDiscoveryRuleData>> ListDiscoveryRulesAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken);

    Task<JsonNode> ListOperationsAsync(CancellationToken cancellationToken);
}

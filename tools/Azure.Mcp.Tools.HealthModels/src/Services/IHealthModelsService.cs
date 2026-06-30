// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.HealthModels.Services;

public interface IHealthModelsService
{
    // Health model (root tracked resource)
    Task<List<JsonNode>> ListHealthModelsAsync(
        string subscription,
        string? resourceGroup = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> GetHealthModelAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> CreateHealthModelAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string location,
        IReadOnlyDictionary<string, string>? tags = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> UpdateHealthModelAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        IReadOnlyDictionary<string, string>? tags = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> DeleteHealthModelAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    // Entities
    Task<List<JsonNode>> ListEntitiesAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> GetEntityAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> CreateOrUpdateEntityAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        string propertiesJson,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> DeleteEntityAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> GetEntityHistoryAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        int? top = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> GetEntitySignalHistoryAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        string signalName,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        int? top = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> GetEntitySignalRecommendationsAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> AddEntityDataAnnotationAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        IReadOnlyDictionary<string, string> annotationDetails,
        string? description = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> GetEntityDataAnnotationsAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        int? top = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    Task<JsonNode> IngestEntityHealthReportAsync(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string entityName,
        string signalName,
        string healthState,
        double? value = null,
        int? expiresInMinutes = null,
        string? additionalContext = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    // Signal definitions
    Task<List<JsonNode>> ListSignalDefinitionsAsync(
        string subscription, string resourceGroup, string healthModelName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> GetSignalDefinitionAsync(
        string subscription, string resourceGroup, string healthModelName, string signalDefinitionName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> CreateOrUpdateSignalDefinitionAsync(
        string subscription, string resourceGroup, string healthModelName, string signalDefinitionName, string propertiesJson,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> DeleteSignalDefinitionAsync(
        string subscription, string resourceGroup, string healthModelName, string signalDefinitionName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    // Relationships
    Task<List<JsonNode>> ListRelationshipsAsync(
        string subscription, string resourceGroup, string healthModelName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> GetRelationshipAsync(
        string subscription, string resourceGroup, string healthModelName, string relationshipName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> CreateOrUpdateRelationshipAsync(
        string subscription, string resourceGroup, string healthModelName, string relationshipName, string propertiesJson,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> DeleteRelationshipAsync(
        string subscription, string resourceGroup, string healthModelName, string relationshipName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    // Discovery rules
    Task<List<JsonNode>> ListDiscoveryRulesAsync(
        string subscription, string resourceGroup, string healthModelName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> GetDiscoveryRuleAsync(
        string subscription, string resourceGroup, string healthModelName, string discoveryRuleName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> CreateOrUpdateDiscoveryRuleAsync(
        string subscription, string resourceGroup, string healthModelName, string discoveryRuleName, string propertiesJson,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> DeleteDiscoveryRuleAsync(
        string subscription, string resourceGroup, string healthModelName, string discoveryRuleName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    // Authentication settings
    Task<List<JsonNode>> ListAuthenticationSettingsAsync(
        string subscription, string resourceGroup, string healthModelName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> GetAuthenticationSettingAsync(
        string subscription, string resourceGroup, string healthModelName, string authenticationSettingName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> CreateOrUpdateAuthenticationSettingAsync(
        string subscription, string resourceGroup, string healthModelName, string authenticationSettingName, string propertiesJson,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> DeleteAuthenticationSettingAsync(
        string subscription, string resourceGroup, string healthModelName, string authenticationSettingName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    // Identity
    Task<JsonNode> GetIdentityAsync(
        string subscription, string resourceGroup, string healthModelName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> AssignIdentityAsync(
        string subscription, string resourceGroup, string healthModelName, string identityType,
        IEnumerable<string>? userAssignedIdentityIds = null,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);

    Task<JsonNode> RemoveIdentityAsync(
        string subscription, string resourceGroup, string healthModelName,
        string? tenant = null, RetryPolicyOptions? retryPolicy = null, CancellationToken cancellationToken = default);
}

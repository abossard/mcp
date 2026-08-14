// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.Mcp.Tools.Monitor.Sandbox;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// Records every call the sandbox makes. Counting is the point: a script that silently made no call at all
/// would satisfy an assertion that merely checks the returned shape, so the tests assert on this log.
/// </summary>
internal sealed class FakeHealthModelSdkRunner : IHealthModelCallRunner, IHealthModelWriteRunner, IHealthModelSdkRunner
{
    internal List<string> Calls { get; } = [];

    /// <summary>
    /// When set, the named operation throws it instead of answering. The sandbox has to survive an SDK
    /// failure the same way it survives a JavaScript one, so the tests need a runner that fails like ARM.
    /// </summary>
    internal Func<Exception>? ThrowOn { get; set; }

    internal string? ThrowOnCall { get; set; }

    private void MaybeThrow(string call)
    {
        if (ThrowOn is not null && (ThrowOnCall is null || ThrowOnCall == call))
        {
            throw ThrowOn();
        }
    }

    /// <summary>
    /// Deliberately uneven: three entities across three health states, so a script that aggregates produces
    /// varied numbers instead of 1 everywhere and a broken filter cannot hide behind a count that happens
    /// to be right.
    /// </summary>
    private static readonly (string Name, string Health)[] Entities =
    [
        ("web", "Healthy"),
        ("api", "Degraded"),
        ("db", "Unhealthy"),
    ];

    public Task<HealthModelEntityListPage> ListEntitiesAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
    {
        Calls.Add($"listEntities:{scope.ResourceGroup}/{scope.HealthModel}:asOf={timestamp?.ToString("o") ?? "-"}:cursor={continuationToken ?? "-"}");
        var items = Entities.Select(entity => Entity(entity.Name, entity.Health)).ToList();
        return Task.FromResult(new HealthModelEntityListPage(items, continuationToken is null ? "page2" : null));
    }

    public Task<HealthModelEntityData> GetEntityAsync(PlanScope scope, string entityName, CancellationToken cancellationToken)
    {
        Calls.Add($"getEntity:{scope.ResourceGroup}/{scope.HealthModel}/{entityName}");
        MaybeThrow("getEntity");
        var match = Entities.FirstOrDefault(entity => entity.Name == entityName);
        return Task.FromResult(Entity(entityName, match.Health ?? "Unknown"));
    }

    public Task<HealthModelListPage<HealthModelRelationshipData>> ListRelationshipsAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
    {
        Calls.Add($"listRelationships:{scope.ResourceGroup}/{scope.HealthModel}");
        return Task.FromResult(new HealthModelListPage<HealthModelRelationshipData>([], null));
    }

    public Task<HealthModelListPage<HealthModelSignalDefinitionData>> ListSignalDefinitionsAsync(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
    {
        Calls.Add($"listSignalDefinitions:{scope.ResourceGroup}/{scope.HealthModel}");
        return Task.FromResult(new HealthModelListPage<HealthModelSignalDefinitionData>([], null));
    }

    public Task<EntityHistoryResult> GetHistoryAsync(
        PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
        string? nextMarker, CancellationToken cancellationToken)
    {
        Calls.Add($"getHistory:{entityName}:top={top?.ToString() ?? "-"}:from={startTime?.ToString("o") ?? "-"}");
        return Task.FromResult(ArmCloudHealthModelFactory.EntityHistoryResult(entityName, [], null));
    }

    public Task<EntitySignalHistoryResult> GetSignalHistoryAsync(
        PlanScope scope, string entityName, string signalName, DateTimeOffset? startTime, DateTimeOffset? endTime,
        int? top, string? nextMarker, CancellationToken cancellationToken)
    {
        Calls.Add($"getSignalHistory:{entityName}:{signalName}");
        return Task.FromResult(ArmCloudHealthModelFactory.EntitySignalHistoryResult(entityName, signalName, [], null));
    }

    public Task<EntityGetSignalRecommendationsResult> GetSignalRecommendationsAsync(
        PlanScope scope, string entityName, CancellationToken cancellationToken)
    {
        Calls.Add($"getSignalRecommendations:{entityName}");
        return Task.FromResult(ArmCloudHealthModelFactory.EntityGetSignalRecommendationsResult([]));
    }

    public Task<EntityGetDataAnnotationsResult> GetDataAnnotationsAsync(
        PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
        string? nextMarker, CancellationToken cancellationToken)
    {
        Calls.Add($"getDataAnnotations:{entityName}");
        return Task.FromResult(ArmCloudHealthModelFactory.EntityGetDataAnnotationsResult(entityName, [], null));
    }

    public Task<HealthModelResourcePage> ListAsync(
        PlanScope scope, HealthModelResourceKind kind, string? continuationToken, CancellationToken cancellationToken)
    {
        Calls.Add($"list:{kind}");
        return Task.FromResult(new HealthModelResourcePage([], null));
    }

    public Task PutAsync(PlanScope scope, HealthModelResourceKind kind, string name, JsonObject body, CancellationToken cancellationToken)
    {
        Calls.Add($"put:{kind}:{name}:{body.ToJsonString()}");
        MaybeThrow("put");
        return Task.CompletedTask;
    }

    public Task DeleteAsync(PlanScope scope, HealthModelResourceKind kind, string name, CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{kind}:{name}");
        return Task.CompletedTask;
    }

    public Task<HealthModelListPage<HealthModelData>> ListHealthModelsAsync(
        string? resourceGroup, string? continuationToken, CancellationToken cancellationToken)
    {
        Calls.Add($"listHealthModels:{resourceGroup ?? "<subscription>"}");
        return Task.FromResult(new HealthModelListPage<HealthModelData>([], null));
    }

    public Task<HealthModelData> GetHealthModelAsync(string resourceGroup, string healthModelName, CancellationToken cancellationToken)
    {
        Calls.Add($"getHealthModel:{resourceGroup}/{healthModelName}");
        return Task.FromResult(ArmCloudHealthModelFactory.HealthModelData(
            name: healthModelName, location: new Azure.Core.AzureLocation("westeurope")));
    }

    public Task<JsonNode> AddDataAnnotationAsync(
        PlanScope scope, string entityName, JsonObject body, CancellationToken cancellationToken)
    {
        Calls.Add($"addDataAnnotation:{entityName}:{body.ToJsonString()}");
        return Task.FromResult<JsonNode>(new JsonObject { ["name"] = "annotation-1" });
    }

    public Task IngestHealthReportAsync(
        PlanScope scope, string entityName, JsonObject body, CancellationToken cancellationToken)
    {
        Calls.Add($"ingestHealthReport:{entityName}:{body.ToJsonString()}");
        return Task.CompletedTask;
    }

    private static HealthModelEntityData Entity(string name, string health) =>
        ArmCloudHealthModelFactory.HealthModelEntityData(
            name: name,
            properties: ArmCloudHealthModelFactory.HealthModelEntityProperties(
                healthState: new EntityHealthState(health)));
}

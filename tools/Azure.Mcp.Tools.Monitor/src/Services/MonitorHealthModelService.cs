// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Core.Services.Azure;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.Mcp.Tools.Monitor.Sandbox;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.Monitor.Services;

public class MonitorHealthModelService(IAzureService azureService, ILogger<MonitorHealthModelService> logger)
    : BaseAzureService(azureService), IMonitorHealthModelService
{
    private const string CloudHealthOperationsApiVersion = "2026-05-01-preview";
    private readonly ILogger<MonitorHealthModelService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    internal static HealthModelSummary ToSummary(HealthModelData data) =>
        new()
        {
            Id = data.Id?.ToString(),
            Name = data.Name,
            ResourceGroup = data.Id?.ResourceGroupName,
            Location = data.Location.ToString(),
            ProvisioningState = data.HealthModelProvisioningState?.ToString(),
        };

    public async Task<List<HealthModelSummary>> ListHealthModels(
        string subscription,
        string? resourceGroup = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription));

        var subscriptionResource = await AzureService.GetSubscription(subscription, tenant, retryPolicy, cancellationToken);
        var results = new List<HealthModelSummary>();

        if (string.IsNullOrEmpty(resourceGroup))
        {
            await foreach (var model in subscriptionResource.GetHealthModelsAsync(cancellationToken))
            {
                results.Add(ToSummary(model.Data));
            }
        }
        else
        {
            var resourceGroupResource = await subscriptionResource.GetResourceGroupAsync(resourceGroup, cancellationToken);
            await foreach (var model in resourceGroupResource.Value.GetHealthModels().GetAllAsync(cancellationToken: cancellationToken))
            {
                results.Add(ToSummary(model.Data));
            }
        }

        return results;
    }

    internal static HealthModelDetail ToDetail(HealthModelData data, string? healthState) =>
        new()
        {
            Id = data.Id?.ToString(),
            Name = data.Name,
            ResourceGroup = data.Id?.ResourceGroupName,
            Location = data.Location.ToString(),
            ProvisioningState = data.HealthModelProvisioningState?.ToString(),
            HealthState = healthState,
            Identity = ToIdentity(data.Identity),
            Tags = data.Tags,
        };

    internal static HealthModelIdentity? ToIdentity(ManagedServiceIdentity? identity)
    {
        if (identity is null)
        {
            return null;
        }

        return new HealthModelIdentity
        {
            Type = identity.ManagedServiceIdentityType.ToString(),
            PrincipalId = identity.PrincipalId?.ToString(),
            TenantId = identity.TenantId?.ToString(),
            UserAssignedIdentities = identity.UserAssignedIdentities?
                .Keys.Select(id => id.ToString()).ToList(),
        };
    }

    public async Task<HealthModelDetail> GetHealthModel(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters(
            (nameof(subscription), subscription),
            (nameof(resourceGroup), resourceGroup),
            (nameof(healthModelName), healthModelName));

        var subscriptionResource = await AzureService.GetSubscription(subscription, tenant, retryPolicy, cancellationToken);
        var resourceGroupResource = await subscriptionResource.GetResourceGroupAsync(resourceGroup, cancellationToken);
        var model = await resourceGroupResource.Value.GetHealthModels().GetAsync(healthModelName, cancellationToken);
        var healthState = await TryGetRootHealthStateAsync(model.Value, healthModelName, cancellationToken);
        return ToDetail(model.Value.Data, healthState);
    }

    private async Task<string?> TryGetRootHealthStateAsync(HealthModelResource model, string rootEntityName, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await model.GetHealthModelEntityAsync(rootEntityName, cancellationToken);
            return entity.Value.Data.Properties?.HealthState?.ToString();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "Could not resolve root-entity health for health model '{HealthModel}'; returning null healthState. Error: {Message}",
                rootEntityName,
                ex.Message);
            return null;
        }
    }

    public async Task<IReadOnlyList<HealthModelQueryResult>> ExecuteHealthModelQueries(
        string subscription,
        IReadOnlyList<HealthModelQuery> queries,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription));
        ArgumentNullException.ThrowIfNull(queries);

        var subscriptionResource = await AzureService.GetSubscription(subscription, tenant, retryPolicy, cancellationToken);
        var armClient = await CreateArmClientAsync(tenant, retryPolicy, cancellationToken: cancellationToken);
        var runner = new ArmHealthModelCallRunner(
            subscriptionResource,
            armClient,
            AzureService.CloudConfiguration.ArmEnvironment.Endpoint.ToString());

        var plan = HealthModelQueryPlanner.Plan(queries);
        return await HealthModelQueryExecutor.ExecuteAsync(plan, runner, cancellationToken);
    }

    public async Task<HealthModelScriptResult> ExecuteHealthModelScript(
        string subscription,
        string code,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(code), code));

        var subscriptionResource = await AzureService.GetSubscription(subscription, tenant, retryPolicy, cancellationToken);
        var armClient = await CreateArmClientAsync(tenant, retryPolicy, cancellationToken: cancellationToken);
        var runner = new ArmHealthModelCallRunner(
            subscriptionResource,
            armClient,
            AzureService.CloudConfiguration.ArmEnvironment.Endpoint.ToString());

        return await Task.Run(
            () => HealthModelScriptRuntime.Run(code, runner, runner, runner, cancellationToken),
            cancellationToken);
    }

    public async Task<HealthModelScriptResult> ExecuteHealthModelReadCode(
        string subscription,
        string code,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription), (nameof(code), code));

        var subscriptionResource = await AzureService.GetSubscription(subscription, tenant, retryPolicy, cancellationToken);
        var armClient = await CreateArmClientAsync(tenant, retryPolicy, cancellationToken: cancellationToken);
        var runner = new ArmHealthModelCallRunner(
            subscriptionResource,
            armClient,
            AzureService.CloudConfiguration.ArmEnvironment.Endpoint.ToString(),
            callCancellationToken => ListCloudHealthOperationsAsync(tenant, callCancellationToken));

        return await Task.Run(
            () => HealthModelScriptRuntime.RunReadOnly(code, runner, cancellationToken),
            cancellationToken);
    }

    public async Task<HealthModelGraphEditResult> ExecuteHealthModelGraphEdit(
        string subscription,
        IReadOnlyList<HealthModelChange> changes,
        HealthModelChangeMode mode,
        HealthModelChangeExpectation? expect,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredParameters((nameof(subscription), subscription));
        ArgumentNullException.ThrowIfNull(changes);

        var subscriptionResource = await AzureService.GetSubscription(subscription, tenant, retryPolicy, cancellationToken);
        var armClient = await CreateArmClientAsync(tenant, retryPolicy, cancellationToken: cancellationToken);
        var runner = new ArmHealthModelCallRunner(
            subscriptionResource,
            armClient,
            AzureService.CloudConfiguration.ArmEnvironment.Endpoint.ToString());

        return await new HealthModelGraphEditExecutor(runner).ExecuteAsync(changes, mode, expect, cancellationToken);
    }

    private async Task<JsonNode> ListCloudHealthOperationsAsync(
        string? tenant,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetArmAccessTokenAsync(tenant, cancellationToken);
        var endpoint = AzureService.CloudConfiguration.ArmEnvironment.Endpoint.ToString().TrimEnd('/');
        var url = $"{endpoint}/providers/Microsoft.CloudHealth/operations?api-version={CloudHealthOperationsApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", accessToken.Token);

        var client = AzureService.GetClient(tenant);
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"CloudHealth operations list failed with HTTP {(int)response.StatusCode}: {payload}",
                null,
                response.StatusCode);
        }

        return JsonNode.Parse(payload) ?? new JsonObject();
    }
}

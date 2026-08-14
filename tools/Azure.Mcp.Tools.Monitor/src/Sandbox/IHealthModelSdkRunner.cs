// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.ResourceManager.CloudHealth;

namespace Azure.Mcp.Tools.Monitor.Sandbox;

/// <summary>
/// The operations the sandbox facade needs that the query and graph-edit runners do not already cover.
/// Kept separate from <see cref="IHealthModelCallRunner"/> so adding the sandbox does not change the
/// interface the query executor and its fakes are written against.
/// </summary>
internal interface IHealthModelSdkRunner
{
    Task<HealthModelListPage<HealthModelData>> ListHealthModelsAsync(
        string? resourceGroup, string? continuationToken, CancellationToken cancellationToken);

    Task<HealthModelData> GetHealthModelAsync(
        string resourceGroup, string healthModelName, CancellationToken cancellationToken);

    Task<JsonNode> AddDataAnnotationAsync(
        PlanScope scope, string entityName, JsonObject body, CancellationToken cancellationToken);

    Task IngestHealthReportAsync(
        PlanScope scope, string entityName, JsonObject body, CancellationToken cancellationToken);
}

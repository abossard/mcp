// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.Monitor.Services;

public interface IMonitorHealthModelService
{
    /// <summary>
    /// Lists Azure Monitor Health Models in a subscription or resource group. Returns a summary projection.
    /// </summary>
    /// <param name="subscription">Subscription ID or name.</param>
    /// <param name="resourceGroup">Optional resource group to scope the listing.</param>
    /// <param name="tenant">Optional tenant ID.</param>
    /// <param name="retryPolicy">Optional retry policy.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>List health models.</returns>
    Task<List<HealthModelSummary>> ListHealthModels(
        string subscription,
        string? resourceGroup = null,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single Azure Monitor Health Model by name.
    /// </summary>
    /// <param name="subscription">Subscription ID or name.</param>
    /// <param name="resourceGroup">The resource group containing the health model.</param>
    /// <param name="healthModelName">The health model name.</param>
    /// <param name="tenant">Optional tenant ID.</param>
    /// <param name="retryPolicy">Optional retry policy.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The health model resource.</returns>
    Task<HealthModelDetail> GetHealthModel(
        string subscription,
        string resourceGroup,
        string healthModelName,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a batch of typed health-model read queries in a single call. The queries are planned into
    /// the minimum set of Azure Resource Manager calls (grouped by model, deduplicated, health-filtered) and
    /// each input query yields exactly one result, correlated by its zero-based input position (queryIndex)
    /// and returned in input order. Per-entity failures are isolated on their own entity node so a single
    /// failing entity does not abort the query or the rest of the batch.
    /// </summary>
    /// <param name="subscription">Subscription ID or name.</param>
    /// <param name="queries">The batch of queries to execute.</param>
    /// <param name="tenant">Optional tenant ID.</param>
    /// <param name="retryPolicy">Optional retry policy.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One result per input query, in input order.</returns>
    Task<IReadOnlyList<HealthModelQueryResult>> ExecuteHealthModelQueries(
        string subscription,
        IReadOnlyList<HealthModelQuery> queries,
        string? tenant = null,
        RetryPolicyOptions? retryPolicy = null,
        CancellationToken cancellationToken = default);
}

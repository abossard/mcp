// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Options.HealthModels;
using Azure.Mcp.Tools.Monitor.Services;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

[CommandMetadata(
    Id = "3f2b8c14-9a6d-4e70-bd51-7c2f0a9e4d38",
    Name = "query",
    Title = "Query Azure Monitor Health Models",
    Description = """
        Run a batch of typed, read-only Azure Monitor Health Model queries (Microsoft.CloudHealth/healthmodels) in a single
        call and get one result per query, returned in input order and correlated by a system-assigned zero-based queryIndex.
        Supported query kinds are entity list (optionally point-in-time), entity get, entity health history, signal history,
        signal recommendations, and data annotations. Queries can target a specific entity or, using a health filter (e.g.
        only unhealthy entities), every matching entity resolved from a single shared entity list. The queries are planned into
        the fewest Azure Resource Manager calls (grouped by model, deduplicated). Payloads are compact by default and callers
        can opt into closed typed field groups or full SDK fidelity. Each result carries a uniform list of
        per-entity nodes (for example
        entity.properties.healthState, history.history[], signalHistory.history[], recommendations.recommendedSignals[],
        annotations.annotations[]). API-native pagination returns one page per request with page.complete, returnedCount, and
        the exact continuationToken or nextMarker needed to resume. A whole-query failure sets the query's success to
        false; once fan-out begins a failing entity is isolated on its own node without affecting sibling entities or queries.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class HealthModelQueryCommand(IMonitorHealthModelService healthModelService, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<HealthModelQueryOptions, List<HealthModelQueryResult>>(subscriptionResolver)
{
    private readonly IMonitorHealthModelService _healthModelService = healthModelService;

    public override void ValidateOptions(HealthModelQueryOptions options, ValidationResult validationResult)
    {
        base.ValidateOptions(options, validationResult);

        if (!validationResult.IsValid)
        {
            return;
        }

        if (!TryParseQueries(options.Queries, out _, out var error))
        {
            validationResult.Errors.Add(error);
        }
    }

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, HealthModelQueryOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryParseQueries(options.Queries, out var queries, out var error))
            {
                throw new ArgumentException(error, nameof(options));
            }

            var results = await _healthModelService.ExecuteHealthModelQueries(
                options.Subscription!,
                queries,
                options.Tenant,
                options.RetryPolicy,
                cancellationToken);

            // Results are returned one-per-input-query in input order (indexed by queryIndex), so the
            // optional echo-only label is re-attached by input position — it never influenced planning.
            for (var index = 0; index < results.Count; index++)
            {
                results[index].Label = queries[index].Label;
            }

            context.Response.Results = ResponseResult.Create<List<HealthModelQueryResult>>([.. results], MonitorJsonContext.Default.ListHealthModelQueryResult);
        }
        catch (Exception ex)
        {
            HandleException(context, ex);
        }

        return context.Response;
    }

    protected override HttpStatusCode GetStatusCode(Exception ex) => ex switch
    {
        JsonException => HttpStatusCode.BadRequest,
        ArgumentException => HttpStatusCode.BadRequest,
        _ => base.GetStatusCode(ex),
    };

    private static bool TryParseQueries(string json, out IReadOnlyList<HealthModelQuery> queries, out string error)
    {
        queries = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "--queries is required and must be a JSON array of health-model queries.";
            return false;
        }

        HealthModelQuery[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, MonitorJsonContext.Default.HealthModelQueryArray);
        }
        catch (JsonException ex)
        {
            error = $"--queries must be a valid JSON array of health-model queries. {ex.Message}";
            return false;
        }

        if (parsed is null || parsed.Length == 0)
        {
            error = "--queries must contain at least one query.";
            return false;
        }

        queries = parsed;
        return true;
    }
}

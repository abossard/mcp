// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;
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
        signal recommendations, data annotations, relationship list (the model's parent/child dependency edges) and signal
        definition list (the model's signal definitions, with their evaluation thresholds). Relationships and signal
        definitions make a dependency rollup explainable and let a caller read real entity and definition names instead of
        guessing them. Each kind is its own closed shape carrying exactly the inputs it accepts, so an input that belongs to
        another kind is rejected by name rather than ignored; the queries option's description is the JSON Schema for the batch.
        Per-entity kinds target either one named entity or, using a health filter (e.g. only unhealthy entities), every
        matching entity resolved from a single shared entity list. The queries are planned into
        the fewest Azure Resource Manager calls (grouped by model, deduplicated). Payloads are compact by default and callers
        can opt into per-kind selections or full SDK fidelity. Each result carries a uniform list of
        per-entity nodes (for example
        entity.properties.healthState, history.history[], signalHistory.history[], recommendations.recommendedSignals[],
        annotations.annotations[]), or, for the model-scope lists, relationships[] and signalDefinitions[].
        API-native pagination returns one page per request with page.complete, page.returnedCount, and the exact page.cursor
        needed to resume. A whole-query failure sets the query's success to
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

        if (!HealthModelQueryParser.TryParse(options.Queries, out _, out var error))
        {
            validationResult.Errors.Add(error);
        }
    }

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, HealthModelQueryOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (!HealthModelQueryParser.TryParse(options.Queries, out var queries, out var error))
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

}

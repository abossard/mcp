// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Query Azure Monitor Health Models in one of two read-only modes.
        Use --queries for typed batch queries (schema-driven JSON, one result per queryIndex).
        Use --code for JavaScript read-code mode in a read-only sandbox with Promise-based Health Models APIs.
        The read-code mode exposes only read operations, supports Promise.all overlap with a four-request gate,
        and enforces sandbox limits (30s timeout, max depth 64, max 5,000,000 statements, max result size 24,000 chars).
        Both modes preserve command cancellation and return structured per-call failures.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class HealthModelQueryCommand(IMonitorHealthModelService healthModelService, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<HealthModelQueryOptions, JsonNode>(subscriptionResolver)
{
    private readonly IMonitorHealthModelService _healthModelService = healthModelService;

    public override void ValidateOptions(HealthModelQueryOptions options, ValidationResult validationResult)
    {
        base.ValidateOptions(options, validationResult);

        if (!validationResult.IsValid)
        {
            return;
        }

        var hasQueries = options.Queries is not null;
        var hasCode = options.Code is not null;

        if (hasQueries == hasCode || string.IsNullOrWhiteSpace(options.Queries ?? options.Code))
        {
            validationResult.Errors.Add("Provide exactly one of code or queries.");
            return;
        }

        if (hasQueries && !HealthModelQueryParser.TryParse(options.Queries!, out _, out var error))
        {
            validationResult.Errors.Add(error);
        }
    }

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, HealthModelQueryOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(options.Code))
            {
                var scriptResult = await _healthModelService.ExecuteHealthModelReadCode(
                    options.Subscription!,
                    options.Code,
                    options.Tenant,
                    options.RetryPolicy,
                    cancellationToken);

                context.Response.Results = ResponseResult.Create(
                    scriptResult,
                    MonitorJsonContext.Default.HealthModelScriptResult);

                return context.Response;
            }

            if (!HealthModelQueryParser.TryParse(options.Queries!, out var queries, out var error))
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

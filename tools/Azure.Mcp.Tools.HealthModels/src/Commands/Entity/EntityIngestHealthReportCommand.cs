// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.Entity;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.Entity;

[CommandMetadata(
    Id = "d63b8ba8-b78d-422f-a24c-74827faa6922",
    Name = "ingest-health-report",
    Title = "Ingest Entity Health Report",
    Description = """
        Ingest Entity Health Report.
        """,
    Destructive = false,
    Idempotent = false,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class EntityIngestHealthReportCommand(ILogger<EntityIngestHealthReportCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<EntityIngestHealthReportOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<EntityIngestHealthReportCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, EntityIngestHealthReportOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.IngestEntityHealthReportAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Entity, options.SignalName, options.HealthState, options.Value, options.ExpiresInMinutes, options.AdditionalContext, options.Tenant, options.RetryPolicy, cancellationToken);
            context.Response.Results = ResponseResult.Create(new HealthModelsItemResult(result), HealthModelsJsonContext.Default.HealthModelsItemResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {Command}.", Name);
            HandleException(context, ex);
        }

        return context.Response;
    }
}

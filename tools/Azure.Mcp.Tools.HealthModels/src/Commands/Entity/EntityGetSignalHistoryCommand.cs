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
    Id = "eb7bf735-79a7-4fae-8381-b6b6a870caf8",
    Name = "get-signal-history",
    Title = "Get Entity Signal History",
    Description = """
        Get Entity Signal History.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class EntityGetSignalHistoryCommand(ILogger<EntityGetSignalHistoryCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<EntityGetSignalHistoryOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<EntityGetSignalHistoryCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, EntityGetSignalHistoryOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetEntitySignalHistoryAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Entity, options.SignalName, options.StartTime, options.EndTime, options.Top, options.Tenant, options.RetryPolicy, cancellationToken);
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

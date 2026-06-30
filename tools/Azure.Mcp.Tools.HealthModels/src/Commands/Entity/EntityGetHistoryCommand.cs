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
    Id = "d90b55af-9c3c-4182-b5d7-5330f27b1c08",
    Name = "get-history",
    Title = "Get Entity Health History",
    Description = """
        Get Entity Health History.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class EntityGetHistoryCommand(ILogger<EntityGetHistoryCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<EntityGetHistoryOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<EntityGetHistoryCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, EntityGetHistoryOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetEntityHistoryAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Entity, options.StartTime, options.EndTime, options.Top, options.Tenant, options.RetryPolicy, cancellationToken);
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

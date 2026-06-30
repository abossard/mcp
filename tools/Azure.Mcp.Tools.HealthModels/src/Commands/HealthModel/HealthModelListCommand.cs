// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.HealthModel;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.HealthModel;

[CommandMetadata(
    Id = "86a3ceab-34ab-4ab5-b178-e9ec596d3ad2",
    Name = "list",
    Title = "List Health Models",
    Description = """
        List Health Models.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class HealthModelListCommand(ILogger<HealthModelListCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<HealthModelListOptions, HealthModelsListResult>(subscriptionResolver)
{
    private readonly ILogger<HealthModelListCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, HealthModelListOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var items = await _service.ListHealthModelsAsync(options.Subscription!, options.ResourceGroup, options.Tenant, options.RetryPolicy, cancellationToken);
            context.Response.Results = ResponseResult.Create(new HealthModelsListResult(items, items.Count), HealthModelsJsonContext.Default.HealthModelsListResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {Command}.", Name);
            HandleException(context, ex);
        }

        return context.Response;
    }
}

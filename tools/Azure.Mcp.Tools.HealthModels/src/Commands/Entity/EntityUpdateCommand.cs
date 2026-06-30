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
    Id = "02baa35e-2e99-4584-b0a1-2c9fc6f7597a",
    Name = "update",
    Title = "Update Entity",
    Description = """
        Update Entity.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class EntityUpdateCommand(ILogger<EntityUpdateCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<EntityUpdateOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<EntityUpdateCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, EntityUpdateOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CreateOrUpdateEntityAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Entity, options.Properties, options.Tenant, options.RetryPolicy, cancellationToken);
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

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
    Id = "60c546ed-df30-4d7e-91cb-b8084d1410ae",
    Name = "get-data-annotations",
    Title = "Get Entity Data Annotations",
    Description = """
        Get Entity Data Annotations.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class EntityGetDataAnnotationsCommand(ILogger<EntityGetDataAnnotationsCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<EntityGetDataAnnotationsOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<EntityGetDataAnnotationsCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, EntityGetDataAnnotationsOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetEntityDataAnnotationsAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Entity, options.StartTime, options.EndTime, options.Top, options.Tenant, options.RetryPolicy, cancellationToken);
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

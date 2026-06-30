// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.SignalDefinition;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.SignalDefinition;

[CommandMetadata(
    Id = "3ffb3d8f-26ef-432e-8320-c4f90bfe18dd",
    Name = "update",
    Title = "Update Signal Definition",
    Description = """
        Update Signal Definition.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class SignalDefinitionUpdateCommand(ILogger<SignalDefinitionUpdateCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<SignalDefinitionUpdateOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<SignalDefinitionUpdateCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, SignalDefinitionUpdateOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CreateOrUpdateSignalDefinitionAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.SignalDefinition, options.Properties, options.Tenant, options.RetryPolicy, cancellationToken);
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

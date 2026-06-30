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
    Id = "6ec5eed9-8ed9-4cb7-baf8-a4afff4f1188",
    Name = "delete",
    Title = "Delete Signal Definition",
    Description = """
        Delete Signal Definition.
        """,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class SignalDefinitionDeleteCommand(ILogger<SignalDefinitionDeleteCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<SignalDefinitionDeleteOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<SignalDefinitionDeleteCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, SignalDefinitionDeleteOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.DeleteSignalDefinitionAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.SignalDefinition, options.Tenant, options.RetryPolicy, cancellationToken);
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

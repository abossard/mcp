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
    Id = "92ca28af-226c-452c-8d9b-db2ca4c62762",
    Name = "list",
    Title = "List Signal Definitions",
    Description = """
        List Signal Definitions.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class SignalDefinitionListCommand(ILogger<SignalDefinitionListCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<SignalDefinitionListOptions, HealthModelsListResult>(subscriptionResolver)
{
    private readonly ILogger<SignalDefinitionListCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, SignalDefinitionListOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var items = await _service.ListSignalDefinitionsAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Tenant, options.RetryPolicy, cancellationToken);
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

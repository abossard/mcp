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
    Id = "f2eccf6a-7363-43ab-ad24-607c38a9a207",
    Name = "get",
    Title = "Get Signal Definition",
    Description = """
        Get Signal Definition.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class SignalDefinitionGetCommand(ILogger<SignalDefinitionGetCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<SignalDefinitionGetOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<SignalDefinitionGetCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, SignalDefinitionGetOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetSignalDefinitionAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.SignalDefinition, options.Tenant, options.RetryPolicy, cancellationToken);
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

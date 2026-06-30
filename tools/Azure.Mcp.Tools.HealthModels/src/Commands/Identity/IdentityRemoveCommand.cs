// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.Identity;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.Identity;

[CommandMetadata(
    Id = "fb68e3ed-e8c5-4f94-a18a-527f46708f18",
    Name = "remove",
    Title = "Remove Health Model Identity",
    Description = """
        Remove Health Model Identity.
        """,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class IdentityRemoveCommand(ILogger<IdentityRemoveCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<IdentityRemoveOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<IdentityRemoveCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, IdentityRemoveOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RemoveIdentityAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Tenant, options.RetryPolicy, cancellationToken);
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

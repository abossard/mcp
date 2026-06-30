// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.DiscoveryRule;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.DiscoveryRule;

[CommandMetadata(
    Id = "763866e5-1ef1-4891-b5a2-997e8ed3f943",
    Name = "create",
    Title = "Create Discovery Rule",
    Description = """
        Create Discovery Rule.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class DiscoveryRuleCreateCommand(ILogger<DiscoveryRuleCreateCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<DiscoveryRuleCreateOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<DiscoveryRuleCreateCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, DiscoveryRuleCreateOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CreateOrUpdateDiscoveryRuleAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.DiscoveryRule, options.Properties, options.Tenant, options.RetryPolicy, cancellationToken);
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

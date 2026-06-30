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
    Id = "69028de9-faed-4b91-8182-6c459e300790",
    Name = "delete",
    Title = "Delete Discovery Rule",
    Description = """
        Delete Discovery Rule.
        """,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class DiscoveryRuleDeleteCommand(ILogger<DiscoveryRuleDeleteCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<DiscoveryRuleDeleteOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<DiscoveryRuleDeleteCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, DiscoveryRuleDeleteOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.DeleteDiscoveryRuleAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.DiscoveryRule, options.Tenant, options.RetryPolicy, cancellationToken);
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

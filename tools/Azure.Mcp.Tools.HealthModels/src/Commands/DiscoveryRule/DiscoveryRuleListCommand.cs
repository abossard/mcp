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
    Id = "c1f26ec8-b2b0-4065-95db-93d3a36dc388",
    Name = "list",
    Title = "List Discovery Rules",
    Description = """
        List Discovery Rules.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class DiscoveryRuleListCommand(ILogger<DiscoveryRuleListCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<DiscoveryRuleListOptions, HealthModelsListResult>(subscriptionResolver)
{
    private readonly ILogger<DiscoveryRuleListCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, DiscoveryRuleListOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var items = await _service.ListDiscoveryRulesAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Tenant, options.RetryPolicy, cancellationToken);
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

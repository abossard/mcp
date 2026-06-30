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
    Id = "ec81444a-e10f-49d5-870d-4460b55f0056",
    Name = "get",
    Title = "Get Discovery Rule",
    Description = """
        Get Discovery Rule.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class DiscoveryRuleGetCommand(ILogger<DiscoveryRuleGetCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<DiscoveryRuleGetOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<DiscoveryRuleGetCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, DiscoveryRuleGetOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetDiscoveryRuleAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.DiscoveryRule, options.Tenant, options.RetryPolicy, cancellationToken);
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

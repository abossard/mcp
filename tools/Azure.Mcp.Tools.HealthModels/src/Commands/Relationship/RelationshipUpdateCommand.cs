// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.Relationship;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.Relationship;

[CommandMetadata(
    Id = "4dd64967-ff40-4e17-8574-72c51de325c2",
    Name = "update",
    Title = "Update Relationship",
    Description = """
        Update Relationship.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class RelationshipUpdateCommand(ILogger<RelationshipUpdateCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<RelationshipUpdateOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<RelationshipUpdateCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, RelationshipUpdateOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CreateOrUpdateRelationshipAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Relationship, options.Properties, options.Tenant, options.RetryPolicy, cancellationToken);
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

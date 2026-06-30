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
    Id = "7e7b3ede-6fba-472f-b46c-b82b4b5b1620",
    Name = "delete",
    Title = "Delete Relationship",
    Description = """
        Delete Relationship.
        """,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class RelationshipDeleteCommand(ILogger<RelationshipDeleteCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<RelationshipDeleteOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<RelationshipDeleteCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, RelationshipDeleteOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.DeleteRelationshipAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Relationship, options.Tenant, options.RetryPolicy, cancellationToken);
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

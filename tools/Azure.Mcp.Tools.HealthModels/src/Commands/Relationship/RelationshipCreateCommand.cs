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
    Id = "6d482a10-be60-4bf3-a31b-323017553208",
    Name = "create",
    Title = "Create Relationship",
    Description = """
        Create Relationship.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class RelationshipCreateCommand(ILogger<RelationshipCreateCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<RelationshipCreateOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<RelationshipCreateCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, RelationshipCreateOptions options, CancellationToken cancellationToken)
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

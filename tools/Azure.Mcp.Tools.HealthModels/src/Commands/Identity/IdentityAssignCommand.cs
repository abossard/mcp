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
    Id = "67f0d9da-6b50-4654-8db9-3205d47b9715",
    Name = "assign",
    Title = "Assign Health Model Identity",
    Description = """
        Assign Health Model Identity.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class IdentityAssignCommand(ILogger<IdentityAssignCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<IdentityAssignOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<IdentityAssignCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, IdentityAssignOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.AssignIdentityAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.IdentityType, HealthModelsInput.ParseList(options.UserAssignedIdentities), options.Tenant, options.RetryPolicy, cancellationToken);
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

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.HealthModel;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.HealthModel;

[CommandMetadata(
    Id = "c5446363-bb74-4be0-b50b-f63620326553",
    Name = "create",
    Title = "Create Health Model",
    Description = """
        Create Health Model.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class HealthModelCreateCommand(ILogger<HealthModelCreateCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<HealthModelCreateOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<HealthModelCreateCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, HealthModelCreateOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CreateHealthModelAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Location, HealthModelsInput.ParseDictionary(options.Tags), options.Tenant, options.RetryPolicy, cancellationToken);
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

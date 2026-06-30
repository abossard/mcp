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
    Id = "92f47074-9fce-40ca-ac09-9d97610cb76c",
    Name = "update",
    Title = "Update Health Model",
    Description = """
        Update Health Model.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class HealthModelUpdateCommand(ILogger<HealthModelUpdateCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<HealthModelUpdateOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<HealthModelUpdateCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, HealthModelUpdateOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.UpdateHealthModelAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, HealthModelsInput.ParseDictionary(options.Tags), options.Tenant, options.RetryPolicy, cancellationToken);
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

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
    Id = "df6a0adc-b662-45a7-927e-f4c6752db04c",
    Name = "delete",
    Title = "Delete Health Model",
    Description = """
        Delete Health Model.
        """,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class HealthModelDeleteCommand(ILogger<HealthModelDeleteCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<HealthModelDeleteOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<HealthModelDeleteCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, HealthModelDeleteOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.DeleteHealthModelAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Tenant, options.RetryPolicy, cancellationToken);
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

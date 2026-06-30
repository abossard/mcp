// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.AuthenticationSetting;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.AuthenticationSetting;

[CommandMetadata(
    Id = "bf0eaec8-a848-482c-aca1-77115649ee8f",
    Name = "delete",
    Title = "Delete Authentication Setting",
    Description = """
        Delete Authentication Setting.
        """,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class AuthenticationSettingDeleteCommand(ILogger<AuthenticationSettingDeleteCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<AuthenticationSettingDeleteOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<AuthenticationSettingDeleteCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, AuthenticationSettingDeleteOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.DeleteAuthenticationSettingAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.AuthenticationSetting, options.Tenant, options.RetryPolicy, cancellationToken);
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

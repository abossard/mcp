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
    Id = "3cd1c4d1-85a6-4c64-91c7-e2abc1add0b6",
    Name = "get",
    Title = "Get Authentication Setting",
    Description = """
        Get Authentication Setting.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class AuthenticationSettingGetCommand(ILogger<AuthenticationSettingGetCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<AuthenticationSettingGetOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<AuthenticationSettingGetCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, AuthenticationSettingGetOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetAuthenticationSettingAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.AuthenticationSetting, options.Tenant, options.RetryPolicy, cancellationToken);
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

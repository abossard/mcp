// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.HealthModels.Models;
using Azure.Mcp.Tools.HealthModels.Options.Entity;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.HealthModels.Commands.Entity;

[CommandMetadata(
    Id = "21c167cd-c9d7-4b24-a90a-d39ebbcf19b1",
    Name = "add-data-annotation",
    Title = "Add Entity Data Annotation",
    Description = """
        Add Entity Data Annotation.
        """,
    Destructive = false,
    Idempotent = false,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class EntityAddDataAnnotationCommand(ILogger<EntityAddDataAnnotationCommand> logger, IHealthModelsService service, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<EntityAddDataAnnotationOptions, HealthModelsItemResult>(subscriptionResolver)
{
    private readonly ILogger<EntityAddDataAnnotationCommand> _logger = logger;
    private readonly IHealthModelsService _service = service;

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, EntityAddDataAnnotationOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.AddEntityDataAnnotationAsync(options.Subscription!, options.ResourceGroup, options.HealthModel, options.Entity, HealthModelsInput.ParseDictionary(options.AnnotationDetails) ?? new Dictionary<string, string>(), options.Description, options.Tenant, options.RetryPolicy, cancellationToken);
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

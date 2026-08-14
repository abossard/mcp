// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Options.HealthModels;
using Azure.Mcp.Tools.Monitor.Services;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

[CommandMetadata(
    Id = "9c4a1e57-2db8-4f36-8a90-6be03d7c1f42",
    Name = "sdk",
    Title = "Run JavaScript against the Azure Monitor Health Model SDK (experimental)",
    Description = """
        EXPERIMENTAL: Run caller-authored JavaScript in a sandbox where the authenticated Azure CloudHealth
        SDK (Microsoft.CloudHealth/healthmodels) is bound to the target subscription as a client object,
        and get back only what the script returns plus whatever it logged. Use this instead of a batch of
        individual calls when an answer needs chaining, filtering, aggregation, or a loop over a model's
        graph: intermediate pages stay inside the sandbox and never enter the caller's context, so a scan
        of thousands of entities can return a single count. The client mirrors @azure/arm-cloudhealth's
        CloudHealthClient — client.healthModels, client.entities, client.relationships and
        client.signalDefinitions, with the SDK's own method names and argument order — and the code
        option's description is its TypeScript declaration. Read operations cover get, list, health and
        signal history, signal recommendations and data annotations; write operations cover createOrUpdate,
        delete, addDataAnnotation and ingestHealthReport, and take effect immediately with no whatIf
        preview. Pagination is API-native: one Azure page per call, with the service's own nextLink echoed
        back as options.cursor to continue, never silently aggregated. The sandbox has no network, no
        filesystem, no module loading, and no route to a .NET type or to the credential the client uses, so
        a script cannot reach anything the client does not expose. Execution is bounded by a wall-clock
        timeout, a statement ceiling and a recursion limit; a script that exceeds one is reported as an
        error rather than a partial result, and an oversized result is truncated with its estimated size.
        """,
    Destructive = true,
    Idempotent = false,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class HealthModelSdkCommand(
    IMonitorHealthModelService healthModelService, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<HealthModelSdkOptions, HealthModelScriptResult>(subscriptionResolver)
{
    private readonly IMonitorHealthModelService _healthModelService = healthModelService;

    public override void ValidateOptions(HealthModelSdkOptions options, ValidationResult validationResult)
    {
        base.ValidateOptions(options, validationResult);

        if (validationResult.IsValid && string.IsNullOrWhiteSpace(options.Code))
        {
            validationResult.Errors.Add("The code option must contain the JavaScript to run.");
        }
    }

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context, HealthModelSdkOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(options.Code))
            {
                throw new ArgumentException("The code option must contain the JavaScript to run.", nameof(options));
            }

            var result = await _healthModelService.ExecuteHealthModelScript(
                options.Subscription!,
                options.Code,
                options.Tenant,
                options.RetryPolicy,
                cancellationToken);

            context.Response.Results = ResponseResult.Create(
                result, MonitorJsonContext.Default.HealthModelScriptResult);
        }
        catch (Exception ex)
        {
            HandleException(context, ex);
        }

        return context.Response;
    }

    protected override HttpStatusCode GetStatusCode(Exception ex) => ex switch
    {
        ArgumentException => HttpStatusCode.BadRequest,
        _ => base.GetStatusCode(ex),
    };
}

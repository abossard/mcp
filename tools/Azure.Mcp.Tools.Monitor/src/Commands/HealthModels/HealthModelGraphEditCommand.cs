// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Options.HealthModels;
using Azure.Mcp.Tools.Monitor.Services;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

[CommandMetadata(
    Id = "fe613806-7add-440c-adba-8d87fea9c6e0",
    Name = "graphedit",
    Title = "Edit an Azure Monitor Health Model graph (experimental)",
    Description = """
        EXPERIMENTAL: Apply a batch of typed changes to an Azure Monitor Health Model's graph
        (Microsoft.CloudHealth/healthmodels) — its entities, the parent/child relationships between them, and its signal
        definitions — in a single call, with one result per input change element returned in input order and correlated by
        a system-assigned zero-based changeIndex. Supported change kinds are create (a full-body upsert of one named
        resource), patch (an RFC 7386 merge patch applied to every selected resource, where a null value removes a property
        and an array replaces wholesale), rename (which expands into a create under the new name, a repoint of every
        relationship that referenced the old name, and a delete of the old name) and delete. Each kind is its own closed
        shape carrying exactly the inputs it accepts, so an input that belongs to another kind is rejected by name rather
        than ignored; the changes option's description is the JSON Schema for the batch. Targets are chosen by exact-match
        selectors over a fully enumerated snapshot of the model, never by substring or pattern, and each result reports the
        resolved targets with a per-property changes[] of {path, before, after} and an action of create, update, replace,
        delete, noOp or skipped. The default mode is whatIf, which computes the whole change set and writes NOTHING;
        writing requires mode 'apply' together with an --expect affectedCount that equals the number of targets the change
        set affects, so a change set can only be applied by a caller that read its preview. Writes are ordered signal
        definitions, then entities, then relationships (deletes in the reverse order) and a target whose dependency failed
        is reported as skipped rather than attempted. Server-owned properties (provisioningState, healthState, discoveredBy,
        systemData, id, name, type) are rejected as input and excluded from every diff. A whole-element failure sets that
        element's success to false without affecting its siblings; a failed guard rejects the entire batch with no writes.
        """,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = false,
    Secret = false,
    LocalRequired = false)]
public sealed class HealthModelGraphEditCommand(
    IMonitorHealthModelService healthModelService, ISubscriptionResolver subscriptionResolver)
    : SubscriptionCommand<HealthModelGraphEditOptions, HealthModelGraphEditResult>(subscriptionResolver)
{
    private readonly IMonitorHealthModelService _healthModelService = healthModelService;

    public override void ValidateOptions(HealthModelGraphEditOptions options, ValidationResult validationResult)
    {
        base.ValidateOptions(options, validationResult);

        if (!validationResult.IsValid)
        {
            return;
        }

        if (!HealthModelChangeParser.TryParse(options.Changes, out _, out var changesError))
        {
            validationResult.Errors.Add(changesError);
        }

        if (!HealthModelGraphEditInputs.TryParseMode(options.Mode, out _, out var modeError))
        {
            validationResult.Errors.Add(modeError);
        }

        if (!HealthModelGraphEditInputs.TryParseExpectation(options.Expect, out _, out var expectError))
        {
            validationResult.Errors.Add(expectError);
        }
    }

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context, HealthModelGraphEditOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (!HealthModelChangeParser.TryParse(options.Changes, out var changes, out var changesError))
            {
                throw new ArgumentException(changesError, nameof(options));
            }

            if (!HealthModelGraphEditInputs.TryParseMode(options.Mode, out var mode, out var modeError))
            {
                throw new ArgumentException(modeError, nameof(options));
            }

            if (!HealthModelGraphEditInputs.TryParseExpectation(options.Expect, out var expect, out var expectError))
            {
                throw new ArgumentException(expectError, nameof(options));
            }

            var result = await _healthModelService.ExecuteHealthModelGraphEdit(
                options.Subscription!,
                changes,
                mode,
                expect,
                options.Tenant,
                options.RetryPolicy,
                cancellationToken);

            // Results are returned one-per-input-element in input order (indexed by changeIndex), so the
            // optional echo-only label is re-attached by input position — it never influenced planning.
            for (var index = 0; index < result.Changes.Count; index++)
            {
                result.Changes[index].Label = changes[index].Label;
            }

            context.Response.Results = ResponseResult.Create(
                result, MonitorJsonContext.Default.HealthModelGraphEditResult);
        }
        catch (Exception ex)
        {
            HandleException(context, ex);
        }

        return context.Response;
    }

    protected override HttpStatusCode GetStatusCode(Exception ex) => ex switch
    {
        JsonException => HttpStatusCode.BadRequest,
        ArgumentException => HttpStatusCode.BadRequest,
        _ => base.GetStatusCode(ex),
    };
}

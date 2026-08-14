// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.Mcp.Tests.Commands;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Services;
using Microsoft.Mcp.Core.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

public class HealthModelGraphEditCommandTests
    : SubscriptionCommandUnitTestsBase<HealthModelGraphEditCommand, IMonitorHealthModelService>
{
    private const string TestSubscription = "sub123";

    // Four elements: two share the label "dup", two have none. Correlation is the zero-based input position.
    private const string FourChangesJson = """
        [
          {"kind":"create","resourceGroup":"rg1","healthModel":"hm1","name":"web","resource":{"entity":{"properties":{"displayName":"Web"}}},"label":"dup"},
          {"kind":"patch","resourceGroup":"rg1","healthModel":"hm1","select":{"entity":{"names":["api"]}},"patch":{"properties":{"displayName":"API"}},"label":"dup"},
          {"kind":"rename","resourceGroup":"rg1","healthModel":"hm1","select":{"entity":{"names":["db"]}},"newName":"database"},
          {"kind":"delete","resourceGroup":"rg1","healthModel":"hm1","select":{"signalDefinition":{"names":["cpu"]}}}
        ]
        """;

    private void EchoServiceResults() =>
        Service.ExecuteHealthModelGraphEdit(
            TestSubscription,
            Arg.Any<IReadOnlyList<HealthModelChange>>(),
            Arg.Any<HealthModelChangeMode>(),
            Arg.Any<HealthModelChangeExpectation?>(),
            Arg.Any<string?>(),
            Arg.Any<RetryPolicyOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var changes = ci.Arg<IReadOnlyList<HealthModelChange>>();
                // The executor produces one result per input in input order; labels are attached by the command.
                return new HealthModelGraphEditResult
                {
                    Mode = ci.Arg<HealthModelChangeMode>(),
                    Success = true,
                    AffectedCount = changes.Count,
                    Changes = [.. changes.Select((c, i) => new HealthModelChangeResult
                    {
                        ChangeIndex = i,
                        Kind = c.Kind,
                        Success = true,
                    })],
                };
            });

    [Fact]
    public async Task ExecuteAsync_EchoesLabelsByPosition_AndPreservesInputOrder()
    {
        EchoServiceResults();

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--changes", FourChangesJson);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(response, MonitorJsonContext.Default.HealthModelGraphEditResult);

        // G5: results are in input order, correlated by a system-assigned zero-based changeIndex.
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Changes.Select(c => c.ChangeIndex));
        Assert.Equal(new[] { "create", "patch", "rename", "delete" }, result.Changes.Select(c => c.Kind));
        // G5: duplicate labels are accepted verbatim and an absent label stays null.
        Assert.Equal(new[] { "dup", "dup", null, null }, result.Changes.Select(c => c.Label));

        await Service.Received(1).ExecuteHealthModelGraphEdit(
            TestSubscription,
            Arg.Is<IReadOnlyList<HealthModelChange>>(c =>
                c.Count == 4 &&
                c[0] is CreateChange && ((CreateChange)c[0]).Name == "web" && c[0].Label == "dup" &&
                c[1] is PatchChange && c[2] is RenameChange && ((RenameChange)c[2]).NewName == "database" &&
                c[3] is DeleteChange && c[3].Label == null),
            HealthModelChangeMode.WhatIf,
            null,
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, HealthModelChangeMode.WhatIf)]
    [InlineData("whatIf", HealthModelChangeMode.WhatIf)]
    [InlineData("WHATIF", HealthModelChangeMode.WhatIf)]
    [InlineData("apply", HealthModelChangeMode.Apply)]
    [InlineData("Apply", HealthModelChangeMode.Apply)]
    public async Task ExecuteAsync_DefaultsToWhatIf_AndForwardsTheRequestedMode(string? mode, HealthModelChangeMode expected)
    {
        EchoServiceResults();

        string[] args = mode is null
            ? ["--subscription", TestSubscription, "--changes", FourChangesJson]
            : ["--subscription", TestSubscription, "--changes", FourChangesJson, "--mode", mode];
        var response = await ExecuteCommandAsync(args);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).ExecuteHealthModelGraphEdit(
            TestSubscription, Arg.Any<IReadOnlyList<HealthModelChange>>(), expected, Arg.Any<HealthModelChangeExpectation?>(),
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsTheDeclaredExpectation()
    {
        EchoServiceResults();

        var response = await ExecuteCommandAsync(
            "--subscription", TestSubscription, "--changes", FourChangesJson,
            "--mode", "apply", "--expect", """{"affectedCount":7,"snapshot":"abc123"}""");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).ExecuteHealthModelGraphEdit(
            TestSubscription, Arg.Any<IReadOnlyList<HealthModelChange>>(), HealthModelChangeMode.Apply,
            Arg.Is<HealthModelChangeExpectation?>(e => e!.AffectedCount == 7 && e.Snapshot == "abc123"),
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_IsolatesAMalformedElement_AtTheCommandBoundary()
    {
        EchoServiceResults();

        // One unreadable element among three (a patch carrying a create-only input). Only its own slot fails.
        const string json = """
            [
              {"kind":"create","resourceGroup":"rg1","healthModel":"hm1","name":"web","resource":{"entity":{"properties":{}}}},
              {"kind":"patch","resourceGroup":"rg1","healthModel":"hm1","select":{"entity":{"all":true}},"patch":{},"name":"web"},
              {"kind":"delete","resourceGroup":"rg1","healthModel":"hm1","select":{"entity":{"names":["db"]}}}
            ]
            """;

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--changes", json);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).ExecuteHealthModelGraphEdit(
            TestSubscription,
            Arg.Is<IReadOnlyList<HealthModelChange>>(c =>
                c.Count == 3 && c[0] is CreateChange && c[1] is MalformedChange && c[2] is DeleteChange),
            Arg.Any<HealthModelChangeMode>(), Arg.Any<HealthModelChangeExpectation?>(),
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WritesTheBatchEnvelopeDirectlyToResults()
    {
        EchoServiceResults();

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--changes", FourChangesJson);

        var json = JsonSerializer.Serialize(response.Results);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("whatIf", root.GetProperty("mode").GetString());
        Assert.Equal(4, root.GetProperty("affectedCount").GetInt32());
        Assert.Equal(4, root.GetProperty("changes").GetArrayLength());
        Assert.Equal("dup", root.GetProperty("changes")[0].GetProperty("label").GetString());
        Assert.False(root.GetProperty("changes")[2].TryGetProperty("label", out _));
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_ReturnsExceptionObject()
    {
        Service.ExecuteHealthModelGraphEdit(
            TestSubscription, Arg.Any<IReadOnlyList<HealthModelChange>>(), Arg.Any<HealthModelChangeMode>(),
            Arg.Any<HealthModelChangeExpectation?>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Test error"));

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--changes", FourChangesJson);

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        var json = JsonSerializer.Serialize(response.Results);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Test error", document.RootElement.GetProperty("message").GetString());
    }

    /// <summary>
    /// G27: the page cap protects the selector from running against a partial view. It is not an internal
    /// fault, so it must not come back as a 500 that a caller can only read as "try again".
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTheModelCouldNotBeReadWhole_ReportsA4xxNamingThePartialView()
    {
        const string Message =
            "Reading Entity in 'hm1' did not finish within 100 pages; the selector would have run against a partial view.";

        Service.ExecuteHealthModelGraphEdit(
            TestSubscription, Arg.Any<IReadOnlyList<HealthModelChange>>(), Arg.Any<HealthModelChangeMode>(),
            Arg.Any<HealthModelChangeExpectation?>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(Message));

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--changes", FourChangesJson);

        // 422, from the base mapping of InvalidOperationException — pinned so a future exception-type change
        // cannot silently turn this into a 500.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
        var json = JsonSerializer.Serialize(response.Results);
        using var document = JsonDocument.Parse(json);
        Assert.Contains("partial view", document.RootElement.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json", null, null)]
    [InlineData("[]", null, null)]
    [InlineData("""{"kind":"delete","resourceGroup":"rg1","healthModel":"hm1","select":{"entity":{"all":true}}}""", null, null)]
    [InlineData(null, "dryRun", null)]
    [InlineData(null, null, "not-json")]
    [InlineData(null, null, "[1]")]
    public async Task ExecuteAsync_ReturnsBadRequest_ForInvalidInput(string? changes, string? mode, string? expect)
    {
        List<string> args = ["--subscription", TestSubscription, "--changes", changes ?? FourChangesJson];
        if (mode is not null)
        {
            args.AddRange("--mode", mode);
        }

        if (expect is not null)
        {
            args.AddRange("--expect", expect);
        }

        var response = await ExecuteCommandAsync([.. args]);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        await Service.DidNotReceive().ExecuteHealthModelGraphEdit(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<HealthModelChange>>(), Arg.Any<HealthModelChangeMode>(),
            Arg.Any<HealthModelChangeExpectation?>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsBadRequest_WhenChangesMissing()
    {
        var response = await ExecuteCommandAsync("--subscription", TestSubscription);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        await Service.DidNotReceive().ExecuteHealthModelGraphEdit(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<HealthModelChange>>(), Arg.Any<HealthModelChangeMode>(),
            Arg.Any<HealthModelChangeExpectation?>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Command_DeclaresItselfExperimentalAndDestructive()
    {
        // G17: the only experimental marking is the description prefix and the title suffix.
        Assert.StartsWith("EXPERIMENTAL:", Command.Description);
        Assert.Contains("(experimental)", Command.Title);
        Assert.Equal("graphedit", Command.Name);

        // G2: a write command is destructive, not read-only, and idempotent (a repeated apply converges).
        Assert.True(Command.Metadata.Destructive);
        Assert.False(Command.Metadata.ReadOnly);
        Assert.True(Command.Metadata.Idempotent);
        Assert.False(Command.Metadata.OpenWorld);
    }

    [Fact]
    public void Command_PublishesTheChangeSchemaAsTheChangesOptionDescription()
    {
        // G4: the schema is the option description, so a caller reads it from --help.
        var changes = CommandDefinition.Options.Single(o => o.Name == "--changes");

        Assert.Equal(HealthModelGraphEditSchema.Schema, changes.Description);
        Assert.Contains("\"const\":\"rename\"", changes.Description);
    }
}

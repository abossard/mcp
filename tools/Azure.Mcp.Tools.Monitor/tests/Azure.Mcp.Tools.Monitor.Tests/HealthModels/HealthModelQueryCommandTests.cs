// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.Mcp.Tests.Commands;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;
using Azure.Mcp.Tools.Monitor.Services;
using Microsoft.Mcp.Core.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

public class HealthModelQueryCommandTests : SubscriptionCommandUnitTestsBase<HealthModelQueryCommand, IMonitorHealthModelService>
{
    private const string TestSubscription = "sub123";

    // No caller id; the second query has no label. Correlation is the zero-based input position.
    private const string TwoQueriesJson = """
        [
          {"kind":"entityList","resourceGroup":"rg1","healthModel":"hm1","label":"first"},
          {"kind":"entityGet","resourceGroup":"rg1","healthModel":"hm1","entity":"e1"}
        ]
        """;

    private void EchoServiceResults() =>
        Service.ExecuteHealthModelQueries(TestSubscription, Arg.Any<IReadOnlyList<HealthModelQuery>>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var queries = ci.Arg<IReadOnlyList<HealthModelQuery>>();
                // The executor produces one result per input in input order; labels are attached by the command.
                return queries.Select((q, i) => new HealthModelQueryResult { QueryIndex = i, Kind = q.Kind.ToString(), Success = true }).ToList();
            });

    [Fact]
    public async Task ExecuteAsync_AcceptsBatchWithoutIds_EchoesLabels_AndPreservesInputOrder()
    {
        EchoServiceResults();

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--queries", TwoQueriesJson);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(response, MonitorJsonContext.Default.ListHealthModelQueryResult);

        // H3: results are in input order, correlated by a system-assigned zero-based queryIndex.
        Assert.Equal(new[] { 0, 1 }, result.Select(r => r.QueryIndex));
        // H2: the label is echoed verbatim on its result; an absent label stays null.
        Assert.Equal("first", result[0].Label);
        Assert.Null(result[1].Label);

        // H1: an id-less batch validates and both typed queries (with their labels) are forwarded to the service.
        await Service.Received(1).ExecuteHealthModelQueries(
            TestSubscription,
            Arg.Is<IReadOnlyList<HealthModelQuery>>(q => q.Count == 2 && q[0].Label == "first" && q[0] is EntityListQuery && ((EntityGetQuery)q[1]).Entity == "e1" && q[1].Label == null),
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsDuplicateLabels_AbsentLabels_AndDuplicateIdenticalQueries()
    {
        EchoServiceResults();

        // Two identical "dup" labels, two absent labels, and two byte-identical queries (indexes 2 and 3).
        const string json = """
            [
              {"kind":"entityList","resourceGroup":"rg1","healthModel":"hm1","label":"dup"},
              {"kind":"entityGet","resourceGroup":"rg1","healthModel":"hm1","entity":"e1","label":"dup"},
              {"kind":"entityList","resourceGroup":"rg1","healthModel":"hm1"},
              {"kind":"entityList","resourceGroup":"rg1","healthModel":"hm1"}
            ]
            """;

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--queries", json);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(response, MonitorJsonContext.Default.ListHealthModelQueryResult);
        Assert.Equal(new[] { "dup", "dup", null, null }, result.Select(r => r.Label));
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Select(r => r.QueryIndex));
    }

    [Fact]
    public async Task ExecuteAsync_ParsesTypedSelectionsAndOpaqueCursors()
    {
        EchoServiceResults();
        const string json = """
            [
              {"kind":"entityList","resourceGroup":"rg1","healthModel":"hm1","page":{"cursor":"list/+=="},"select":["identity","audit"]},
              {"kind":"entityHistory","resourceGroup":"rg1","healthModel":"hm1","target":{"entity":"e1"},"page":{"cursor":"entity/+==","size":25},"select":["full"]}
            ]
            """;

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--queries", json);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).ExecuteHealthModelQueries(
            TestSubscription,
            Arg.Is<IReadOnlyList<HealthModelQuery>>(queries =>
                ((EntityListQuery)queries[0]).Page!.Cursor == "list/+==" &&
                ((EntityListQuery)queries[0]).Select!.SequenceEqual(new[] { EntitySelection.Identity, EntitySelection.Audit }) &&
                ((EntityHistoryQuery)queries[1]).Page!.Cursor == "entity/+==" &&
                ((EntityHistoryQuery)queries[1]).Page!.Size == 25 &&
                ((EntityHistoryQuery)queries[1]).Select!.SequenceEqual(new[] { FullSelection.Full })),
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SerializesMachineReadablePageMetadataWithoutFabricatedCapsOrTotals()
    {
        Service.ExecuteHealthModelQueries(TestSubscription, Arg.Any<IReadOnlyList<HealthModelQuery>>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new HealthModelQueryResult
                {
                    QueryIndex = 0,
                    Kind = "entityList",
                    Success = true,
                    Entities = [],
                    Page = new HealthModelQueryPage(false, 0, "more"),
                },
                new HealthModelQueryResult
                {
                    QueryIndex = 1,
                    Kind = "entityHistory",
                    Success = true,
                    Entities =
                    [
                        new HealthModelEntityResult
                        {
                            EntityName = "e1",
                            Success = true,
                            Page = new HealthModelEntityPage(true, 0, null),
                        },
                    ],
                },
            ]);

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--queries", TwoQueriesJson);
        var json = JsonSerializer.Serialize(response.Results);
        using var document = JsonDocument.Parse(json);

        var listPage = document.RootElement[0].GetProperty("page");
        Assert.False(listPage.GetProperty("complete").GetBoolean());
        Assert.Equal(0, listPage.GetProperty("returnedCount").GetInt32());
        Assert.Equal("more", listPage.GetProperty("cursor").GetString());
        var entityPage = document.RootElement[1].GetProperty("entities")[0].GetProperty("page");
        Assert.True(entityPage.GetProperty("complete").GetBoolean());
        Assert.Equal(0, entityPage.GetProperty("returnedCount").GetInt32());
        Assert.False(entityPage.TryGetProperty("cursor", out _));
        Assert.DoesNotContain("maxItems", json);
        Assert.DoesNotContain("truncated", json);
        Assert.DoesNotContain("totalCount", json);
        Assert.DoesNotContain("totalItems", json);
    }

    [Fact]
    public void QueryResult_OmitsAbsentLabelFromJson_ButEmitsPresentLabel()
    {
        // H2: an absent label is omitted entirely (not serialized as null); a present label is echoed.
        var withLabel = JsonSerializer.Serialize(
            new HealthModelQueryResult { QueryIndex = 0, Kind = "entityList", Success = true, Label = "hot" },
            MonitorJsonContext.Default.HealthModelQueryResult);
        var withoutLabel = JsonSerializer.Serialize(
            new HealthModelQueryResult { QueryIndex = 1, Kind = "entityList", Success = true, Label = null },
            MonitorJsonContext.Default.HealthModelQueryResult);

        Assert.Contains("\"label\":\"hot\"", withLabel);
        Assert.DoesNotContain("label", withoutLabel);
    }

    [Fact]
    public async Task ExecuteAsync_WritesQueryArrayDirectlyToResults()
    {
        EchoServiceResults();

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--queries", TwoQueriesJson);

        Assert.Equal(HttpStatusCode.OK, response.Status);

        // F1: assert on the exact bytes ResultConverter writes into CommandResponse.results — the document the CLI prints.
        var json = JsonSerializer.Serialize(response.Results);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());
        Assert.Equal(0, root[0].GetProperty("queryIndex").GetInt32());
        Assert.Equal(1, root[1].GetProperty("queryIndex").GetInt32());
        Assert.Equal("first", root[0].GetProperty("label").GetString());
        // No intermediate wrapper: the old shape emitted a nested "results" key here.
        Assert.DoesNotContain("\"results\"", json);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_ReturnsExceptionObjectNotArray()
    {
        Service.ExecuteHealthModelQueries(TestSubscription, Arg.Any<IReadOnlyList<HealthModelQuery>>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Test error"));

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--queries", TwoQueriesJson);

        // F5: a failure keeps the exception object; flattening must never turn it into an array (not even an empty one).
        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        var json = JsonSerializer.Serialize(response.Results);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("Test error", root.GetProperty("message").GetString());
        Assert.Equal("Exception", root.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("""{"kind":"entityList","resourceGroup":"rg1","healthModel":"hm1"}""")]
    public async Task ExecuteAsync_ReturnsBadRequest_ForInvalidQueriesInput(string queriesJson)
    {
        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--queries", queriesJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        await Service.DidNotReceive().ExecuteHealthModelQueries(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<HealthModelQuery>>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsBadRequest_WhenQueriesMissing()
    {
        var response = await ExecuteCommandAsync("--subscription", TestSubscription);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        await Service.DidNotReceive().ExecuteHealthModelQueries(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<HealthModelQuery>>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_IsolatesAMalformedQuery_AtTheCommandBoundary()
    {
        EchoServiceResults();

        // One unreadable element among three. The batch still runs; only its own slot fails.
        const string json = """
            [
              {"kind":"entityList","resourceGroup":"rg1","healthModel":"hm1"},
              {"kind":"entityList","resourceGroup":"rg1","healthModel":"hm1","signal":"cpu"},
              {"kind":"entityGet","resourceGroup":"rg1","healthModel":"hm1","entity":"e1"}
            ]
            """;

        var response = await ExecuteCommandAsync("--subscription", TestSubscription, "--queries", json);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).ExecuteHealthModelQueries(
            TestSubscription,
            Arg.Is<IReadOnlyList<HealthModelQuery>>(q =>
                q.Count == 3 && q[0] is EntityListQuery && q[1] is MalformedQuery && q[2] is EntityGetQuery),
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }
}

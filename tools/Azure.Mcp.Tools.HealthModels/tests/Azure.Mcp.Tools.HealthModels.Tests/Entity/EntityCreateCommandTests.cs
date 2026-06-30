// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json.Nodes;
using Azure.Mcp.Tests.Commands;
using Azure.Mcp.Tools.HealthModels.Commands;
using Azure.Mcp.Tools.HealthModels.Commands.Entity;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Mcp.Core.Options;
using NSubstitute;
using Xunit;

namespace Azure.Mcp.Tools.HealthModels.Tests.Entity;

public class EntityCreateCommandTests : SubscriptionCommandUnitTestsBase<EntityCreateCommand, IHealthModelsService>
{
    private const string Properties = """{"displayName":"Frontend","impact":"Standard"}""";

    [Fact]
    public async Task ExecuteAsync_ForwardsPropertiesVerbatim_AndReturnsEntity()
    {
        Service.CreateOrUpdateEntityAsync("sub123", "rg1", "hm1", "frontend", Properties, Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns(JsonNode.Parse("""{"name":"frontend","properties":{"displayName":"Frontend"}}""")!);

        var response = await ExecuteCommandAsync(
            "--subscription", "sub123",
            "--resource-group", "rg1",
            "--health-model", "hm1",
            "--entity", "frontend",
            "--properties", Properties);

        var result = ValidateAndDeserializeResponse(response, HealthModelsJsonContext.Default.HealthModelsItemResult);
        Assert.Equal("frontend", result.Result["name"]!.GetValue<string>());
        await Service.Received(1).CreateOrUpdateEntityAsync("sub123", "rg1", "hm1", "frontend", Properties, Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("--subscription", "sub123", "--resource-group", "rg1", "--health-model", "hm1")] // missing entity + properties
    [InlineData("--subscription", "sub123", "--resource-group", "rg1", "--health-model", "hm1", "--entity", "frontend")] // missing properties
    public async Task ExecuteAsync_Returns400_WhenRequiredOptionsMissing(params string[] args)
    {
        var response = await ExecuteCommandAsync(args);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("required", response.Message.ToLower());
    }
}

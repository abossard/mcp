// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json.Nodes;
using Azure.Mcp.Tests.Commands;
using Azure.Mcp.Tools.HealthModels.Commands;
using Azure.Mcp.Tools.HealthModels.Commands.HealthModel;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Mcp.Core.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Mcp.Tools.HealthModels.Tests.HealthModel;

public class HealthModelListCommandTests : SubscriptionCommandUnitTestsBase<HealthModelListCommand, IHealthModelsService>
{
    [Fact]
    public async Task ExecuteAsync_ReturnsHealthModels_WhenTheyExist()
    {
        Service.ListHealthModelsAsync("sub123", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                JsonNode.Parse("""{"name":"hm1","location":"eastus2"}""")!,
                JsonNode.Parse("""{"name":"hm2","location":"westus2"}""")!,
            ]);

        var response = await ExecuteCommandAsync("--subscription", "sub123");

        var result = ValidateAndDeserializeResponse(response, HealthModelsJsonContext.Default.HealthModelsListResult);
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("hm1", result.Items[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenNoneExist()
    {
        Service.ListHealthModelsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var response = await ExecuteCommandAsync("--subscription", "sub123");

        var result = ValidateAndDeserializeResponse(response, HealthModelsJsonContext.Default.HealthModelsListResult);
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsResourceGroup_WhenProvided()
    {
        Service.ListHealthModelsAsync(Arg.Any<string>(), Arg.Is("rg1"), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var response = await ExecuteCommandAsync("--subscription", "sub123", "--resource-group", "rg1");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).ListHealthModelsAsync(Arg.Any<string>(), Arg.Is("rg1"), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Returns400_WhenSubscriptionMissing()
    {
        var response = await ExecuteCommandAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("required", response.Message.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_Returns500_WhenServiceThrows()
    {
        Service.ListHealthModelsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("boom"));

        var response = await ExecuteCommandAsync("--subscription", "sub123");

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Contains("boom", response.Message);
    }
}

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

public class HealthModelGetCommandTests : SubscriptionCommandUnitTestsBase<HealthModelGetCommand, IHealthModelsService>
{
    [Fact]
    public async Task ExecuteAsync_ReturnsHealthModel_WhenItExists()
    {
        Service.GetHealthModelAsync("sub123", "rg1", "hm1", Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns(JsonNode.Parse("""{"name":"hm1","properties":{"provisioningState":"Succeeded"}}""")!);

        var response = await ExecuteCommandAsync("--subscription", "sub123", "--resource-group", "rg1", "--health-model", "hm1");

        var result = ValidateAndDeserializeResponse(response, HealthModelsJsonContext.Default.HealthModelsItemResult);
        Assert.Equal("hm1", result.Result["name"]!.GetValue<string>());
        Assert.Equal("Succeeded", result.Result["properties"]!["provisioningState"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("--subscription", "sub123")] // missing resource-group and health-model
    [InlineData("--subscription", "sub123", "--resource-group", "rg1")] // missing health-model
    public async Task ExecuteAsync_Returns400_WhenRequiredOptionsMissing(params string[] args)
    {
        var response = await ExecuteCommandAsync(args);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("required", response.Message.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_Returns500_WhenServiceThrows()
    {
        Service.GetHealthModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("boom"));

        var response = await ExecuteCommandAsync("--subscription", "sub123", "--resource-group", "rg1", "--health-model", "hm1");

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Contains("boom", response.Message);
    }
}

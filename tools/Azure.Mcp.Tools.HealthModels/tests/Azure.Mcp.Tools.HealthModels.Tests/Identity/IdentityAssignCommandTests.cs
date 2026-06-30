// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json.Nodes;
using Azure.Mcp.Tests.Commands;
using Azure.Mcp.Tools.HealthModels.Commands;
using Azure.Mcp.Tools.HealthModels.Commands.Identity;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Mcp.Core.Options;
using NSubstitute;
using Xunit;

namespace Azure.Mcp.Tools.HealthModels.Tests.Identity;

public class IdentityAssignCommandTests : SubscriptionCommandUnitTestsBase<IdentityAssignCommand, IHealthModelsService>
{
    [Fact]
    public async Task ExecuteAsync_ParsesUserAssignedIds_AndAssignsIdentity()
    {
        const string id1 = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/ua1";
        const string id2 = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/ua2";

        Service.AssignIdentityAsync(
            "sub123", "rg1", "hm1", "UserAssigned",
            Arg.Is<IEnumerable<string>?>(ids => ids != null && ids.SequenceEqual(new[] { id1, id2 })),
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns(JsonNode.Parse("""{"type":"UserAssigned"}""")!);

        var response = await ExecuteCommandAsync(
            "--subscription", "sub123",
            "--resource-group", "rg1",
            "--health-model", "hm1",
            "--identity-type", "UserAssigned",
            "--user-assigned-identities", $"{id1}, {id2}");

        var result = ValidateAndDeserializeResponse(response, HealthModelsJsonContext.Default.HealthModelsItemResult);
        Assert.Equal("UserAssigned", result.Result["type"]!.GetValue<string>());
        await Service.Received(1).AssignIdentityAsync(
            "sub123", "rg1", "hm1", "UserAssigned",
            Arg.Is<IEnumerable<string>?>(ids => ids != null && ids.SequenceEqual(new[] { id1, id2 })),
            Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Returns400_WhenIdentityTypeMissing()
    {
        var response = await ExecuteCommandAsync("--subscription", "sub123", "--resource-group", "rg1", "--health-model", "hm1");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("required", response.Message.ToLower());
    }
}

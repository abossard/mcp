// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Mcp.Tests;
using Microsoft.Mcp.Tests.Client;
using Microsoft.Mcp.Tests.Client.Helpers;
using Microsoft.Mcp.Tests.Generated.Models;
using Xunit;

namespace Azure.Mcp.Tools.HealthModels.Tests;

public class HealthModelsCommandTests(ITestOutputHelper output, TestProxyFixture fixture, LiveServerFixture liveServerFixture)
    : RecordedCommandTestsBase(output, fixture, liveServerFixture)
{
    private const string Sanitized = "Sanitized";

    public override List<UriRegexSanitizer> UriRegexSanitizers =>
    [
        new UriRegexSanitizer(new UriRegexSanitizerBody
        {
            Regex = "healthmodels\\/([^?\\/]+)",
            Value = Sanitized,
            GroupForReplace = "1"
        }),
        new UriRegexSanitizer(new UriRegexSanitizerBody
        {
            Regex = "resource[gG]roups\\/([^?\\/]+)",
            Value = Sanitized,
            GroupForReplace = "1"
        })
    ];

    [Fact]
    public async Task Should_ListHealthModels_BySubscription()
    {
        var result = await CallToolAsync(
            "healthmodels_list",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "tenant", Settings.TenantId }
            });

        Assert.NotNull(result);
        var items = result.Value.AssertProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
    }

    [Fact]
    public async Task Should_ListHealthModels_ByResourceGroup()
    {
        var result = await CallToolAsync(
            "healthmodels_list",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName },
                { "tenant", Settings.TenantId }
            });

        Assert.NotNull(result);
        var items = result.Value.AssertProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(items.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Should_GetHealthModel()
    {
        var result = await CallToolAsync(
            "healthmodels_get",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName },
                { "health-model", Settings.ResourceBaseName },
                { "tenant", Settings.TenantId }
            });

        Assert.NotNull(result);
        var model = result.Value.AssertProperty("result");
        Assert.Equal(JsonValueKind.Object, model.ValueKind);
        Assert.Equal("Microsoft.CloudHealth/healthmodels", model.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Should_ListEntities()
    {
        var result = await CallToolAsync(
            "healthmodels_entity_list",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName },
                { "health-model", Settings.ResourceBaseName },
                { "tenant", Settings.TenantId }
            });

        Assert.NotNull(result);
        var items = result.Value.AssertProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
    }
}

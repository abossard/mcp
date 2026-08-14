// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Planning;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// Selector resolution against the shared fixture. A selector that quietly matches more than the caller
/// meant is the difference between editing one entity and editing the whole model.
/// </summary>
public class HealthModelSelectorMatcherTests
{
    private static HealthModelSelector Selector(string json) =>
        JsonSerializer.Deserialize(json, MonitorJsonContext.Default.HealthModelSelector)!;

    private static SelectorMatch Match(string json)
    {
        Assert.True(HealthModelSelectorMatcher.TryMatch(
            Selector(json), HealthModelGraphFixture.Snapshot(), out var match, out var error), error);
        return match;
    }

    [Theory]
    [InlineData("""{"entity":{"names":["web"]}}""", HealthModelResourceKind.Entity, "web")]
    [InlineData("""{"entity":{"names":["web","db"]}}""", HealthModelResourceKind.Entity, "web,db")]
    [InlineData("""{"entity":{"all":true}}""", HealthModelResourceKind.Entity, "web,api,db")]
    [InlineData("""{"entity":{"displayName":"API"}}""", HealthModelResourceKind.Entity, "api")]
    [InlineData("""{"entity":{"discoveredBy":"rule-1"}}""", HealthModelResourceKind.Entity, "api")]
    [InlineData("""{"entity":{"tags":{"tier":"api"}}}""", HealthModelResourceKind.Entity, "api")]
    // Every listed tag must be present, so a pair that only half matches selects nothing.
    [InlineData("""{"entity":{"tags":{"tier":"api","env":"dev"}}}""", HealthModelResourceKind.Entity, "")]
    // Criteria are combined with AND.
    [InlineData("""{"entity":{"all":true,"tags":{"tier":"web"}}}""", HealthModelResourceKind.Entity, "web")]
    [InlineData("""{"entity":{"names":["web","api"],"discoveredBy":"rule-1"}}""", HealthModelResourceKind.Entity, "api")]
    [InlineData("""{"relationship":{"parent":"api"}}""", HealthModelResourceKind.Relationship, "api-db")]
    [InlineData("""{"relationship":{"child":"api"}}""", HealthModelResourceKind.Relationship, "web-api")]
    [InlineData("""{"relationship":{"discoveredBy":"rule-1"}}""", HealthModelResourceKind.Relationship, "api-db")]
    [InlineData("""{"relationship":{"names":["web-api","api-db"]}}""", HealthModelResourceKind.Relationship, "web-api,api-db")]
    [InlineData("""{"signalDefinition":{"signalKind":"LogAnalyticsQuery"}}""", HealthModelResourceKind.SignalDefinition, "requests")]
    [InlineData("""{"signalDefinition":{"displayName":"CPU"}}""", HealthModelResourceKind.SignalDefinition, "cpu")]
    [InlineData("""{"signalDefinition":{"names":["cpu"]}}""", HealthModelResourceKind.SignalDefinition, "cpu")]
    // An exact-match selector does not fall back to substring matching.
    [InlineData("""{"entity":{"displayName":"AP"}}""", HealthModelResourceKind.Entity, "")]
    [InlineData("""{"entity":{"names":["WEB"]}}""", HealthModelResourceKind.Entity, "")]
    public void TryMatch_SelectsExactlyTheNamedResources(string json, HealthModelResourceKind kind, string expected)
    {
        var match = Match(json);

        Assert.Equal(kind, match.Kind);
        Assert.Equal(
            expected.Length == 0 ? [] : expected.Split(','),
            match.Matches.Select(m => m.Name).ToArray());
    }

    [Theory]
    [InlineData("{}", "exactly one")]
    [InlineData("""{"entity":{"all":true},"relationship":{"parent":"api"}}""", "2 target kinds")]
    [InlineData("""{"entity":{}}""", "select.entity names no criteria")]
    [InlineData("""{"relationship":{}}""", "select.relationship names no criteria")]
    [InlineData("""{"signalDefinition":{}}""", "select.signalDefinition names no criteria")]
    public void TryMatch_RejectsASelectorThatDoesNotNameOneKindAndOneCriterion(string json, string expected)
    {
        Assert.False(HealthModelSelectorMatcher.TryMatch(
            Selector(json), HealthModelGraphFixture.Snapshot(), out _, out var error));
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMatch_DescribesTheCriteriaItUsed_SoAZeroMatchCanQuoteThem()
    {
        Assert.Equal("entity {names: [web, db]}", Match("""{"entity":{"names":["web","db"]}}""").Description);
        Assert.Equal("relationship {parent: api}", Match("""{"relationship":{"parent":"api"}}""").Description);
        Assert.Equal(
            "signalDefinition {signalKind: LogAnalyticsQuery}",
            Match("""{"signalDefinition":{"signalKind":"LogAnalyticsQuery"}}""").Description);
    }
}

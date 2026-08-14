// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Planning;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// What the planner decides to do, against the shared fixture. Every assertion here is what a caller reads
/// in a what-if, so a planner that decides one thing and writes another is exactly the failure this guards.
/// </summary>
public class HealthModelChangePlannerTests
{
    private const string Scope = "\"resourceGroup\":\"rg\",\"healthModel\":\"hm\"";

    private static string Element(string kind, string tail) =>
        "{\"kind\":\"" + kind + "\"," + Scope + "," + tail + "}";

    private static string Patch(string select, string properties) =>
        Element("patch", "\"select\":" + select + ",\"patch\":{\"properties\":" + properties + "}");

    private static ChangePlan Plan(params string[] elements)
    {
        var json = "[" + string.Join(",", elements) + "]";
        Assert.True(HealthModelChangeParser.TryParse(json, out var changes, out var error), error);
        return HealthModelChangePlanner.Plan(
            changes,
            new Dictionary<PlanScope, HealthModelGraphSnapshot>
            {
                [HealthModelGraphFixture.Scope] = HealthModelGraphFixture.Snapshot(),
            });
    }

    private static PlannedChange Single(string element) => Assert.Single(Plan(element).Changes);

    [Fact]
    public void Plan_PatchingOneProperty_ReportsUpdateWithExactlyThatOneDifference()
    {
        var change = Single(Patch("""{"entity":{"names":["web"]}}""", """{"displayName":"Web front end"}"""));

        Assert.True(change.Success, change.Error);
        var target = Assert.Single(change.Targets);
        Assert.Equal(HealthModelChangeAction.Update, target.Action);
        Assert.Equal("web", target.Name);
        var difference = Assert.Single(target.Changes);
        Assert.Equal("properties.displayName", difference.Path);
        Assert.Equal("\"Frontend\"", difference.Before!.ToJsonString());
        Assert.Equal("\"Web front end\"", difference.After!.ToJsonString());
        // The collections the selector ran against, not the matched count.
        Assert.Equal(new HealthModelScanCounts(3, 2, 2), change.Scanned);
    }

    [Theory]
    // Writing back what is already there is a no-op, so an unchanged target is never rewritten.
    [InlineData("""{"entity":{"names":["web"]}}""", """{"displayName":"Frontend"}""", HealthModelChangeAction.NoOp, 0)]
    [InlineData("""{"entity":{"names":["web"]}}""", """{"displayName":"New"}""", HealthModelChangeAction.Update, 1)]
    // Endpoints are fixed at create time, so repointing one is a delete followed by a create.
    [InlineData("""{"relationship":{"names":["web-api"]}}""", """{"childEntityName":"db"}""", HealthModelChangeAction.Replace, 2)]
    [InlineData("""{"relationship":{"names":["web-api"]}}""", """{"parentEntityName":"db"}""", HealthModelChangeAction.Replace, 2)]
    // A relationship property that is not an endpoint is still an in-place update.
    [InlineData("""{"relationship":{"names":["web-api"]}}""", """{"displayName":"renamed edge"}""", HealthModelChangeAction.Update, 1)]
    [InlineData("""{"signalDefinition":{"names":["cpu"]}}""", """{"displayName":"Processor"}""", HealthModelChangeAction.Update, 1)]
    public void Plan_ChoosesTheActionAndCallCountTheResourceModelAllows(
        string select, string properties, HealthModelChangeAction action, int writes)
    {
        var target = Assert.Single(Single(Patch(select, properties)).Targets);

        Assert.Equal(action, target.Action);
        Assert.Equal(writes, target.Writes.Count);
        if (action == HealthModelChangeAction.Replace)
        {
            Assert.Equal(
                [PlannedOperation.Delete, PlannedOperation.Put],
                target.Writes.Select(w => w.Operation).ToArray());
        }
    }

    [Fact]
    public void Plan_RenamingAnEntity_ExpandsToCreateThenRepointThenDelete()
    {
        // 'api' is the child of web-api and the parent of api-db, so both edges have to move.
        var change = Single(Element("rename", """ "select":{"entity":{"names":["api"]}},"newName":"api-v2" """));

        Assert.True(change.Success, change.Error);
        Assert.Equal(
            [
                (HealthModelResourceKind.Entity, "api-v2", HealthModelChangeAction.Create),
                (HealthModelResourceKind.Relationship, "web-api", HealthModelChangeAction.Replace),
                (HealthModelResourceKind.Relationship, "api-db", HealthModelChangeAction.Replace),
                (HealthModelResourceKind.Entity, "api", HealthModelChangeAction.Delete),
            ],
            change.Targets.Select(t => (t.Kind, t.Name, t.Action)).ToArray());

        Assert.Equal("properties.childEntityName", Assert.Single(change.Targets[1].Changes).Path);
        Assert.Equal("\"api-v2\"", change.Targets[1].Changes[0].After!.ToJsonString());
        Assert.Equal("properties.parentEntityName", Assert.Single(change.Targets[2].Changes).Path);
        // The new entity carries the old body forward rather than being created empty.
        Assert.Contains(change.Targets[0].Changes, c => c.Path == "properties.displayName");
    }

    [Theory]
    [InlineData("""{"entity":{"names":["nope"]}}""", "entity {names: [nope]}")]
    [InlineData("""{"entity":{"displayName":"nope"}}""", "entity {displayName: nope}")]
    [InlineData("""{"entity":{"tags":{"tier":"nope"}}}""", "entity {tags: tier=nope}")]
    [InlineData("""{"entity":{"discoveredBy":"nope"}}""", "entity {discoveredBy: nope}")]
    [InlineData("""{"relationship":{"parent":"nope"}}""", "relationship {parent: nope}")]
    [InlineData("""{"relationship":{"child":"nope"}}""", "relationship {child: nope}")]
    [InlineData("""{"signalDefinition":{"signalKind":"nope"}}""", "signalDefinition {signalKind: nope}")]
    public void Plan_ASelectorThatMatchesNothing_FailsTheElementQuotingTheSelectorAndTheZeroCount(
        string select, string description)
    {
        var change = Single(Patch(select, """{"displayName":"x"}"""));

        Assert.False(change.Success);
        Assert.Contains(description, change.Error!, StringComparison.Ordinal);
        Assert.Contains("matched 0 targets", change.Error!, StringComparison.Ordinal);
        Assert.Empty(change.Targets);
    }

    [Theory]
    [InlineData("patch", """ ,"patch":{"properties":{"displayName":"x"}} """)]
    [InlineData("delete", "")]
    [InlineData("rename", """ ,"newName":"whatever" """)]
    public void Plan_AllowEmptyMatch_TurnsAZeroMatchIntoASuccessWithNoTargets(string kind, string tail)
    {
        var change = Single(Element(
            kind, """ "select":{"entity":{"names":["nope"]}},"allowEmptyMatch":true """ + tail));

        Assert.True(change.Success, change.Error);
        Assert.Null(change.Error);
        Assert.Empty(change.Targets);
    }

    [Theory]
    // A create against a name that is not there is a create; against an identical body it is a no-op.
    [InlineData("""{"entity":{"properties":{"displayName":"Cache"}}}""", "cache", HealthModelChangeAction.Create)]
    [InlineData("""{"entity":{"properties":{"displayName":"Database"}}}""", "db", HealthModelChangeAction.NoOp)]
    [InlineData("""{"entity":{"properties":{"displayName":"Renamed"}}}""", "db", HealthModelChangeAction.Update)]
    [InlineData("""{"signalDefinition":{"properties":{"signalKind":"AzureResourceMetric","displayName":"CPU","metricNamespace":"Microsoft.Compute/virtualMachines","metricName":"Percentage CPU"}}}""", "cpu", HealthModelChangeAction.NoOp)]
    public void Plan_CreateIsAnUpsert(string resource, string name, HealthModelChangeAction action)
    {
        var change = Single(Element("create", "\"name\":\"" + name + "\",\"resource\":" + resource));

        Assert.True(change.Success, change.Error);
        var target = Assert.Single(change.Targets);
        Assert.Equal(action, target.Action);
        Assert.Equal(action == HealthModelChangeAction.NoOp ? 0 : 1, target.Writes.Count);
    }

    [Fact]
    public void Plan_DeletingAnEntityThatAnEdgeStillPointsAt_IsRejectedRatherThanCascaded()
    {
        var change = Single(Element("delete", """ "select":{"entity":{"names":["db"]}} """));

        Assert.False(change.Success);
        Assert.Contains("api-db", change.Error!, StringComparison.Ordinal);
        Assert.Empty(change.Targets);
    }

    [Theory]
    [InlineData("""{"signalDefinition":{"names":["cpu","requests"]}}""", 2)]
    [InlineData("""{"relationship":{"names":["web-api","api-db"]}}""", 2)]
    [InlineData("""{"relationship":{"discoveredBy":"rule-1"}}""", 1)]
    public void Plan_DeleteWithNoStrandedEdges_PlansOneDeleteCallPerTarget(string select, int expected)
    {
        var change = Single(Element("delete", "\"select\":" + select));

        Assert.True(change.Success, change.Error);
        Assert.Equal(expected, change.Targets.Count);
        Assert.All(change.Targets, target =>
        {
            Assert.Equal(HealthModelChangeAction.Delete, target.Action);
            Assert.Equal(PlannedOperation.Delete, Assert.Single(target.Writes).Operation);
        });
    }

    [Fact]
    public void Plan_ReadsEachElementAgainstWhatTheEarlierElementsWouldLeaveBehind()
    {
        var plan = Plan(
            Element("create", """ "name":"cache","resource":{"entity":{"properties":{"displayName":"Cache"}}} """),
            Patch("""{"entity":{"names":["cache"]}}""", """{"displayName":"Redis"}"""),
            Element("delete", """ "select":{"entity":{"names":["cache"]}} """));

        Assert.All(plan.Changes, change => Assert.True(change.Success, change.Error));
        Assert.Equal(HealthModelChangeAction.Create, plan.Changes[0].Targets[0].Action);
        // The patch resolves against the entity the create would add, not against the original snapshot.
        Assert.Equal(HealthModelChangeAction.Update, plan.Changes[1].Targets[0].Action);
        Assert.Equal("properties.displayName", plan.Changes[1].Targets[0].Changes[0].Path);
        Assert.Equal(HealthModelChangeAction.Delete, plan.Changes[2].Targets[0].Action);
        Assert.Equal([0, 1, 2], plan.Changes.Select(c => c.ChangeIndex).ToArray());
        // Scanned tracks the growing model: the create is planned against 3 entities, the rest against 4.
        Assert.Equal(3, plan.Changes[0].Scanned.Entities);
        Assert.Equal(4, plan.Changes[1].Scanned.Entities);
        Assert.Equal(3, plan.AffectedCount);
    }

    [Theory]
    [InlineData("""{"properties":{"provisioningState":"Succeeded"}}""", "provisioningState")]
    [InlineData("""{"properties":{"healthState":"Healthy"}}""", "healthState")]
    [InlineData("""{"properties":{"discoveredBy":"rule-2"}}""", "discoveredBy")]
    public void Plan_APatchNamingAServerOwnedProperty_FailsTheElementBeforeAnySelectorRuns(
        string patch, string offending)
    {
        var change = Single(Element(
            "patch", """ "select":{"entity":{"names":["web"]}},"patch": """ + patch));

        Assert.False(change.Success);
        Assert.Contains(offending, change.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// G21: a tag key is caller data even when it spells a reserved token, so the element is planned and
    /// the tag shows up in the what-if instead of being rejected or silently dropped from the diff.
    /// </summary>
    [Theory]
    [InlineData("name")]
    [InlineData("type")]
    [InlineData("id")]
    public void Plan_APatchNamingAReservedTokenAsATagKey_IsPlannedAndReportsTheTag(string tagKey)
    {
        var change = Single(Element(
            "patch",
            """ "select":{"entity":{"names":["web"]}},"patch":{"properties":{"tags":{"""
                + $"\"{tagKey}\":\"platform\"" + "}}}"));

        Assert.True(change.Success, change.Error);
        var target = Assert.Single(change.Targets);
        Assert.Equal(HealthModelChangeAction.Update, target.Action);
        var difference = Assert.Single(target.Changes);
        Assert.Equal($"properties.tags.{tagKey}", difference.Path);
        Assert.Null(difference.Before);
        Assert.Equal("\"platform\"", difference.After!.ToJsonString());
        // The sibling tag the fixture already carries is untouched, so this really was a merge.
        Assert.Equal("web", Assert.Single(target.Writes).Body!["properties"]!["tags"]!["tier"]!.GetValue<string>());
    }

    /// <summary>
    /// G33: elements fold forward so a batch composes, but a folded body is a prediction. The restore body
    /// a target carries is the one the batch started from, so a failed replace can only ever put back a
    /// state the service is known to have held.
    /// </summary>
    [Fact]
    public void Plan_ASecondElementOnTheSameResource_CarriesThePreBatchRestoreBody()
    {
        var plan = Plan(
            Patch("""{"relationship":{"names":["web-api"]}}""", """{"displayName":"FOLDED-ONLY"}"""),
            Patch("""{"relationship":{"names":["web-api"]}}""", """{"childEntityName":"db"}"""));

        var second = Assert.Single(plan.Changes[1].Targets);
        // The fold did reach the planning input: the endpoint change is planned as a replace.
        Assert.Equal(HealthModelChangeAction.Replace, second.Action);
        Assert.Equal(
            "web depends on api",
            second.Restore!["properties"]!["displayName"]!.GetValue<string>());

        var original = HealthModelGraphFixture.Snapshot()
            .Find(HealthModelResourceKind.Relationship, "web-api")!.Body;
        Assert.True(JsonNode.DeepEquals(original, second.Restore));
        Assert.True(JsonNode.DeepEquals(original, Assert.Single(plan.Changes[0].Targets).Restore));
    }

    [Fact]
    public void Plan_AMalformedElement_FailsOnItsOwnSlotWhileItsSiblingsStillPlan()
    {
        var plan = Plan(
            """{"kind":"nonsense"}""",
            Patch("""{"entity":{"names":["web"]}}""", """{"displayName":"New"}"""));

        Assert.False(plan.Changes[0].Success);
        Assert.Null(plan.Changes[0].Kind);
        Assert.True(plan.Changes[1].Success, plan.Changes[1].Error);
        Assert.Equal(HealthModelChangeKind.Patch, plan.Changes[1].Kind);
        Assert.Equal(1, plan.AffectedCount);
    }
}

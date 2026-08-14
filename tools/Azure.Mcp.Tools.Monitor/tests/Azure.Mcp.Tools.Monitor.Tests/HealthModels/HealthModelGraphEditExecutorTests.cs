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
/// End-to-end behaviour of a change set against a fake service: the two fail-closed guards, the reconcile
/// order, and the promise that a second run of the same change set does nothing.
/// </summary>
public class HealthModelGraphEditExecutorTests
{
    private const string Scope = "\"resourceGroup\":\"rg\",\"healthModel\":\"hm\"";

    private static string Element(string kind, string tail) =>
        "{\"kind\":\"" + kind + "\"," + Scope + "," + tail + "}";

    private const string PatchWeb =
        """ "select":{"entity":{"names":["web"]}},"patch":{"properties":{"displayName":"Web front end"}} """;

    private const string CreateCache =
        """ "name":"cache","resource":{"entity":{"properties":{"displayName":"Cache"}}} """;

    /// <summary>
    /// The shape that only churns through the real SDK bridge: the signal inside the array gets a
    /// <c>signalKind</c> the caller never wrote, and an array is compared as one value.
    /// </summary>
    private const string CreateCacheWithSignals =
        """ "name":"cache","resource":{"entity":{"properties":{"displayName":"Cache","signalGroups":{"azureLogAnalytics":{"signals":[{"name":"p99","signalDefinitionName":"cpu"}]}}}}} """;

    private static async Task<(HealthModelGraphEditResult Result, FakeHealthModelWriteRunner Runner)> Run(
        FakeHealthModelWriteRunner runner,
        HealthModelChangeMode mode,
        HealthModelChangeExpectation? expect,
        params string[] elements)
    {
        var json = "[" + string.Join(",", elements) + "]";
        Assert.True(HealthModelChangeParser.TryParse(json, out var changes, out var error), error);
        var result = await new HealthModelGraphEditExecutor(runner)
            .ExecuteAsync(changes, mode, expect, CancellationToken.None);
        return (result, runner);
    }

    private static Task<(HealthModelGraphEditResult Result, FakeHealthModelWriteRunner Runner)> WhatIf(
        params string[] elements) =>
        Run(new FakeHealthModelWriteRunner(), HealthModelChangeMode.WhatIf, null, elements);

    [Fact]
    public async Task ExecuteAsync_WhatIf_ComputesTheWholeChangeSetWithoutTouchingTheService()
    {
        var (result, runner) = await WhatIf(
            Element("create", CreateCache),
            Element("patch", PatchWeb),
            Element("rename", """ "select":{"entity":{"names":["api"]}},"newName":"api-v2" """),
            Element("delete", """ "select":{"signalDefinition":{"names":["requests"]}} """));

        Assert.Equal(0, runner.WriteCallCount);
        Assert.Equal(0, runner.PutCallCount);
        Assert.Equal(0, runner.DeleteCallCount);
        Assert.True(result.Success);
        Assert.Equal(HealthModelChangeMode.WhatIf, result.Mode);
        Assert.All(result.Changes, change =>
        {
            Assert.True(change.Success, change.Error);
            Assert.NotEmpty(change.Targets!);
        });
        // create(1) + patch(1) + rename(4) + delete(1)
        Assert.Equal(7, result.AffectedCount);
        Assert.Equal([0, 1, 2, 3], result.Changes.Select(c => c.ChangeIndex).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_Apply_WritesOnlyWhenTheCallerDeclaredTheCountItReadInTheWhatIf()
    {
        var (whatIf, _) = await WhatIf(Element("create", CreateCache), Element("patch", PatchWeb));
        Assert.Equal(2, whatIf.AffectedCount);

        var (result, runner) = await Run(
            new FakeHealthModelWriteRunner(),
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = whatIf.AffectedCount },
            Element("create", CreateCache),
            Element("patch", PatchWeb));

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, runner.PutCallCount);
        Assert.Equal(0, runner.DeleteCallCount);
        Assert.All(result.Changes, change => Assert.All(change.Targets!, t => Assert.True(t.Success, t.Error)));
        Assert.Equal("Cache", runner.Find(HealthModelResourceKind.Entity, "cache")!
            .Body["properties"]!["displayName"]!.GetValue<string>());
    }

    [Theory]
    // One lower than the two targets the change set actually affects.
    [InlineData(1, "1")]
    [InlineData(3, "3")]
    public async Task ExecuteAsync_Apply_WithAMismatchedCount_RejectsTheWholeBatchWithoutWriting(
        int declared, string expectedInError)
    {
        var (result, runner) = await Run(
            new FakeHealthModelWriteRunner(),
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = declared },
            Element("create", CreateCache),
            Element("patch", PatchWeb));

        Assert.False(result.Success);
        Assert.Equal(0, runner.WriteCallCount);
        Assert.Contains(expectedInError, result.Error!, StringComparison.Ordinal);
        Assert.Contains("2 target(s)", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Apply_WithNoDeclaredCount_RejectsTheWholeBatchWithoutWriting()
    {
        var (result, runner) = await Run(
            new FakeHealthModelWriteRunner(),
            HealthModelChangeMode.Apply,
            null,
            Element("create", CreateCache),
            Element("patch", PatchWeb));

        Assert.False(result.Success);
        Assert.Equal(0, runner.WriteCallCount);
        Assert.Contains("expect.affectedCount", result.Error!, StringComparison.Ordinal);

        // The same change set in the default mode needs no declaration at all: only an apply is gated.
        var (preview, previewRunner) = await WhatIf(Element("create", CreateCache), Element("patch", PatchWeb));
        Assert.True(preview.Success);
        Assert.Equal(0, previewRunner.WriteCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_Apply_WithASnapshotTokenThatNoLongerMatches_RejectsTheWholeBatchWithoutWriting()
    {
        var (whatIf, _) = await WhatIf(Element("patch", PatchWeb));
        Assert.False(string.IsNullOrEmpty(whatIf.Snapshot));

        var runner = new FakeHealthModelWriteRunner();
        // Someone else edited a different property of the same entity in between.
        runner.Mutate(HealthModelResourceKind.Entity, "web", "healthObjective", 95.0);

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = whatIf.AffectedCount, Snapshot = whatIf.Snapshot },
            Element("patch", PatchWeb));

        Assert.False(result.Success);
        Assert.Equal(0, runner.WriteCallCount);
        Assert.Contains("does not match", result.Error!, StringComparison.Ordinal);

        // The same token against an unchanged model is accepted, so the guard is about drift, not the token.
        var (accepted, acceptedRunner) = await Run(
            new FakeHealthModelWriteRunner(),
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = whatIf.AffectedCount, Snapshot = whatIf.Snapshot },
            Element("patch", PatchWeb));
        Assert.True(accepted.Success, accepted.Error);
        Assert.Equal(1, acceptedRunner.PutCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_OrdersWritesSoADependentIsNeverAttemptedBeforeOrAfterAFailedDependency()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailOn.Add("put:Entity:cache");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 4 },
            Element("create", """ "name":"latency","resource":{"signalDefinition":{"properties":{"signalKind":"LogAnalyticsQuery","displayName":"Latency"}}} """),
            Element("create", CreateCache),
            Element("create", """ "name":"web-cache","resource":{"relationship":{"properties":{"parentEntityName":"web","childEntityName":"cache"}}} """),
            Element("create", """ "name":"web-db","resource":{"relationship":{"properties":{"parentEntityName":"web","childEntityName":"db"}}} """));

        // Signal definitions first, then entities, then relationships — the blocked edge is never attempted.
        Assert.Equal(
            ["put:SignalDefinition:latency", "put:Entity:cache", "put:Relationship:web-db"],
            runner.Calls);

        var blocked = Assert.Single(result.Changes[2].Targets!);
        Assert.Equal(HealthModelChangeAction.Skipped, blocked.Action);
        Assert.False(blocked.Success);
        Assert.Contains("cache", blocked.SkipReason!, StringComparison.Ordinal);

        Assert.False(Assert.Single(result.Changes[1].Targets!).Success);
        Assert.True(Assert.Single(result.Changes[3].Targets!).Success);
        Assert.True(Assert.Single(result.Changes[0].Targets!).Success);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesRunAfterCreatesAndInTheReverseKindOrder()
    {
        var runner = new FakeHealthModelWriteRunner();

        var (_, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 4 },
            Element("delete", """ "select":{"relationship":{"names":["api-db"]}} """),
            Element("delete", """ "select":{"signalDefinition":{"names":["cpu"]}} """),
            Element("create", CreateCache),
            Element("delete", """ "select":{"entity":{"names":["db"]}} """));

        Assert.Equal(
            [
                "put:Entity:cache",
                "delete:Relationship:api-db",
                "delete:Entity:db",
                "delete:SignalDefinition:cpu",
            ],
            runner.Calls);
    }

    /// <summary>
    /// G13: the promise that a change set can be re-run. The fake is behind the real SDK bridge here,
    /// because that bridge is what materialises properties the caller never wrote — including inside
    /// array elements, where a whole-array comparison is the only thing standing between an unchanged
    /// model and a PUT on every re-apply.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ApplyingTheSameChangeSetTwice_LeavesTheSecondRunWithNothingToDo()
    {
        var runner = new FakeHealthModelWriteRunner { RoundTripThroughSdk = true };
        string[] elements = [Element("create", CreateCacheWithSignals), Element("patch", PatchWeb)];

        var (first, _) = await Run(
            runner, HealthModelChangeMode.Apply, new HealthModelChangeExpectation { AffectedCount = 2 }, elements);
        Assert.True(first.Success, first.Error);
        Assert.Equal(2, runner.PutCallCount);
        // The bridge really did add a property inside an array element the caller never wrote.
        Assert.Contains(
            "\"signalKind\":null",
            runner.Find(HealthModelResourceKind.Entity, "cache")!.Body.ToJsonString(),
            StringComparison.Ordinal);

        var (second, _) = await Run(runner, HealthModelChangeMode.WhatIf, null, elements);

        Assert.Equal(0, second.AffectedCount);
        Assert.All(second.Changes, change => Assert.All(change.Targets!, target =>
        {
            Assert.Equal(HealthModelChangeAction.NoOp, target.Action);
            Assert.Null(target.Changes);
        }));
        // The second run reads, but still writes nothing.
        Assert.Equal(2, runner.PutCallCount);

        // And an apply of the same set is a no-op end to end, not only in the preview.
        var (third, _) = await Run(
            runner, HealthModelChangeMode.Apply, new HealthModelChangeExpectation { AffectedCount = 0 }, elements);
        Assert.True(third.Success, third.Error);
        Assert.Equal(2, runner.PutCallCount);
        Assert.Equal(0, runner.DeleteCallCount);
    }

    /// <summary>
    /// G23: a create means "ensure the target exists with at least these properties". Against a service
    /// that fills its own defaults on write, a create that diffed the whole body would report the
    /// service's default as a removal and churn forever.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ApplyingACreateTwiceAgainstAServiceThatFillsDefaults_LeavesNothingToDo()
    {
        var runner = new FakeHealthModelWriteRunner { RoundTripThroughSdk = true };
        runner.ServerDefaults["impact"] = "Standard";
        runner.ServerDefaults["healthObjective"] = 99.0;
        string[] elements = [Element("create", CreateCacheWithSignals)];

        var (first, _) = await Run(
            runner, HealthModelChangeMode.Apply, new HealthModelChangeExpectation { AffectedCount = 1 }, elements);
        Assert.True(first.Success, first.Error);
        Assert.Equal(1, runner.PutCallCount);
        // The service really did add properties the caller never wrote.
        Assert.Equal("Standard", runner.Find(HealthModelResourceKind.Entity, "cache")!
            .Body["properties"]!["impact"]!.GetValue<string>());

        var (second, _) = await Run(runner, HealthModelChangeMode.WhatIf, null, elements);

        Assert.Equal(0, second.AffectedCount);
        var target = Assert.Single(Assert.Single(second.Changes).Targets!);
        Assert.Equal(HealthModelChangeAction.NoOp, target.Action);
        Assert.Null(target.Changes);
        Assert.Equal(1, runner.PutCallCount);

        // Re-applying is still a no-op end to end, not just in the preview.
        var (third, _) = await Run(
            runner, HealthModelChangeMode.Apply, new HealthModelChangeExpectation { AffectedCount = 0 }, elements);
        Assert.True(third.Success, third.Error);
        Assert.Equal(1, runner.PutCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ACreateThatChangesADefaultedProperty_IsStillReportedAndWritten()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.ServerDefaults["impact"] = "Standard";

        await Run(
            runner, HealthModelChangeMode.Apply, new HealthModelChangeExpectation { AffectedCount = 1 },
            Element("create", CreateCache));
        Assert.Equal(1, runner.PutCallCount);

        // Same name, same displayName, but now the caller explicitly writes the property the service filled.
        var (result, _) = await Run(
            runner, HealthModelChangeMode.Apply, new HealthModelChangeExpectation { AffectedCount = 1 },
            Element("create", """ "name":"cache","resource":{"entity":{"properties":{"displayName":"Cache","impact":"Suppressed"}}} """));

        Assert.True(result.Success, result.Error);
        var target = Assert.Single(Assert.Single(result.Changes).Targets!);
        Assert.Equal(HealthModelChangeAction.Update, target.Action);
        var difference = Assert.Single(target.Changes!);
        Assert.Equal("properties.impact", difference.Path);
        Assert.Equal("\"Standard\"", difference.Before!.ToJsonString());
        Assert.Equal("\"Suppressed\"", difference.After!.ToJsonString());
        Assert.Equal(2, runner.PutCallCount);
        Assert.Equal("Suppressed", runner.Find(HealthModelResourceKind.Entity, "cache")!
            .Body["properties"]!["impact"]!.GetValue<string>());
    }

    /// <summary>
    /// G22: an entity's signals can borrow their defaults from a model-scope signal definition, so an
    /// entity written against a definition that failed is the MissingSignalDefinition failure the RP
    /// reports. The dependency runs signalDefinition -> entity, not just entity -> relationship.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenASignalDefinitionFails_TheEntitiesReferencingItAreSkippedByName()
    {
        var runner = new FakeHealthModelWriteRunner { RoundTripThroughSdk = true };
        runner.FailOn.Add("put:SignalDefinition:latency");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 4 },
            Element("create", """ "name":"latency","resource":{"signalDefinition":{"properties":{"signalKind":"LogAnalyticsQuery","displayName":"Latency"}}} """),
            Element("create", """ "name":"cache","resource":{"entity":{"properties":{"displayName":"Cache","signalGroups":{"azureLogAnalytics":{"signals":[{"name":"p99","signalDefinitionName":"latency"}]}}}}} """),
            Element("create", """ "name":"queue","resource":{"entity":{"properties":{"displayName":"Queue"}}} """),
            Element("create", """ "name":"web-cache","resource":{"relationship":{"properties":{"parentEntityName":"web","childEntityName":"cache"}}} """));

        // The referencing entity is never attempted, and the edge that depends on it is skipped in turn.
        Assert.Equal(["put:SignalDefinition:latency", "put:Entity:queue"], runner.Calls);

        var referencing = Assert.Single(result.Changes[1].Targets!);
        Assert.Equal(HealthModelChangeAction.Skipped, referencing.Action);
        Assert.False(referencing.Success);
        Assert.Contains("signal definition 'latency'", referencing.SkipReason!, StringComparison.Ordinal);

        Assert.Equal(HealthModelChangeAction.Skipped, Assert.Single(result.Changes[3].Targets!).Action);
        Assert.Contains("entity 'cache'", Assert.Single(result.Changes[3].Targets!).SkipReason!, StringComparison.Ordinal);

        // The unrelated entity still succeeds, so this is dependency tracking and not a batch abort.
        Assert.True(Assert.Single(result.Changes[2].Targets!).Success);
        Assert.Null(runner.Find(HealthModelResourceKind.Entity, "cache"));
    }

    /// <summary>
    /// G25: the failed-dependency set is keyed by scope as well as name, so a failure in one health model
    /// cannot skip an identically named target in another.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AFailureInOneHealthModel_DoesNotSkipTheSameNameInAnother()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailOn.Add("hm/put:Entity:cache");

        const string OtherScope = "\"resourceGroup\":\"rg\",\"healthModel\":\"hm2\"";
        static string Other(string kind, string tail) =>
            "{\"kind\":\"" + kind + "\"," + OtherScope + "," + tail + "}";

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 4 },
            Element("create", CreateCache),
            Element("create", """ "name":"web-cache","resource":{"relationship":{"properties":{"parentEntityName":"web","childEntityName":"cache"}}} """),
            Other("create", CreateCache),
            Other("create", """ "name":"web-cache","resource":{"relationship":{"properties":{"parentEntityName":"web","childEntityName":"cache"}}} """));

        Assert.False(Assert.Single(result.Changes[0].Targets!).Success);
        Assert.Equal(HealthModelChangeAction.Skipped, Assert.Single(result.Changes[1].Targets!).Action);

        // Same names, other model: both still run.
        Assert.True(Assert.Single(result.Changes[2].Targets!).Success);
        Assert.True(Assert.Single(result.Changes[3].Targets!).Success);
        Assert.Equal(
            ["put:Entity:cache", "put:Entity:cache", "put:Relationship:web-cache"],
            runner.Calls);
        Assert.NotNull(runner.Find(new PlanScope("rg", "hm2"), HealthModelResourceKind.Entity, "cache"));
        Assert.Null(runner.Find(HealthModelGraphFixture.Scope, HealthModelResourceKind.Entity, "cache"));
    }

    /// <summary>
    /// G24: the planner's target order is asserted elsewhere; this pins the order the writes were actually
    /// issued in, because deleting the old entity before its edges are repointed strands them.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RenamingAnEntity_RepointsEveryEdgeBeforeDeletingTheOldName()
    {
        var runner = new FakeHealthModelWriteRunner();

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            // 'api' is the child of web-api and the parent of api-db: create + two repoints + delete.
            new HealthModelChangeExpectation { AffectedCount = 4 },
            Element("rename", """ "select":{"entity":{"names":["api"]}},"newName":"api-v2" """));

        Assert.True(result.Success, result.Error);
        Assert.Equal(
            [
                "put:Entity:api-v2",
                "delete:Relationship:web-api",
                "put:Relationship:web-api",
                "delete:Relationship:api-db",
                "put:Relationship:api-db",
                "delete:Entity:api",
            ],
            runner.Calls);

        Assert.Equal("api-v2", runner.Find(HealthModelResourceKind.Relationship, "web-api")!
            .Body["properties"]!["childEntityName"]!.GetValue<string>());
        Assert.Equal("api-v2", runner.Find(HealthModelResourceKind.Relationship, "api-db")!
            .Body["properties"]!["parentEntityName"]!.GetValue<string>());
        Assert.Null(runner.Find(HealthModelResourceKind.Entity, "api"));
    }

    /// <summary>
    /// G12/G24: the happy-path order is pinned above, but order alone does not protect the graph. When a
    /// repoint fails, deleting the old name anyway strands the edge that still points at it, so the delete
    /// is a dependent of every repoint and not merely a later write.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenARepointFails_TheOldNameIsNotDeletedAndTheFailedEdgeIsNamed()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailOnce.Add("put:Relationship:web-api");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 4 },
            Element("rename", """ "select":{"entity":{"names":["api"]}},"newName":"api-v2" """));

        // The entity that an edge still points at is still there, and was never even attempted.
        Assert.DoesNotContain("delete:Entity:api", runner.Calls);
        Assert.NotNull(runner.Find(HealthModelResourceKind.Entity, "api"));
        Assert.Equal("api", runner.Find(HealthModelResourceKind.Relationship, "web-api")!
            .Body["properties"]!["childEntityName"]!.GetValue<string>());

        var targets = Assert.Single(result.Changes).Targets!;
        var deleteOld = Assert.Single(targets, target =>
            target.ResourceKind == HealthModelResourceKind.Entity && target.Name == "api");
        Assert.Equal(HealthModelChangeAction.Skipped, deleteOld.Action);
        Assert.False(deleteOld.Success);
        Assert.Contains("relationship 'web-api'", deleteOld.SkipReason!, StringComparison.Ordinal);

        // The unrelated edge still repoints, so this is dependency tracking and not a batch abort.
        Assert.Equal("api-v2", runner.Find(HealthModelResourceKind.Relationship, "api-db")!
            .Body["properties"]!["parentEntityName"]!.GetValue<string>());
    }

    /// <summary>
    /// G29: a replace is a delete followed by a create, so a create that fails destroys a resource the
    /// caller only asked to modify. The original body is captured before the delete and put back.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTheCreateHalfOfAReplaceFails_TheOriginalBodyIsPutBack()
    {
        var runner = new FakeHealthModelWriteRunner();
        var original = runner.Find(HealthModelResourceKind.Relationship, "web-api")!.Body.DeepClone();
        runner.FailOnce.Add("put:Relationship:web-api");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 1 },
            Element("patch", """ "select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"childEntityName":"db"}} """));

        Assert.Equal(
            ["delete:Relationship:web-api", "put:Relationship:web-api", "put:Relationship:web-api"],
            runner.Calls);

        var restored = runner.Find(HealthModelResourceKind.Relationship, "web-api");
        Assert.NotNull(restored);
        Assert.True(JsonNode.DeepEquals(original, restored.Body));

        var target = Assert.Single(Assert.Single(result.Changes).Targets!);
        Assert.False(target.Success);
        Assert.Contains("was restored", target.Error!, StringComparison.Ordinal);
        Assert.Contains("web-api", target.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// G29: when the restore fails as well the resource really is gone, and that is a different outcome
    /// from a change that simply did not happen. It has to be named as such.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTheRestoreAlsoFails_ReportsADistinctResourceLostErrorNamingTheResource()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailOn.Add("put:Relationship:web-api");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 1 },
            Element("patch", """ "select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"childEntityName":"db"}} """));

        // The restore was attempted, and the resource is genuinely gone.
        Assert.Equal(
            ["delete:Relationship:web-api", "put:Relationship:web-api", "put:Relationship:web-api"],
            runner.Calls);
        Assert.Null(runner.Find(HealthModelResourceKind.Relationship, "web-api"));

        var target = Assert.Single(Assert.Single(result.Changes).Targets!);
        Assert.False(target.Success);
        Assert.Contains("RESOURCE LOST", target.Error!, StringComparison.Ordinal);
        Assert.Contains("Relationship 'web-api'", target.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// G32: a resource whose earlier write failed is in an unknown state, so a later element on it would
    /// act on a state that never happened. It is skipped the same way a dependent of a failed dependency
    /// is, and the element that actually failed is named.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAnEarlierElementFailedOnAResource_TheLaterElementOnItIsSkipped()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailOn.Add("put:Relationship:web-api");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 3 },
            Element("patch", """ "label":"retitle","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"Renamed edge"}} """),
            Element("patch", """ "select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"childEntityName":"db"}} """),
            Element("patch", """ "select":{"relationship":{"names":["api-db"]}},"patch":{"properties":{"displayName":"api depends on db"}} """));

        // The second element's replace would have deleted the resource; it never ran at all.
        Assert.Equal(["put:Relationship:web-api", "put:Relationship:api-db"], runner.Calls);
        Assert.NotNull(runner.Find(HealthModelResourceKind.Relationship, "web-api"));
        Assert.Equal("api", runner.Find(HealthModelResourceKind.Relationship, "web-api")!
            .Body["properties"]!["childEntityName"]!.GetValue<string>());

        var skipped = Assert.Single(result.Changes[1].Targets!);
        Assert.Equal(HealthModelChangeAction.Skipped, skipped.Action);
        Assert.False(skipped.Success);
        Assert.Contains("change 0 ('retitle')", skipped.SkipReason!, StringComparison.Ordinal);
        Assert.Contains("relationship 'web-api'", skipped.SkipReason!, StringComparison.Ordinal);

        Assert.False(Assert.Single(result.Changes[0].Targets!).Success);
        // The unrelated edge still runs, so this is per-resource and not a batch abort.
        Assert.True(Assert.Single(result.Changes[2].Targets!).Success);
    }

    /// <summary>
    /// G37/G41: change 0 committed a write to the relationship, and change 1's replace on it fails after
    /// the delete half. Putting the pre-batch body back would overwrite a write the caller was already told
    /// had succeeded, so the restore — and only the restore — is withheld. The write itself was allowed to
    /// run, which is what keeps correct change sets working; the earlier element is told its write is gone.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAFailedReplaceWouldRestoreOverACommittedWrite_TheRestoreIsSkippedNotTheWrite()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailAt["put:Relationship:web-api"] = 2;

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 2 },
            Element("patch", """ "label":"retitle","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"COMMITTED"}} """),
            Element("patch", """ "label":"repoint","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"childEntityName":"db"}} """));

        // The replace ran. There is no fourth call: the pre-batch body was never put back over change 0's.
        Assert.Equal(
            ["put:Relationship:web-api", "delete:Relationship:web-api", "put:Relationship:web-api"],
            runner.Calls);
        Assert.Null(runner.Find(HealthModelResourceKind.Relationship, "web-api"));

        var failedTarget = Assert.Single(result.Changes[1].Targets!);
        Assert.False(failedTarget.Success);
        Assert.Contains("RESTORE SKIPPED", failedTarget.Error!, StringComparison.Ordinal);
        Assert.Contains("change 0 ('retitle')", failedTarget.Error!, StringComparison.Ordinal);
        Assert.Contains("Relationship 'web-api'", failedTarget.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("was restored", failedTarget.Error!, StringComparison.Ordinal);

        // Never silently: change 0 was told its write succeeded, so it is told that it no longer stands.
        var committed = Assert.Single(result.Changes[0].Targets!);
        Assert.True(committed.Success);
        Assert.Equal(
            "change 1 ('repoint') deleted relationship 'web-api' and failed to re-create it, so this " +
            "element's write is no longer present on that resource.",
            committed.SupersededBy);
    }

    /// <summary>
    /// G42: the shape the preemptive block used to break. Change 0 writes the relationship, change 1 renames
    /// the entity on the other end of it, which repoints that same relationship. Nothing here is in conflict:
    /// the planner folded change 0's write into the body change 1 repoints with, so the rename completes and
    /// carries the committed value with it. Every one of the five targets runs.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenARenameRepointsARelationshipAnEarlierElementWrote_TheRenameCompletes()
    {
        var runner = new FakeHealthModelWriteRunner();

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 5 },
            Element("patch", """ "label":"retitle","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"COMMITTED"}} """),
            Element("rename", """ "label":"rn","select":{"entity":{"names":["api"]}},"newName":"api-v2" """));

        Assert.All(
            result.Changes.SelectMany(change => change.Targets!),
            target =>
            {
                Assert.NotEqual(HealthModelChangeAction.Skipped, target.Action);
                Assert.True(target.Success, target.Error ?? target.SkipReason);
            });

        // A whole rename, not half of one: the old name is gone and both edges point at the new one.
        Assert.Null(runner.Find(HealthModelResourceKind.Entity, "api"));
        Assert.NotNull(runner.Find(HealthModelResourceKind.Entity, "api-v2"));

        var edge = runner.Find(HealthModelResourceKind.Relationship, "web-api")!;
        Assert.Equal("api-v2", edge.Body["properties"]!["childEntityName"]!.GetValue<string>());
        Assert.Equal("api-v2", runner.Find(HealthModelResourceKind.Relationship, "api-db")!
            .Body["properties"]!["parentEntityName"]!.GetValue<string>());

        // The repoint was folded over change 0's write rather than over the pre-batch body, so nothing the
        // caller was told had succeeded was quietly dropped on the way through.
        Assert.Equal("COMMITTED", edge.Body["properties"]!["displayName"]!.GetValue<string>());
    }

    /// <summary>
    /// G43: <c>expect.affectedCount</c> is echoed from a what-if into the apply, so an apply that touches a
    /// different set of targets than the what-if predicted makes that echo meaningless. Same change set, same
    /// starting model, every write succeeding: the two runs have to agree target for target.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenEveryWriteSucceeds_ApplyPerformsExactlyWhatWhatIfPredicted()
    {
        string[] elements =
        [
            Element("patch", """ "label":"retitle","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"COMMITTED"}} """),
            Element("rename", """ "label":"rn","select":{"entity":{"names":["api"]}},"newName":"api-v2" """),
            Element("create", CreateCache),
        ];

        var (predicted, _) = await WhatIf(elements);
        var (performed, _) = await Run(
            new FakeHealthModelWriteRunner(),
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = predicted.AffectedCount },
            elements);

        Assert.True(performed.Success, performed.Error);
        Assert.Equal(predicted.AffectedCount, performed.AffectedCount);
        Assert.Equal<IEnumerable<string>>(
            Shape(predicted),
            Shape(performed));

        static string[] Shape(HealthModelGraphEditResult result) => result.Changes
            .SelectMany(change => change.Targets!.Select(target =>
                $"{change.ChangeIndex}:{target.ResourceKind}:{target.Name}:{target.Action}"))
            .ToArray();
    }

    /// <summary>
    /// G33/G44: the body a restore puts back is the one the batch started from, not the working snapshot the
    /// planner folds each element into. A resource an earlier element CREATED is absent from the pre-batch
    /// state, so there is nothing to restore and the failure is reported alone — where a folded capture would
    /// have a body to put back, one the service never held before this batch.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAReplaceFailsOnAResourceAnEarlierElementCreated_ThereIsNoPreBatchBodyToRestore()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailAt["put:Relationship:web-db"] = 2;

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 2 },
            Element("create", """ "label":"edge","name":"web-db","resource":{"relationship":{"properties":{"parentEntityName":"web","childEntityName":"db","displayName":"ONLY-IN-THIS-BATCH"}}} """),
            Element("patch", """ "label":"repoint","select":{"relationship":{"names":["web-db"]}},"patch":{"properties":{"childEntityName":"api"}} """));

        Assert.Equal(
            ["put:Relationship:web-db", "delete:Relationship:web-db", "put:Relationship:web-db"],
            runner.Calls);
        Assert.Null(runner.Find(HealthModelResourceKind.Relationship, "web-db"));

        // The bare failure, with no restore of any kind mentioned, because the pre-batch state had no
        // web-db at all. A body folded from change 0 would have produced a restore outcome here instead.
        var failedTarget = Assert.Single(result.Changes[1].Targets!);
        Assert.False(failedTarget.Success);
        Assert.Equal("the service rejected 'web-db'.", failedTarget.Error);

        Assert.True(Assert.Single(result.Changes[0].Targets!).Success);
    }

    /// <summary>
    /// G45: a delete is allowed to remove a write an earlier element made — the caller asked for it, and the
    /// deletes run last precisely so they win. What is not allowed is doing it silently, because the earlier
    /// element's node still says its write succeeded and a reader has no other way to learn it is gone.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenADeleteRemovesAnEarlierElementsCommittedWrite_TheEarlierElementReportsIt()
    {
        var runner = new FakeHealthModelWriteRunner();

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 2 },
            Element("patch", """ "label":"retitle","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"COMMITTED"}} """),
            Element("delete", """ "label":"drop","select":{"relationship":{"names":["web-api"]}} """));

        Assert.Equal(["put:Relationship:web-api", "delete:Relationship:web-api"], runner.Calls);
        Assert.Null(runner.Find(HealthModelResourceKind.Relationship, "web-api"));

        var committed = Assert.Single(result.Changes[0].Targets!);
        Assert.True(committed.Success);
        Assert.Equal(
            "change 1 ('drop') deleted relationship 'web-api', so this element's write is no longer " +
            "present on that resource.",
            committed.SupersededBy);

        var deleted = Assert.Single(result.Changes[1].Targets!);
        Assert.True(deleted.Success);
        Assert.Null(deleted.SupersededBy);
    }

    /// <summary>
    /// G45: the same rule on a chain longer than two. Three elements write disjoint properties, so all
    /// three writes genuinely stand on the resource at once — none was overwritten by the next — and then
    /// a delete takes every one of them away together. Annotating only the most recent writer leaves the
    /// first two claiming an unqualified success for content that is gone, which is the silent discard
    /// this criterion exists to forbid. The two-element case above pins the shape this generalises.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenADeleteRemovesSeveralElementsCommittedWrites_EveryOneOfThemReportsIt()
    {
        var runner = new FakeHealthModelWriteRunner();

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 4 },
            Element("patch", """ "label":"first","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"A"}} """),
            Element("patch", """ "label":"second","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"tags":{"owner":"B"}}} """),
            Element("patch", """ "label":"third","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"tags":{"tier":"C"}}} """),
            Element("delete", """ "label":"drop","select":{"relationship":{"names":["web-api"]}} """));

        Assert.Equal(
            [
                "put:Relationship:web-api",
                "put:Relationship:web-api",
                "put:Relationship:web-api",
                "delete:Relationship:web-api",
            ],
            runner.Calls);
        Assert.Null(runner.Find(HealthModelResourceKind.Relationship, "web-api"));

        // Disjoint: each element changed a property of its own, so no write here was already gone before
        // the delete ran. All three stood on the resource together.
        Assert.Equal<IEnumerable<string>>(
            ["properties.displayName", "properties.tags.owner", "properties.tags.tier"],
            result.Changes.Take(3)
                .Select(change => Assert.Single(Assert.Single(change.Targets!).Changes!).Path)
                .ToArray());

        foreach (var index in new[] { 0, 1, 2 })
        {
            var superseded = Assert.Single(result.Changes[index].Targets!);
            Assert.True(superseded.Success);
            Assert.Equal(
                "change 3 ('drop') deleted relationship 'web-api', so this element's write is no longer " +
                "present on that resource.",
                superseded.SupersededBy);
        }

        var terminator = Assert.Single(result.Changes[3].Targets!);
        Assert.True(terminator.Success);
        Assert.Null(terminator.SupersededBy);
    }

    /// <summary>
    /// G45 on the other terminator: a replace whose re-create fails after the delete half removes every
    /// earlier write on that resource just as finally as a delete does, so every earlier writer is told —
    /// not only the one the RESTORE SKIPPED error names. That error names the most recent writer because
    /// that is the body the withheld restore would have discarded; the earlier ones learn it on their own
    /// nodes, which is the only place they can.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAFailedReplaceRemovesSeveralCommittedWrites_EveryOneOfThemReportsIt()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailAt["put:Relationship:web-api"] = 3;

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 3 },
            Element("patch", """ "label":"first","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"A"}} """),
            Element("patch", """ "label":"second","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"tags":{"owner":"B"}}} """),
            Element("patch", """ "label":"repoint","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"childEntityName":"db"}} """));

        // Two writes, then the replace: delete, failed re-create, and no fourth put — the pre-batch body
        // was never put back over either committed write.
        Assert.Equal(
            [
                "put:Relationship:web-api",
                "put:Relationship:web-api",
                "delete:Relationship:web-api",
                "put:Relationship:web-api",
            ],
            runner.Calls);
        Assert.Null(runner.Find(HealthModelResourceKind.Relationship, "web-api"));

        foreach (var index in new[] { 0, 1 })
        {
            var superseded = Assert.Single(result.Changes[index].Targets!);
            Assert.True(superseded.Success);
            Assert.Equal(
                "change 2 ('repoint') deleted relationship 'web-api' and failed to re-create it, so this " +
                "element's write is no longer present on that resource.",
                superseded.SupersededBy);
        }

        var failedTarget = Assert.Single(result.Changes[2].Targets!);
        Assert.False(failedTarget.Success);
        Assert.Contains("RESTORE SKIPPED", failedTarget.Error!, StringComparison.Ordinal);
        Assert.Contains("change 1 ('second')", failedTarget.Error!, StringComparison.Ordinal);
        Assert.Null(failedTarget.SupersededBy);
    }

    /// <summary>
    /// G46: the fan-out below a failed write, three deep on one resource. Nothing after the failure may
    /// reach the service, and every skip names the element that actually failed rather than the one
    /// immediately above it — a skipped element issued no write, so blaming it sends the caller after a
    /// call that never happened.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTheFirstElementOnAResourceFails_EveryLaterElementOnItIsSkippedNamingIt()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailOn.Add("put:Relationship:web-api");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 3 },
            Element("patch", """ "label":"first","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"A"}} """),
            Element("patch", """ "label":"second","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"B"}} """),
            Element("patch", """ "label":"third","select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"childEntityName":"db"}} """));

        // One attempt, then nothing: the third element's replace would have deleted the resource, and a
        // resource whose state is unknown is exactly what must not be deleted.
        Assert.Equal(["put:Relationship:web-api"], runner.Calls);
        Assert.Equal("api", runner.Find(HealthModelResourceKind.Relationship, "web-api")!
            .Body["properties"]!["childEntityName"]!.GetValue<string>());

        Assert.False(Assert.Single(result.Changes[0].Targets!).Success);

        foreach (var index in new[] { 1, 2 })
        {
            var skipped = Assert.Single(result.Changes[index].Targets!);
            Assert.Equal(HealthModelChangeAction.Skipped, skipped.Action);
            Assert.False(skipped.Success);
            Assert.Equal(
                "change 0 ('first') failed to write relationship 'web-api', so this target was not attempted.",
                skipped.SkipReason);
        }
    }

    /// <summary>
    /// G37: two elements that both simply write the same resource both run. The later one supersedes the
    /// earlier on purpose and loses nothing doing it, because the planner folded the earlier write into the
    /// body the later one sends — which is why a preemptive block on the second write is the wrong shape.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAnEarlierElementWroteTheResource_ALaterUpdateOnItStillRuns()
    {
        var runner = new FakeHealthModelWriteRunner();

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 2 },
            Element("patch", """ "select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"FIRST"}} """),
            Element("patch", """ "select":{"relationship":{"names":["web-api"]}},"patch":{"properties":{"displayName":"SECOND"}} """));

        Assert.Equal(["put:Relationship:web-api", "put:Relationship:web-api"], runner.Calls);
        Assert.True(Assert.Single(result.Changes[0].Targets!).Success);
        Assert.True(Assert.Single(result.Changes[1].Targets!).Success);
        Assert.Equal("SECOND", runner.Find(HealthModelResourceKind.Relationship, "web-api")!
            .Body["properties"]!["displayName"]!.GetValue<string>());
    }

    /// <summary>
    /// G38: "earlier" is the order the writes are issued in, not the order the caller listed them. Deletes
    /// run in a later phase, so the delete listed first here runs last and is blocked by the create listed
    /// second — and the skip reason has to name that second element, not an element before it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TheBlockingElementIsEarlierInExecutionOrderNotInInputOrder()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailOn.Add("put:SignalDefinition:cpu");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 2 },
            Element("delete", """ "label":"drop","select":{"signalDefinition":{"names":["cpu"]}} """),
            Element("create", """ "label":"recreate","name":"cpu","resource":{"signalDefinition":{"properties":{"signalKind":"AzureResourceMetric","displayName":"CPU v2"}}} """));

        // The create is phase 0 and runs first even though it was listed second; the delete never runs.
        Assert.Equal(["put:SignalDefinition:cpu"], runner.Calls);
        Assert.NotNull(runner.Find(HealthModelResourceKind.SignalDefinition, "cpu"));

        var skipped = Assert.Single(result.Changes[0].Targets!);
        Assert.Equal(HealthModelChangeAction.Skipped, skipped.Action);
        Assert.Contains("change 1 ('recreate')", skipped.SkipReason!, StringComparison.Ordinal);
        Assert.Contains("failed to write signal definition 'cpu'", skipped.SkipReason!, StringComparison.Ordinal);

        Assert.False(Assert.Single(result.Changes[1].Targets!).Success);
    }

    /// <summary>
    /// G39: a skipped target issued no write, so anything it blocks in turn must not be told it "failed to
    /// write". Only the element that actually reached the service failed; everything downstream of it was
    /// never attempted, and saying otherwise sends the caller after a call that never happened.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AcrossASkipChain_DescribesABlockerThatIssuedNoWriteAsNotAttempted()
    {
        var runner = new FakeHealthModelWriteRunner();
        runner.FailOn.Add("put:SignalDefinition:latency");

        var (result, _) = await Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 4 },
            Element("create", """ "label":"sig","name":"latency","resource":{"signalDefinition":{"properties":{"signalKind":"LogAnalyticsQuery","displayName":"Latency"}}} """),
            Element("create", """ "label":"ent","name":"web","resource":{"entity":{"properties":{"signalGroups":{"azureLogAnalytics":{"signals":[{"name":"p99","signalDefinitionName":"latency"}]}}}}} """),
            Element("patch", """ "label":"retitle","select":{"entity":{"names":["web"]}},"patch":{"properties":{"displayName":"Web front end"}} """),
            Element("create", """ "label":"edge","name":"web-db","resource":{"relationship":{"properties":{"parentEntityName":"web","childEntityName":"db"}}} """));

        Assert.Equal(["put:SignalDefinition:latency"], runner.Calls);

        // Level 1: the blocker really did issue a write and it really did fail.
        var entity = Assert.Single(result.Changes[1].Targets!);
        Assert.Equal(HealthModelChangeAction.Skipped, entity.Action);
        Assert.Equal(
            "signal definition 'latency' failed to write, so this target was not attempted.",
            entity.SkipReason);

        // Level 2: same resource as level 1, whose element never reached the service.
        var sameResource = Assert.Single(result.Changes[2].Targets!);
        Assert.Equal(HealthModelChangeAction.Skipped, sameResource.Action);
        Assert.Equal(
            "change 1 ('ent') was not attempted for entity 'web', so this target was not attempted.",
            sameResource.SkipReason);
        Assert.DoesNotContain("failed to write", sameResource.SkipReason!, StringComparison.Ordinal);

        // Level 3: depends on that same never-attempted entity.
        var dependent = Assert.Single(result.Changes[3].Targets!);
        Assert.Equal(HealthModelChangeAction.Skipped, dependent.Action);
        Assert.Equal(
            "entity 'web' was itself not attempted, so this target was not attempted.",
            dependent.SkipReason);
        Assert.DoesNotContain("failed to write", dependent.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// G27: a service that keeps handing back a continuation token is a paging bug, not a caller error, but
    /// the caller is the one who has to know the selector never saw the whole model. It must not read as an
    /// unexplained 500, and it must not write.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTheListingNeverEnds_FailsNamingThePartialViewAndWritesNothing()
    {
        // An empty page carrying a token: the shape that loops forever.
        var runner = new FakeHealthModelWriteRunner { PageSize = 0 };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(
            runner,
            HealthModelChangeMode.Apply,
            new HealthModelChangeExpectation { AffectedCount = 1 },
            Element("create", CreateCache)));

        Assert.Contains("partial view", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, runner.WriteCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsEveryPageBeforeResolvingASelector()
    {
        var runner = new FakeHealthModelWriteRunner { PageSize = 1 };

        var (result, _) = await Run(
            runner, HealthModelChangeMode.WhatIf, null, Element("patch", PatchWeb));

        // Three entities at one per page: the third page is what tells the reader it is done.
        Assert.Equal(3, runner.ListCallCounts[HealthModelResourceKind.Entity]);
        Assert.Equal(new HealthModelScanCounts(3, 2, 2), result.Changes[0].Scanned);
        Assert.Equal("web", Assert.Single(result.Changes[0].Targets!).Name);
    }
}

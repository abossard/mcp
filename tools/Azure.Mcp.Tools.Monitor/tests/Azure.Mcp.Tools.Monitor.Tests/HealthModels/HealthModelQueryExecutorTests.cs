// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// Tests for <see cref="HealthModelQueryExecutor"/> using an in-memory <see cref="IHealthModelCallRunner"/>
/// (no ARM) whose fixtures are built from real <c>Azure.ResourceManager.CloudHealth</c> SDK DTOs via
/// <see cref="ArmCloudHealthModelFactory"/>. Proves input-ordered slot filling (H3, H4), the SDK-payload
/// entity envelope (H5), per-entity partial-failure isolation (H7), and query-vs-entity error semantics
/// with a cached shared gate (H8), while preserving cancellation propagation.
/// </summary>
public class HealthModelQueryExecutorTests
{
    private const string Rg = "rg1";
    private const string Model = "modelA";
    private static readonly DateTimeOffset Snapshot = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset When = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly HealthModelEntityData[] MixedEntities =
    [
        Entity("e1", EntityHealthState.Healthy),
        Entity("e2", EntityHealthState.Unhealthy),
        Entity("e3", EntityHealthState.Degraded),
    ];

    private static HealthModelEntityData Entity(string name, EntityHealthState state) =>
        ArmCloudHealthModelFactory.HealthModelEntityData(
            name: name,
            properties: ArmCloudHealthModelFactory.HealthModelEntityProperties(healthState: state));

    private static async Task<IReadOnlyList<HealthModelQueryResult>> RunAsync(FakeCallRunner runner, params HealthModelQuery[] queries) =>
        await HealthModelQueryExecutor.ExecuteAsync(HealthModelQueryPlanner.Plan(queries), runner, CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_ReturnsOneResultPerInput_InInputOrder_WithSdkEntityPayloads()
    {
        var runner = new FakeCallRunner { Entities = MixedEntities, ListContinuationToken = "list-next" };
        var results = await RunAsync(runner,
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model },
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { Entity = "e1" } });

        // H3: one result per input, in input order, each carrying its zero-based queryIndex.
        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].QueryIndex);
        Assert.Equal(1, results[1].QueryIndex);

        // H5: entityList yields one universal node per entity, each holding the verbatim SDK entity payload.
        var list = results[0];
        Assert.True(list.Success);
        Assert.Equal("entityList", list.Kind);
        Assert.Equal(3, list.Entities!.Count);
        Assert.All(list.Entities!, n => Assert.True(n.Success));
        Assert.Equal(new[] { "e1", "e2", "e3" }, list.Entities!.Select(n => n.EntityName));
        Assert.Equal("Unhealthy", list.Entities![1].Entity!.Properties!.HealthState!.Value.ToString());
        Assert.Equal(new HealthModelQueryPage(false, 3, "list-next"), list.Page);

        // H5: entityHistory yields a node holding the SDK EntityHistoryResult payload.
        var history = results[1];
        Assert.True(history.Success);
        Assert.Equal("entityHistory", history.Kind);
        var node = Assert.Single(history.Entities!);
        Assert.Equal("e1", node.EntityName);
        Assert.NotNull(node.History);
        Assert.Equal("e1", node.History!.EntityName);
        Assert.Single(node.History.History);
        Assert.Equal(new HealthModelEntityPage(true, 1, null), node.Page);
    }

    [Fact]
    public async Task ExecuteAsync_FillsEverySlotOnce_ForDuplicateIdenticalQueries()
    {
        // H4: two identical queries dedupe to one call but every input slot is filled exactly once, sharing the payload.
        var runner = new FakeCallRunner { Entities = MixedEntities };
        var results = await RunAsync(runner,
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { Entity = "e1" } },
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { Entity = "e1" } });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(new[] { 0, 1 }, results.Select(r => r.QueryIndex));
        Assert.Equal(1, runner.HistoryCallCount); // deduped to a single ARM call
        Assert.All(results, r => Assert.Equal("e1", Assert.Single(r.Entities!).EntityName));
    }

    [Theory]
    [InlineData(HealthModelHealthFilter.Unhealthy, "e2")]
    [InlineData(HealthModelHealthFilter.NotHealthy, "e2,e3")]
    [InlineData(HealthModelHealthFilter.Degraded, "e3")]
    public async Task ExecuteAsync_RunsGateOnce_ThenFansOutOnlyToMatchingEntities(HealthModelHealthFilter filter, string expectedEntities)
    {
        var runner = new FakeCallRunner { Entities = MixedEntities };
        var results = await RunAsync(runner,
            new SignalHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Signal = "cpu", Target = new() { WhereHealth = filter } });

        // H8 gate: the shared entity list is fetched exactly once; only matching entities get per-entity calls.
        Assert.Equal(1, runner.ListCallCount);
        Assert.Equal(expectedEntities.Split(','), runner.SignalHistoryEntities);
        Assert.True(results[0].Success);
        Assert.Equal(expectedEntities.Split(','), results[0].Entities!.Select(n => n.EntityName));
        Assert.All(results[0].Entities!, n =>
        {
            Assert.True(n.Success);
            Assert.Equal("cpu", n.SignalName);
            Assert.NotNull(n.SignalHistory);
        });
    }

    [Fact]
    public async Task ExecuteAsync_UsesTimestampOverload_ForPointInTimeList()
    {
        var runner = new FakeCallRunner { Entities = MixedEntities };
        var results = await RunAsync(runner,
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model, AsOf = Snapshot });

        Assert.Equal(Snapshot, runner.LastListTimestamp);
        Assert.True(results[0].Success);
        Assert.Equal(3, results[0].Entities!.Count);
    }

    [Fact]
    public async Task ExecuteAsync_FanOutExposesIndependentDiscoveryAndEntityPages_WithSiblingIsolation()
    {
        var runner = new FakeCallRunner
        {
            Entities =
            [
                Entity("healthy", EntityHealthState.Healthy),
                Entity("failed", EntityHealthState.Unhealthy),
                Entity("page-a", EntityHealthState.Degraded),
                Entity("page-b", EntityHealthState.Unknown),
            ],
            ListContinuationToken = "discovery-next",
        };
        runner.ThrowForHistoryEntities.Add("failed");
        runner.HistoryNextMarkers["page-a"] = "entity-next-a";
        runner.HistoryNextMarkers["page-b"] = "entity-next-b";

        var result = Assert.Single(await RunAsync(runner,
            new EntityHistoryQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Target = new() { WhereHealth = HealthModelHealthFilter.NotHealthy },
            }));

        Assert.True(result.Success);
        Assert.Equal(new HealthModelQueryPage(false, 4, "discovery-next"), result.Page);
        Assert.Equal(["failed", "page-a", "page-b"], result.Entities!.Select(entity => entity.EntityName));
        Assert.False(result.Entities![0].Success);
        Assert.Null(result.Entities[0].Page);
        Assert.Equal(new HealthModelEntityPage(false, 1, "entity-next-a"), result.Entities[1].Page);
        Assert.Equal(new HealthModelEntityPage(false, 1, "entity-next-b"), result.Entities[2].Page);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyIncompleteDiscoveryPage_RemainsExplicit()
    {
        var runner = new FakeCallRunner
        {
            Entities = [Entity("healthy", EntityHealthState.Healthy)],
            ListContinuationToken = "more-entities",
        };

        var result = Assert.Single(await RunAsync(runner,
            new EntityHistoryQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Target = new() { WhereHealth = HealthModelHealthFilter.Unhealthy },
            }));

        Assert.True(result.Success);
        Assert.Empty(result.Entities!);
        Assert.Equal(new HealthModelQueryPage(false, 1, "more-entities"), result.Page);
    }

    [Fact]
    public async Task ExecuteAsync_ResumeDiscoveryTargetsOnlyCallerToken_AndRepeatsAcrossExecutions()
    {
        var runner = new FakeCallRunner { Entities = [Entity("e2", EntityHealthState.Unhealthy)] };
        var query = new EntityHistoryQuery
        {
            ResourceGroup = Rg,
            HealthModel = Model,
            Target = new() { WhereHealth = HealthModelHealthFilter.Unhealthy },
            Page = new() { Cursor = "opaque-list-token" },
        };

        await RunAsync(runner, query);
        await RunAsync(runner, query);

        Assert.Equal(["opaque-list-token", "opaque-list-token"], runner.ListContinuationTokens);
        Assert.Equal(2, runner.ListCallCount);
        Assert.Equal(2, runner.HistoryCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ResumeEntityPassesMarkerOnlyAndReturnsOnePage()
    {
        var runner = new FakeCallRunner();
        var result = Assert.Single(await RunAsync(runner,
            new EntityHistoryQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Target = new() { Entity = "e1" },
                Page = new() { Size = 17, Cursor = "opaque/+== marker" },
            }));

        Assert.Equal(["opaque/+== marker"], runner.HistoryInputMarkers);
        Assert.Equal(1, runner.HistoryCallCount);
        Assert.Equal(new HealthModelEntityPage(true, 1, null), Assert.Single(result.Entities!).Page);
    }

    [Fact]
    public async Task ExecuteAsync_DedupesFieldDifferences_AndShapesEachResultIndependently()
    {
        var runner = new FakeCallRunner();

        var results = await RunAsync(runner,
            new SignalHistoryQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Signal = "cpu",
                Target = new() { Entity = "e1" },
            },
            new SignalHistoryQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Signal = "cpu",
                Target = new() { Entity = "e1" },
                Select = [SignalHistorySelection.Context],
            });

        Assert.Equal(1, runner.SignalHistoryCallCount);
        Assert.False(Assert.Single(results[0].Entities!).Shape.Includes(HealthModelFieldGroup.Context));
        Assert.True(Assert.Single(results[1].Entities!).Shape.Includes(HealthModelFieldGroup.Context));
    }

    [Fact]
    public async Task ExecuteAsync_DifferentEntityMarkersNeverDedupe()
    {
        var runner = new FakeCallRunner();

        await RunAsync(runner,
            new EntityHistoryQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Target = new() { Entity = "e1" },
                Page = new() { Cursor = "marker-a" },
            },
            new EntityHistoryQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Target = new() { Entity = "e1" },
                Page = new() { Cursor = "marker-b" },
            });

        Assert.Equal(2, runner.HistoryCallCount);
        Assert.Equal(["marker-a", "marker-b"], runner.HistoryInputMarkers);
    }

    [Fact]
    public async Task ExecuteAsync_ImmediateRepeatedEntityMarker_IsExplicitEntityError()
    {
        var runner = new FakeCallRunner();
        runner.HistoryNextMarkers["e1"] = "same";

        var result = Assert.Single(await RunAsync(runner,
            new EntityHistoryQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Target = new() { Entity = "e1" },
                Page = new() { Cursor = "same" },
            }));

        var entity = Assert.Single(result.Entities!);
        Assert.False(entity.Success);
        Assert.Contains("did not advance", entity.Error);
        Assert.Null(entity.Page);
    }

    [Fact]
    public async Task ExecuteAsync_ImmediateRepeatedListContinuation_IsExplicitQueryError()
    {
        var runner = new FakeCallRunner { ListContinuationToken = "same" };

        var result = Assert.Single(await RunAsync(runner,
            new EntityListQuery
            {
                ResourceGroup = Rg,
                HealthModel = Model,
                Page = new() { Cursor = "same" },
            }));

        Assert.False(result.Success);
        Assert.Contains("did not advance", result.Error);
        Assert.Null(result.Page);
    }

    [Fact]
    public async Task ExecuteAsync_IsolatesFailingEntity_WhileSiblingsSucceed()
    {
        // H7: a fan-out where one target entity throws keeps the query success=true, records the failing
        // entity on its own node, and still returns the successful sibling with its payload.
        var runner = new FakeCallRunner { Entities = MixedEntities, ThrowForHistoryEntities = { "e2" } };
        var results = await RunAsync(runner,
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { WhereHealth = HealthModelHealthFilter.NotHealthy } });

        var query = results[0];
        Assert.True(query.Success); // whole-query success despite a failed entity
        Assert.Equal(2, query.Entities!.Count);

        var failed = Assert.Single(query.Entities!, n => n.EntityName == "e2");
        Assert.False(failed.Success);
        Assert.Contains("boom", failed.Error);
        Assert.Null(failed.History);

        var ok = Assert.Single(query.Entities!, n => n.EntityName == "e3");
        Assert.True(ok.Success);
        Assert.NotNull(ok.History);
        Assert.Equal("e3", ok.History!.EntityName);
    }

    [Fact]
    public async Task ExecuteAsync_FailedSharedGate_FailsEveryDependentQuery_AndListsOnce()
    {
        // H8: a failed shared entity-list gate makes every dependent (health-filtered) query a whole-query
        // failure carrying the gate error, and the list is attempted exactly once (no retry storm).
        var runner = new FakeCallRunner { Entities = MixedEntities, ThrowOnList = true };
        var results = await RunAsync(runner,
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { WhereHealth = HealthModelHealthFilter.NotHealthy } },
            new SignalHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Signal = "mem", Target = new() { WhereHealth = HealthModelHealthFilter.NotHealthy } });

        Assert.Equal(1, runner.ListCallCount);
        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Contains("list boom", r.Error);
            Assert.Null(r.Entities);
        });
    }

    [Fact]
    public async Task ExecuteAsync_FailedEntityListCall_IsWholeQueryFailure()
    {
        // H8: a failed explicit entity-list call is a whole-query failure (not an entity-level node error).
        var runner = new FakeCallRunner { Entities = MixedEntities, ThrowOnList = true };
        var results = await RunAsync(runner,
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model });

        Assert.False(results[0].Success);
        Assert.Contains("list boom", results[0].Error);
        Assert.Null(results[0].Entities);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation_InsteadOfReturningPerEntityError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new FakeCallRunner { Entities = MixedEntities };
        var plan = HealthModelQueryPlanner.Plan(
        [
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { Entity = "e1" } },
        ]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await HealthModelQueryExecutor.ExecuteAsync(plan, runner, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_IsolatesUnrelatedCancellation_AsEntityFailure_WhenSuppliedTokenNotCancelled()
    {
        // An OperationCanceledException NOT caused by the supplied token remains an isolated entity failure.
        var runner = new FakeCallRunner { Entities = MixedEntities, RaiseUnrelatedCancellationForHistory = true };
        var results = await RunAsync(runner,
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { Entity = "e1" } });

        Assert.True(results[0].Success);
        var node = Assert.Single(results[0].Entities!);
        Assert.False(node.Success);
        Assert.Contains("unrelated cancellation", node.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsWholeQueryFailure_ForPlannerDiagnostic()
    {
        var runner = new FakeCallRunner { Entities = MixedEntities };
        var results = await RunAsync(runner,
            new SignalHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { Entity = "e1" } }); // missing signal

        Assert.False(results[0].Success);
        Assert.Contains("signal is required", results[0].Error);
        Assert.Null(results[0].Entities);
        Assert.Equal(0, runner.ListCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersEachEntityListSlot_FromOneSharedListCall()
    {
        var runner = new FakeCallRunner { Entities = MixedEntities, ListContinuationToken = "list-next" };

        var results = await RunAsync(runner,
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model, WhereHealth = HealthModelHealthFilter.Unhealthy },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model, WhereHealth = HealthModelHealthFilter.NotHealthy });

        // Three differently filtered views cost exactly one list call.
        Assert.Equal(1, runner.ListCallCount);
        Assert.All(results, r => Assert.True(r.Success));

        Assert.Equal(new[] { "e1", "e2", "e3" }, results[0].Entities!.Select(n => n.EntityName));
        Assert.Equal(new[] { "e2" }, results[1].Entities!.Select(n => n.EntityName));
        Assert.Equal(new[] { "e2", "e3" }, results[2].Entities!.Select(n => n.EntityName));

        // returnedCount reports what each caller received; completeness stays the API's answer for all of them.
        Assert.Equal(new HealthModelQueryPage(false, 3, "list-next"), results[0].Page);
        Assert.Equal(new HealthModelQueryPage(false, 1, "list-next"), results[1].Page);
        Assert.Equal(new HealthModelQueryPage(false, 2, "list-next"), results[2].Page);
    }

    [Fact]
    public async Task ExecuteAsync_NotHealthyExcludesDeletedAndReportsEmptyFilteredPageAsComplete()
    {
        HealthModelEntityData[] entities =
        [
            Entity("healthy", EntityHealthState.Healthy),
            Entity("unhealthy", EntityHealthState.Unhealthy),
            Entity("degraded", EntityHealthState.Degraded),
            Entity("unknown", EntityHealthState.Unknown),
            Entity("deleted", EntityHealthState.Deleted),
        ];
        var runner = new FakeCallRunner { Entities = entities, ListContinuationToken = null };

        var results = await RunAsync(runner,
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model, WhereHealth = HealthModelHealthFilter.NotHealthy },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model, WhereHealth = HealthModelHealthFilter.Unknown },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model, WhereHealth = HealthModelHealthFilter.Degraded });

        Assert.Equal(1, runner.ListCallCount);

        // notHealthy is the closed set {Unhealthy, Degraded, Unknown}: Deleted is an extensible-enum state and stays out.
        Assert.Equal(new[] { "unhealthy", "degraded", "unknown" }, results[0].Entities!.Select(n => n.EntityName));
        Assert.Equal(new[] { "unknown" }, results[1].Entities!.Select(n => n.EntityName));

        // A page that carried entities but matched none is still the API's completed page, reported as an explicit zero.
        var noMatches = await RunAsync(
            new FakeCallRunner { Entities = [Entity("healthy", EntityHealthState.Healthy)], ListContinuationToken = null },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model, WhereHealth = HealthModelHealthFilter.Unhealthy });

        Assert.True(noMatches[0].Success);
        Assert.Empty(noMatches[0].Entities!);
        Assert.Equal(new HealthModelQueryPage(true, 0, null), noMatches[0].Page);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsServiceMessageWithStatusAndCode_WithoutTransportNoise()
    {
        // The SDK appends the status line, a verbatim body echo, and every response header to the service's own
        // sentence. Both the whole-query slot and the per-entity node must lead with the actionable text alone.
        var results = await RunAsync(
            new FakeCallRunner { Entities = MixedEntities, ListException = ServiceRejection() },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model });

        AssertConcise(results[0].Error);

        var perEntity = await RunAsync(
            new FakeCallRunner { Entities = MixedEntities, HistoryException = ServiceRejection() },
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = Model, Target = new() { Entity = "e1" } });

        AssertConcise(Assert.Single(perEntity[0].Entities!).Error);

        static void AssertConcise(string? error)
        {
            Assert.NotNull(error);
            Assert.Equal(
                "The value for startAt cannot be more than 30 days in the past. (HTTP 400, InvalidStartAt)",
                error);
            Assert.DoesNotContain("Headers:", error);
            Assert.DoesNotContain("x-ms-request-id", error);
            Assert.DoesNotContain("Content:", error);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DropsTheDumpWhenTheServiceSuppliedNoMessage()
    {
        // A body-less failure makes the SDK open its text with the decorated status line, so nothing of the
        // service's own survives. Falling back to the raw message would reinstate the whole header dump.
        var runner = new FakeCallRunner
        {
            ListException = new RequestFailedException(
                status: 403,
                message: "Status: 403 (Forbidden)\n\nContent:\n{\"error\":{\"code\":\"AuthorizationFailed\"}}\n\nHeaders:\nx-ms-request-id: deadbeef\n",
                errorCode: "AuthorizationFailed",
                innerException: null),
        };

        var results = await RunAsync(runner,
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model });

        Assert.Equal("HTTP 403, AuthorizationFailed", results[0].Error);
        Assert.DoesNotContain("Headers:", results[0].Error);
        Assert.DoesNotContain("x-ms-request-id", results[0].Error);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsAServiceMessageThatContainsItsOwnStatusLine()
    {
        // Only the SDK's decorated "Status: <code> (<reason>)" line is transport noise. A service sentence that
        // happens to start a line with "Status:" is the caller's answer and must survive.
        var runner = new FakeCallRunner
        {
            ListException = new RequestFailedException(
                status: 409,
                message: "Deployment is still running.\nStatus: pending approval\nStatus: 409 (Conflict)\nErrorCode: InProgress\n\nHeaders:\nx-ms-request-id: abc\n",
                errorCode: "InProgress",
                innerException: null),
        };

        var results = await RunAsync(runner,
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model });

        Assert.Equal(
            "Deployment is still running. Status: pending approval (HTTP 409, InProgress)",
            results[0].Error);
    }

    [Fact]
    public async Task ExecuteAsync_PassesThroughNonAzureExceptionMessagesUnchanged()
    {
        var results = await RunAsync(
            new FakeCallRunner { Entities = MixedEntities, ThrowOnList = true },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = Model });

        Assert.Equal("list boom", results[0].Error);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsRelationshipEdges_FromOneCallSharedByDuplicateQueries()
    {
        var runner = new FakeCallRunner
        {
            Entities = MixedEntities,
            Relationships =
            [
                Relationship("r-root-e2", parent: "root", child: "e2"),
                Relationship("r-root-e3", parent: "root", child: "e3"),
            ],
        };

        var results = await RunAsync(runner,
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = Model },
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = Model, Label = "same-call" });

        // One list call answers both slots; the dependency edges are readable without touching an entity.
        Assert.Equal(1, runner.RelationshipCallCount);
        Assert.Equal(0, runner.ListCallCount);
        Assert.All(results, result =>
        {
            Assert.True(result.Success);
            Assert.Equal("relationshipList", result.Kind);
            Assert.Null(result.Entities);
            Assert.Equal(["r-root-e2", "r-root-e3"], result.Relationships!.Select(item => item.Name));
            Assert.Equal(
                [("root", "e2"), ("root", "e3")],
                result.Relationships!.Select(item =>
                    (item.Relationship!.Properties!.ParentEntityName, item.Relationship!.Properties!.ChildEntityName)));
            Assert.Equal(new HealthModelQueryPage(true, 2, null), result.Page);
        });
    }

    [Fact]
    public async Task ExecuteAsync_ReportsModelScopeListPageAndResumesFromContinuationToken()
    {
        var firstPage = new FakeCallRunner
        {
            Relationships = [Relationship("r-root-e2", "root", "e2")],
            RelationshipContinuationToken = "page-2",
        };
        var resumed = new FakeCallRunner
        {
            Relationships = [Relationship("r-root-e3", "root", "e3")],
            RelationshipContinuationToken = null,
        };

        var incomplete = await RunAsync(firstPage,
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = Model });
        var complete = await RunAsync(resumed,
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = Model, Page = new() { Cursor = "page-2" } });

        Assert.Equal(new HealthModelQueryPage(false, 1, "page-2"), incomplete[0].Page);
        Assert.Equal(new HealthModelQueryPage(true, 1, null), complete[0].Page);

        // The caller's token is handed to the service untouched, never re-derived.
        Assert.Equal([null], firstPage.RelationshipContinuationTokens);
        Assert.Equal(["page-2"], resumed.RelationshipContinuationTokens);
    }

    [Fact]
    public async Task ExecuteAsync_RoutesTimestampToModelScopeList_AndFailsOnlyThatQuery()
    {
        var runner = new FakeCallRunner
        {
            Entities = MixedEntities,
            SignalDefinitions = [SignalDefinition("availability")],
            RelationshipException = new InvalidOperationException("relationship boom"),
        };

        var results = await RunAsync(runner,
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = Model, AsOf = Snapshot },
            new SignalDefinitionListQuery { ResourceGroup = Rg, HealthModel = Model });

        Assert.Equal([Snapshot], runner.RelationshipTimestamps);

        // A model-scope list has no per-item failure mode, so its failure is the whole query's, and it stops there.
        Assert.False(results[0].Success);
        Assert.Equal("relationship boom", results[0].Error);
        Assert.Null(results[0].Relationships);

        Assert.True(results[1].Success);
        Assert.Equal("signalDefinitionList", results[1].Kind);
        Assert.Equal(["availability"], results[1].SignalDefinitions!.Select(item => item.Name));
        Assert.Null(results[1].Relationships);
    }

    [Fact]
    public async Task ExecuteAsync_PassesTimestampAndContinuationToken_ToBothModelScopeCollections()
    {
        var runner = new FakeCallRunner
        {
            Relationships = [Relationship("r-root-e2", "root", "e2")],
            SignalDefinitions = [SignalDefinition("availability")],
        };

        await RunAsync(runner,
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = Model, AsOf = Snapshot },
            new SignalDefinitionListQuery { ResourceGroup = Rg, HealthModel = Model, AsOf = Snapshot },
            new SignalDefinitionListQuery { ResourceGroup = Rg, HealthModel = Model, Page = new() { Cursor = "defs-page-2" } });

        // Both collections route the caller's point-in-time and page inputs to the service unchanged.
        Assert.Equal([Snapshot], runner.RelationshipTimestamps);
        Assert.Equal([Snapshot, null], runner.SignalDefinitionTimestamps);
        Assert.Equal([null, "defs-page-2"], runner.SignalDefinitionContinuationTokens);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesEveryModelScopeCallKind()
    {
        // The behavioral guard for the single IsModelScopeList definition: admitting a kind there without
        // teaching the executor about it must fail, not silently borrow another collection's reader.
        var modelScopeKinds = Enum.GetValues<HealthModelCallKind>()
            .Where(HealthModelCallKinds.IsModelScopeList)
            .ToList();

        Assert.NotEmpty(modelScopeKinds);

        foreach (var callKind in modelScopeKinds)
        {
            var queryKind = callKind switch
            {
                HealthModelCallKind.ListRelationships => "relationshipList",
                HealthModelCallKind.ListSignalDefinitions => "signalDefinitionList",
                _ => throw new Xunit.Sdk.XunitException(
                    $"Call kind '{callKind}' is model-scope but this test has no query kind for it; " +
                    "the executor and this guard both need updating."),
            };

            var runner = new FakeCallRunner
            {
                Relationships = [Relationship("r-root-e2", "root", "e2")],
                SignalDefinitions = [SignalDefinition("availability")],
            };

            var results = await RunAsync(runner,
                HealthModelQueryPlannerTests.Minimal(queryKind));

            // Exactly one collection is populated, and it is the one matching the kind requested.
            Assert.True(results[0].Success);
            var populated = new[] { results[0].Relationships, results[0].SignalDefinitions }
                .Count(collection => collection is not null);
            Assert.Equal(1, populated);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AppliesFullFieldGroup_PerSlot_OnModelScopeLists()
    {
        var runner = new FakeCallRunner
        {
            Relationships = [Relationship("r-root-e2", "root", "e2")],
            SignalDefinitions = [SignalDefinition("availability")],
        };

        var results = await RunAsync(runner,
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = Model },
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = Model, Select = [FullSelection.Full] },
            new SignalDefinitionListQuery { ResourceGroup = Rg, HealthModel = Model },
            new SignalDefinitionListQuery { ResourceGroup = Rg, HealthModel = Model, Select = [FullSelection.Full] });

        // One call per collection still answers both of its slots, but each slot keeps its own projection.
        Assert.Equal(1, runner.RelationshipCallCount);
        Assert.Equal(1, runner.SignalDefinitionCallCount);
        var serialized = results
            .Select(result => JsonSerializer.Serialize(result, MonitorJsonContext.Default.HealthModelQueryResult))
            .ToList();
        Assert.DoesNotContain("\"type\"", serialized[0]);
        Assert.Contains("\"type\"", serialized[1]);
        Assert.DoesNotContain("\"type\"", serialized[2]);
        Assert.Contains("\"type\"", serialized[3]);
    }

    private static HealthModelRelationshipData Relationship(string name, string parent, string child) =>
        ArmCloudHealthModelFactory.HealthModelRelationshipData(
            name: name,
            properties: ArmCloudHealthModelFactory.HealthModelRelationshipProperties(
                parentEntityName: parent, childEntityName: child));

    private static HealthModelSignalDefinitionData SignalDefinition(string name) =>
        ArmCloudHealthModelFactory.HealthModelSignalDefinitionData(
            name: name,
            properties: ArmCloudHealthModelFactory.HealthModelSignalDefinitionProperties(displayName: name));

    /// <summary>The exact shape a CloudHealth 400 takes once the SDK has finished decorating it.</summary>
    private static RequestFailedException ServiceRejection() => new(
        status: 400,
        message:
            "The value for startAt cannot be more than 30 days in the past.\n" +
            "Status: 400 (Bad Request)\n" +
            "ErrorCode: InvalidStartAt\n" +
            "\n" +
            "Content:\n" +
            "{\"error\":{\"code\":\"InvalidStartAt\",\"message\":\"The value for startAt cannot be more than 30 days in the past.\"}}\n" +
            "\n" +
            "Headers:\n" +
            "Cache-Control: no-cache\n" +
            "Pragma: no-cache\n" +
            "x-ms-request-id: 11595bdc-20be-4b67-88ea-21e750891ac8\n",
        errorCode: "InvalidStartAt",
        innerException: null);

    private sealed class FakeCallRunner : IHealthModelCallRunner
    {
        public IReadOnlyList<HealthModelEntityData> Entities { get; set; } = [];
        public string? ListContinuationToken { get; set; }
        public int ListCallCount { get; private set; }
        public int HistoryCallCount { get; private set; }
        public int SignalHistoryCallCount { get; private set; }
        public DateTimeOffset? LastListTimestamp { get; private set; }
        public List<string?> ListContinuationTokens { get; } = [];
        public List<string?> HistoryInputMarkers { get; } = [];
        public List<string> SignalHistoryEntities { get; } = [];
        public Dictionary<string, string?> HistoryNextMarkers { get; } = [];
        public HashSet<string> ThrowForHistoryEntities { get; } = [];
        public Exception? HistoryException { get; set; }
        public bool RaiseUnrelatedCancellationForHistory { get; set; }
        public bool ThrowOnList { get; set; }
        public Exception? ListException { get; set; }
        public IReadOnlyList<HealthModelRelationshipData> Relationships { get; set; } = [];
        public IReadOnlyList<HealthModelSignalDefinitionData> SignalDefinitions { get; set; } = [];
        public string? RelationshipContinuationToken { get; set; }
        public string? SignalDefinitionContinuationToken { get; set; }
        public int RelationshipCallCount { get; private set; }
        public int SignalDefinitionCallCount { get; private set; }
        public List<DateTimeOffset?> RelationshipTimestamps { get; } = [];
        public List<string?> RelationshipContinuationTokens { get; } = [];
        public List<DateTimeOffset?> SignalDefinitionTimestamps { get; } = [];
        public List<string?> SignalDefinitionContinuationTokens { get; } = [];
        public Exception? RelationshipException { get; set; }

        public Task<HealthModelEntityListPage> ListEntitiesAsync(
            PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
        {
            ListCallCount++;
            LastListTimestamp = timestamp;
            ListContinuationTokens.Add(continuationToken);
            if (ListException is not null)
            {
                throw ListException;
            }
            if (ThrowOnList)
            {
                throw new InvalidOperationException("list boom");
            }
            return Task.FromResult(new HealthModelEntityListPage(Entities, ListContinuationToken));
        }

        public Task<HealthModelEntityData> GetEntityAsync(PlanScope scope, string entityName, CancellationToken cancellationToken) =>
            Task.FromResult(Entity(entityName, EntityHealthState.Healthy));

        public Task<HealthModelListPage<HealthModelRelationshipData>> ListRelationshipsAsync(
            PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
        {
            RelationshipCallCount++;
            RelationshipTimestamps.Add(timestamp);
            RelationshipContinuationTokens.Add(continuationToken);
            if (RelationshipException is not null)
            {
                throw RelationshipException;
            }
            return Task.FromResult(new HealthModelListPage<HealthModelRelationshipData>(
                Relationships, RelationshipContinuationToken));
        }

        public Task<HealthModelListPage<HealthModelSignalDefinitionData>> ListSignalDefinitionsAsync(
            PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
        {
            SignalDefinitionCallCount++;
            SignalDefinitionTimestamps.Add(timestamp);
            SignalDefinitionContinuationTokens.Add(continuationToken);
            return Task.FromResult(new HealthModelListPage<HealthModelSignalDefinitionData>(
                SignalDefinitions, SignalDefinitionContinuationToken));
        }

        public Task<EntityHistoryResult> GetHistoryAsync(
            PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
            string? nextMarker, CancellationToken cancellationToken)
        {
            HistoryCallCount++;
            HistoryInputMarkers.Add(nextMarker);
            cancellationToken.ThrowIfCancellationRequested();
            if (HistoryException is not null)
            {
                throw HistoryException;
            }
            if (RaiseUnrelatedCancellationForHistory)
            {
                throw new OperationCanceledException("unrelated cancellation not tied to the supplied token");
            }
            if (ThrowForHistoryEntities.Contains(entityName))
            {
                throw new InvalidOperationException($"boom: {entityName} not found");
            }
            var result = ArmCloudHealthModelFactory.EntityHistoryResult(
                entityName,
                [ArmCloudHealthModelFactory.HealthStateTransition(EntityHealthState.Degraded, EntityHealthState.Unhealthy, When, "reason")],
                HistoryNextMarkers.GetValueOrDefault(entityName));
            return Task.FromResult(result);
        }

        public Task<EntitySignalHistoryResult> GetSignalHistoryAsync(
            PlanScope scope, string entityName, string signalName, DateTimeOffset? startTime, DateTimeOffset? endTime,
            int? top, string? nextMarker, CancellationToken cancellationToken)
        {
            SignalHistoryCallCount++;
            SignalHistoryEntities.Add(entityName);
            var result = ArmCloudHealthModelFactory.EntitySignalHistoryResult(
                entityName, signalName,
                [ArmCloudHealthModelFactory.SignalHistoryDataPoint(When, 42, EntityHealthState.Degraded, "context")],
                nextMarker: null);
            return Task.FromResult(result);
        }

        public Task<EntityGetSignalRecommendationsResult> GetSignalRecommendationsAsync(PlanScope scope, string entityName, CancellationToken cancellationToken) =>
            Task.FromResult(ArmCloudHealthModelFactory.EntityGetSignalRecommendationsResult([], []));

        public Task<EntityGetDataAnnotationsResult> GetDataAnnotationsAsync(
            PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
            string? nextMarker, CancellationToken cancellationToken) =>
            Task.FromResult(ArmCloudHealthModelFactory.EntityGetDataAnnotationsResult(entityName, [], nextMarker: null));
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model },
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = Model, EntityName = "e1" });

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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = Model, EntityName = "e1" },
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = Model, EntityName = "e1" });

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
            new HealthModelQuery { Kind = HealthModelQueryKind.SignalHistory, ResourceGroup = Rg, HealthModel = Model, SignalName = "cpu", HealthFilter = filter });

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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model, Timestamp = Snapshot });

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
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.EntityHistory,
                ResourceGroup = Rg,
                HealthModel = Model,
                HealthFilter = HealthModelHealthFilter.NotHealthy,
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
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.EntityHistory,
                ResourceGroup = Rg,
                HealthModel = Model,
                HealthFilter = HealthModelHealthFilter.Unhealthy,
            }));

        Assert.True(result.Success);
        Assert.Empty(result.Entities!);
        Assert.Equal(new HealthModelQueryPage(false, 1, "more-entities"), result.Page);
    }

    [Fact]
    public async Task ExecuteAsync_ResumeDiscoveryTargetsOnlyCallerToken_AndRepeatsAcrossExecutions()
    {
        var runner = new FakeCallRunner { Entities = [Entity("e2", EntityHealthState.Unhealthy)] };
        var query = new HealthModelQuery
        {
            Kind = HealthModelQueryKind.EntityHistory,
            ResourceGroup = Rg,
            HealthModel = Model,
            HealthFilter = HealthModelHealthFilter.Unhealthy,
            ContinuationToken = "opaque-list-token",
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
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.EntityHistory,
                ResourceGroup = Rg,
                HealthModel = Model,
                EntityName = "e1",
                NextMarker = "opaque/+== marker",
                Top = 17,
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
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.SignalHistory,
                ResourceGroup = Rg,
                HealthModel = Model,
                EntityName = "e1",
                SignalName = "cpu",
            },
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.SignalHistory,
                ResourceGroup = Rg,
                HealthModel = Model,
                EntityName = "e1",
                SignalName = "cpu",
                Fields = [HealthModelFieldGroup.Context],
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
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.EntityHistory,
                ResourceGroup = Rg,
                HealthModel = Model,
                EntityName = "e1",
                NextMarker = "marker-a",
            },
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.EntityHistory,
                ResourceGroup = Rg,
                HealthModel = Model,
                EntityName = "e1",
                NextMarker = "marker-b",
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
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.EntityHistory,
                ResourceGroup = Rg,
                HealthModel = Model,
                EntityName = "e1",
                NextMarker = "same",
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
            new HealthModelQuery
            {
                Kind = HealthModelQueryKind.EntityList,
                ResourceGroup = Rg,
                HealthModel = Model,
                ContinuationToken = "same",
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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = Model, HealthFilter = HealthModelHealthFilter.NotHealthy });

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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = Model, HealthFilter = HealthModelHealthFilter.NotHealthy },
            new HealthModelQuery { Kind = HealthModelQueryKind.SignalHistory, ResourceGroup = Rg, HealthModel = Model, SignalName = "mem", HealthFilter = HealthModelHealthFilter.NotHealthy });

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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model });

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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = Model, EntityName = "e1" },
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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = Model, EntityName = "e1" });

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
            new HealthModelQuery { Kind = HealthModelQueryKind.SignalHistory, ResourceGroup = Rg, HealthModel = Model, EntityName = "e1" }); // missing signalName

        Assert.False(results[0].Success);
        Assert.Contains("signalName is required", results[0].Error);
        Assert.Null(results[0].Entities);
        Assert.Equal(0, runner.ListCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersEachEntityListSlot_FromOneSharedListCall()
    {
        var runner = new FakeCallRunner { Entities = MixedEntities, ListContinuationToken = "list-next" };

        var results = await RunAsync(runner,
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model },
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model, HealthFilter = HealthModelHealthFilter.Unhealthy },
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model, HealthFilter = HealthModelHealthFilter.NotHealthy });

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
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model, HealthFilter = HealthModelHealthFilter.NotHealthy },
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model, HealthFilter = HealthModelHealthFilter.Unknown },
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model, HealthFilter = HealthModelHealthFilter.Degraded });

        Assert.Equal(1, runner.ListCallCount);

        // notHealthy is the closed set {Unhealthy, Degraded, Unknown}: Deleted is an extensible-enum state and stays out.
        Assert.Equal(new[] { "unhealthy", "degraded", "unknown" }, results[0].Entities!.Select(n => n.EntityName));
        Assert.Equal(new[] { "unknown" }, results[1].Entities!.Select(n => n.EntityName));

        // A page that carried entities but matched none is still the API's completed page, reported as an explicit zero.
        var noMatches = await RunAsync(
            new FakeCallRunner { Entities = [Entity("healthy", EntityHealthState.Healthy)], ListContinuationToken = null },
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = Model, HealthFilter = HealthModelHealthFilter.Unhealthy });

        Assert.True(noMatches[0].Success);
        Assert.Empty(noMatches[0].Entities!);
        Assert.Equal(new HealthModelQueryPage(true, 0, null), noMatches[0].Page);
    }

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
        public bool RaiseUnrelatedCancellationForHistory { get; set; }
        public bool ThrowOnList { get; set; }

        public Task<HealthModelEntityListPage> ListEntitiesAsync(
            PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, CancellationToken cancellationToken)
        {
            ListCallCount++;
            LastListTimestamp = timestamp;
            ListContinuationTokens.Add(continuationToken);
            if (ThrowOnList)
            {
                throw new InvalidOperationException("list boom");
            }
            return Task.FromResult(new HealthModelEntityListPage(Entities, ListContinuationToken));
        }

        public Task<HealthModelEntityData> GetEntityAsync(PlanScope scope, string entityName, CancellationToken cancellationToken) =>
            Task.FromResult(Entity(entityName, EntityHealthState.Healthy));

        public Task<EntityHistoryResult> GetHistoryAsync(
            PlanScope scope, string entityName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top,
            string? nextMarker, CancellationToken cancellationToken)
        {
            HistoryCallCount++;
            HistoryInputMarkers.Add(nextMarker);
            cancellationToken.ThrowIfCancellationRequested();
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

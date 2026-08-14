// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;
using Azure.Mcp.Tools.Monitor.Planning;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// Direct, mock-free unit tests for the pure <see cref="HealthModelQueryPlanner"/>. Covers system-assigned
/// zero-based indexing and dedupe-by-<c>queryIndexes</c>, the echo-only label guarantee, and the two rules
/// the shape cannot express. Per-kind field combinations are no longer validated here: each query type
/// admits only its own inputs, so those cases live in <see cref="HealthModelQueryParserTests"/>.
/// </summary>
public class HealthModelQueryPlannerTests
{
    private const string Rg = "rg1";
    private const string ModelA = "modelA";
    private const string ModelB = "modelB";
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Snapshot = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    private static HealthModelWindow Window => new() { From = T0, To = T1 };
    private static HealthModelTarget Entity(string name) => new() { Entity = name };
    private static HealthModelTarget Where(HealthModelHealthFilter filter) => new() { WhereHealth = filter };

    // One rich fixture exercising grouping, dedupe + list-reuse, ordering and as-of routing. Duplicate
    // identical queries and duplicate/absent labels are all present so index aggregation and
    // label-inertness are genuinely exercised.
    private static IReadOnlyList<HealthModelQuery> RichBatch() =>
    [
        new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA, Label = "dup" },                                            // 0
        new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA, Label = "dup" },                                            // 1 dup of 0
        new SignalHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1"), Signal = "cpu", Window = Window }, // 2
        new SignalHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1"), Signal = "cpu", Window = Window }, // 3 dup of 2
        new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Where(HealthModelHealthFilter.Unhealthy) },      // 4 deferred, reuses list
        new SignalHistoryQuery { ResourceGroup = Rg, HealthModel = ModelB, Signal = "mem", Target = Where(HealthModelHealthFilter.NotHealthy) }, // 5 deferred in modelB -> gate list
        new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA, AsOf = Snapshot },                                          // 6 point-in-time
        new SignalHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1"), Signal = "cpu", Window = Window }, // 7 dup of 2
    ];

    [Fact]
    public void Plan_GroupsDedupesReusesOrdersAndRoutesAsOf_ByQueryIndex()
    {
        var batch = RichBatch();
        var plan = HealthModelQueryPlanner.Plan(batch);

        Assert.Empty(plan.Diagnostics);
        Assert.Equal(batch.Count, plan.QueryCount);

        // One group per (resourceGroup, healthModel), in first-seen order; calls grouped per model.
        Assert.Equal(2, plan.Groups.Count);
        var groupA = plan.Groups[0];
        var groupB = plan.Groups[1];
        Assert.Equal(new PlanScope(Rg, ModelA), groupA.Scope);
        Assert.Equal(new PlanScope(Rg, ModelB), groupB.Scope);
        Assert.All(groupA.Calls, c => Assert.Equal(groupA.Scope, c.Scope));
        Assert.All(groupB.Calls, c => Assert.Equal(groupB.Scope, c.Scope));

        // Exactly one current-time (as-of-less) entity list per model carries both explicit indexes.
        var currentListsA = groupA.Calls.Where(c => c.Kind == HealthModelCallKind.ListEntities && c.AsOf is null).ToList();
        Assert.Single(currentListsA);
        Assert.Equal(new[] { 0, 1 }, currentListsA[0].QueryIndexes);

        // Identical concrete signal-history queries collapse to a single call carrying every index.
        var concrete = Assert.Single(groupA.Calls, c => c.Kind == HealthModelCallKind.GetSignalHistory && c.EntityName == "e1");
        Assert.Equal(new[] { 2, 3, 7 }, concrete.QueryIndexes);
        Assert.Equal("cpu", concrete.SignalName);

        // Emitted call count is strictly below the naive one-call-per-query count.
        var emitted = plan.Groups.Sum(g => g.Calls.Count);
        Assert.True(emitted < batch.Count, $"expected emitted {emitted} < naive {batch.Count}");
        Assert.Equal(6, emitted);

        // Point-in-time entity list routed to a distinct list call carrying the as-of and its index.
        var snapshot = Assert.Single(groupA.Calls, c => c.Kind == HealthModelCallKind.ListEntities && c.AsOf is not null);
        Assert.Equal(Snapshot, snapshot.AsOf);
        Assert.Equal(new[] { 6 }, snapshot.QueryIndexes);

        // The shared entity list precedes the deferred health-filtered per-entity call it feeds.
        var deferredIndexA = groupA.Calls.ToList().FindIndex(c => c.IsDeferredPerEntity);
        var currentListIndexA = groupA.Calls.ToList().FindIndex(c => c.Kind == HealthModelCallKind.ListEntities && c.AsOf is null);
        Assert.True(currentListIndexA >= 0 && currentListIndexA < deferredIndexA, "entity list must precede dependent per-entity call");
        var deferredA = groupA.Calls[deferredIndexA];
        Assert.Equal(HealthModelCallKind.GetHistory, deferredA.Kind);
        Assert.Null(deferredA.EntityName);
        Assert.Equal(HealthModelHealthFilter.Unhealthy, deferredA.HealthFilter);
        Assert.Equal(new[] { 4 }, deferredA.QueryIndexes);

        // For modelB: a gate list (no direct query) is created and ordered before the deferred call.
        var listB = Assert.Single(groupB.Calls, c => c.Kind == HealthModelCallKind.ListEntities);
        Assert.Empty(listB.QueryIndexes);
        Assert.Equal(0, groupB.Calls.ToList().FindIndex(c => c.Kind == HealthModelCallKind.ListEntities));
        var deferredB = Assert.Single(groupB.Calls, c => c.IsDeferredPerEntity);
        Assert.Equal(HealthModelCallKind.GetSignalHistory, deferredB.Kind);
        Assert.Equal("mem", deferredB.SignalName);
        Assert.Equal(HealthModelHealthFilter.NotHealthy, deferredB.HealthFilter);
        Assert.Equal(new[] { 5 }, deferredB.QueryIndexes);
    }

    [Fact]
    public void Plan_IgnoresLabel_ForPlanningAndDedupe()
    {
        // A batch with duplicate labels (and absent labels) yields a plan identical to the same batch with
        // every label removed — label never influences grouping, dedupe, routing, or slots.
        var withLabels = RichBatch();
        var withoutLabels = RichBatch();
        foreach (var query in withoutLabels)
        {
            query.Label = null;
        }

        Assert.Equal(
            Describe(HealthModelQueryPlanner.Plan(withLabels)),
            Describe(HealthModelQueryPlanner.Plan(withoutLabels)));
    }

    [Fact]
    public void Plan_IsDeterministic_ForSameInput() =>
        Assert.Equal(
            Describe(HealthModelQueryPlanner.Plan(RichBatch())),
            Describe(HealthModelQueryPlanner.Plan(RichBatch())));

    [Fact]
    public void Plan_DedupesSelectionDifferences_ButNeverDifferentApiCursors()
    {
        HealthModelQuery[] queries =
        [
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1") },
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1"), Select = [FullSelection.Full] },
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1"), Page = new() { Cursor = "cursor-a" } },
            new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1"), Page = new() { Cursor = "cursor-b" } },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA, Page = new() { Cursor = "list-a" } },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA, Page = new() { Cursor = "list-b" } },
        ];

        var plan = HealthModelQueryPlanner.Plan(queries);

        Assert.Empty(plan.Diagnostics);
        var calls = Assert.Single(plan.Groups).Calls;
        var firstPage = Assert.Single(calls, call => call.Kind == HealthModelCallKind.GetHistory && call.Cursor is null);
        Assert.Equal([0, 1], firstPage.QueryIndexes);
        Assert.Equal(2, calls.Count(call => call.Kind == HealthModelCallKind.GetHistory && call.Cursor is not null));
        Assert.Equal(2, calls.Count(call => call.Kind == HealthModelCallKind.ListEntities));
    }

    [Theory]
    [InlineData("entityList", "ListEntities")]
    [InlineData("entityGet", "GetEntity")]
    [InlineData("entityHistory", "GetHistory")]
    [InlineData("signalHistory", "GetSignalHistory")]
    [InlineData("signalRecommendations", "GetSignalRecommendations")]
    [InlineData("dataAnnotations", "GetDataAnnotations")]
    [InlineData("relationshipList", "ListRelationships")]
    [InlineData("signalDefinitionList", "ListSignalDefinitions")]
    public void Plan_DispatchesEachKind_ToTheCorrectCall(string kind, string expectedCall)
    {
        var plan = HealthModelQueryPlanner.Plan([Minimal(kind)]);

        Assert.Empty(plan.Diagnostics);
        var call = Assert.Single(Assert.Single(plan.Groups).Calls);
        Assert.Equal(expectedCall, call.Kind.ToString());
        Assert.Equal(new[] { 0 }, call.QueryIndexes);
    }

    [Theory]
    [InlineData("entityHistory")]
    [InlineData("signalHistory")]
    [InlineData("signalRecommendations")]
    [InlineData("dataAnnotations")]
    public void Plan_RejectsATargetThatNamesBothOrNeither(string kind)
    {
        var both = Minimal(kind);
        SetTarget(both, new HealthModelTarget { Entity = "e1", WhereHealth = HealthModelHealthFilter.NotHealthy });
        var neither = Minimal(kind);
        SetTarget(neither, new HealthModelTarget());

        foreach (var query in new[] { both, neither })
        {
            var diagnostic = Assert.Single(HealthModelQueryPlanner.Plan([query]).Diagnostics);
            Assert.Contains("exactly one", diagnostic.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Plan_RejectsACursorCombinedWithAWindow()
    {
        var query = new EntityHistoryQuery
        {
            ResourceGroup = Rg,
            HealthModel = ModelA,
            Target = Entity("e1"),
            Window = Window,
            Page = new() { Cursor = "cursor-a" },
        };

        var diagnostic = Assert.Single(HealthModelQueryPlanner.Plan([query]).Diagnostics);
        Assert.Contains("cursor", diagnostic.Error, StringComparison.Ordinal);
        Assert.Contains("window", diagnostic.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_EmitsDiagnosticAtTheOffendingIndex_WithoutFailingSiblings()
    {
        var plan = HealthModelQueryPlanner.Plan(
        [
            new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA },
            new EntityGetQuery { ResourceGroup = Rg, HealthModel = ModelA },
            new EntityListQuery { ResourceGroup = "", HealthModel = ModelA },
        ]);

        Assert.Equal([1, 2], plan.Diagnostics.Select(d => d.QueryIndex));
        Assert.Contains("entity is required", plan.Diagnostics[0].Error, StringComparison.Ordinal);
        Assert.Contains("resourceGroup", plan.Diagnostics[1].Error, StringComparison.Ordinal);
        Assert.Single(Assert.Single(plan.Groups).Calls);
    }

    [Fact]
    public void Plan_ShareOneListCall_ForEntityListQueriesThatDifferOnlyByHealthFilter()
    {
        HealthModelQuery[] batch =
        [
            new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA, WhereHealth = HealthModelHealthFilter.Unhealthy },
            new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA, WhereHealth = HealthModelHealthFilter.NotHealthy },
        ];

        var plan = HealthModelQueryPlanner.Plan(batch);

        Assert.Empty(plan.Diagnostics);
        var call = Assert.Single(Assert.Single(plan.Groups).Calls);
        Assert.Equal(HealthModelCallKind.ListEntities, call.Kind);
        Assert.Equal(new[] { 0, 1, 2 }, call.QueryIndexes);
        Assert.Equal(
            [null, HealthModelHealthFilter.Unhealthy, HealthModelHealthFilter.NotHealthy],
            plan.ListFilters);
    }

    [Fact]
    public void Plan_DedupesModelScopeListCalls_AndKeepsPointInTimeAndPagesDistinct()
    {
        var plan = HealthModelQueryPlanner.Plan(
        [
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = ModelA, Label = "topology" },        // 0
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = ModelA, Label = "same-call" },       // 1 dup of 0; only the inert label differs
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = ModelA, AsOf = Snapshot },           // 2 point-in-time
            new RelationshipListQuery { ResourceGroup = Rg, HealthModel = ModelA, Page = new() { Cursor = "p2" } }, // 3 next page
            new SignalDefinitionListQuery { ResourceGroup = Rg, HealthModel = ModelA },                        // 4 different collection
        ]);

        Assert.Empty(plan.Diagnostics);
        var calls = Assert.Single(plan.Groups).Calls;

        Assert.Equal(4, calls.Count);
        Assert.Equal(
            [0, 1],
            Assert.Single(calls, c => c.Kind == HealthModelCallKind.ListRelationships && c.AsOf is null && c.Cursor is null).QueryIndexes);
        Assert.Equal([2], Assert.Single(calls, c => c.AsOf == Snapshot).QueryIndexes);
        Assert.Equal([3], Assert.Single(calls, c => c.Cursor == "p2").QueryIndexes);
        Assert.Equal([4], Assert.Single(calls, c => c.Kind == HealthModelCallKind.ListSignalDefinitions).QueryIndexes);

        Assert.DoesNotContain(calls, c => c.Kind == HealthModelCallKind.ListEntities);
        Assert.All(calls, c => Assert.False(c.IsDeferredPerEntity));
    }

    internal static HealthModelQuery Minimal(string kind) => kind switch
    {
        "entityList" => new EntityListQuery { ResourceGroup = Rg, HealthModel = ModelA },
        "entityGet" => new EntityGetQuery { ResourceGroup = Rg, HealthModel = ModelA, Entity = "e1" },
        "entityHistory" => new EntityHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1") },
        "signalHistory" => new SignalHistoryQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1"), Signal = "cpu" },
        "signalRecommendations" => new SignalRecommendationsQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1") },
        "dataAnnotations" => new DataAnnotationsQuery { ResourceGroup = Rg, HealthModel = ModelA, Target = Entity("e1") },
        "relationshipList" => new RelationshipListQuery { ResourceGroup = Rg, HealthModel = ModelA },
        "signalDefinitionList" => new SignalDefinitionListQuery { ResourceGroup = Rg, HealthModel = ModelA },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped kind."),
    };

    private static void SetTarget(HealthModelQuery query, HealthModelTarget target)
    {
        switch (query)
        {
            case EntityHistoryQuery q: q.Target = target; break;
            case SignalHistoryQuery q: q.Target = target; break;
            case SignalRecommendationsQuery q: q.Target = target; break;
            case DataAnnotationsQuery q: q.Target = target; break;
            default: throw new ArgumentOutOfRangeException(nameof(query), query.GetType(), "Kind has no target.");
        }
    }

    private static IReadOnlyList<string> Describe(ExecutionPlan plan) =>
    [
        .. plan.Groups.SelectMany(g => g.Calls.Select(c =>
            $"{g.Scope.ResourceGroup}/{g.Scope.HealthModel}|{c.Kind}|{c.EntityName}|{c.SignalName}|{c.AsOf}|{c.HealthFilter}|{c.Cursor}|{string.Join(',', c.QueryIndexes)}")),
        .. plan.Diagnostics.Select(d => $"diag:{d.QueryIndex}:{d.Error}"),
    ];
}

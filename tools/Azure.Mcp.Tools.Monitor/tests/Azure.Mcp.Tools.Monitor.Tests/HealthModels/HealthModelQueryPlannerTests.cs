// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Planning;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// Direct, mock-free unit tests for the pure <see cref="HealthModelQueryPlanner"/>. The planner is called
/// as a static function with no service, DI, ARM, or network involvement. Covers system-assigned
/// zero-based indexing and dedupe-by-<c>queryIndexes</c> (H3, H4), no id validation (H1), and the
/// echo-only, never-routing label guarantee (H2).
/// </summary>
public class HealthModelQueryPlannerTests
{
    private const string Rg = "rg1";
    private const string ModelA = "modelA";
    private const string ModelB = "modelB";
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Snapshot = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    // One rich fixture (no caller ids; correlation is the zero-based input position) exercising grouping,
    // dedupe + list-reuse, ordering and timestamp routing. Duplicate identical queries and duplicate/absent
    // labels are all present so index aggregation and label-inertness are genuinely exercised.
    private static readonly IReadOnlyList<HealthModelQuery> RichBatch =
    [
        new() { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelA, Label = "dup" },        // 0
        new() { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelA, Label = "dup" },        // 1 dup of 0
        new() { Kind = HealthModelQueryKind.SignalHistory, ResourceGroup = Rg, HealthModel = ModelA, EntityName = "e1", SignalName = "cpu", StartTime = T0, EndTime = T1 }, // 2
        new() { Kind = HealthModelQueryKind.SignalHistory, ResourceGroup = Rg, HealthModel = ModelA, EntityName = "e1", SignalName = "cpu", StartTime = T0, EndTime = T1 }, // 3 dup of 2
        new() { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = ModelA, HealthFilter = HealthModelHealthFilter.Unhealthy }, // 4 deferred, reuses list
        new() { Kind = HealthModelQueryKind.SignalHistory, ResourceGroup = Rg, HealthModel = ModelB, SignalName = "mem", HealthFilter = HealthModelHealthFilter.NotHealthy }, // 5 deferred in modelB -> gate list
        new() { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelA, Timestamp = Snapshot }, // 6 point-in-time
        new() { Kind = HealthModelQueryKind.SignalHistory, ResourceGroup = Rg, HealthModel = ModelA, EntityName = "e1", SignalName = "cpu", StartTime = T0, EndTime = T1 }, // 7 dup of 2
    ];

    [Fact]
    public void Plan_GroupsDedupesReusesOrdersAndRoutesTimestamp_ByQueryIndex()
    {
        var plan = HealthModelQueryPlanner.Plan(RichBatch);

        Assert.Empty(plan.Diagnostics);
        Assert.Equal(RichBatch.Count, plan.QueryCount);

        // One group per (resourceGroup, healthModel), in first-seen order; calls grouped per model.
        Assert.Equal(2, plan.Groups.Count);
        var groupA = plan.Groups[0];
        var groupB = plan.Groups[1];
        Assert.Equal(new PlanScope(Rg, ModelA), groupA.Scope);
        Assert.Equal(new PlanScope(Rg, ModelB), groupB.Scope);
        Assert.All(groupA.Calls, c => Assert.Equal(groupA.Scope, c.Scope));
        Assert.All(groupB.Calls, c => Assert.Equal(groupB.Scope, c.Scope));

        // Exactly one current-time (timestamp-less) entity list per model carries both explicit indexes.
        var currentListsA = groupA.Calls.Where(c => c.Kind == HealthModelCallKind.ListEntities && c.Timestamp is null).ToList();
        Assert.Single(currentListsA);
        Assert.Equal(new[] { 0, 1 }, currentListsA[0].QueryIndexes);

        // Identical concrete signal-history queries collapse to a single call carrying every index (H4 dedupe).
        var concrete = Assert.Single(groupA.Calls, c => c.Kind == HealthModelCallKind.GetSignalHistory && c.EntityName == "e1");
        Assert.Equal(new[] { 2, 3, 7 }, concrete.QueryIndexes);
        Assert.Equal("cpu", concrete.SignalName);

        // Emitted call count is strictly below the naive one-call-per-query count.
        var emitted = plan.Groups.Sum(g => g.Calls.Count);
        Assert.True(emitted < RichBatch.Count, $"expected emitted {emitted} < naive {RichBatch.Count}");
        Assert.Equal(6, emitted);

        // Point-in-time entity list routed to a distinct list call carrying the timestamp and its index.
        var snapshot = Assert.Single(groupA.Calls, c => c.Kind == HealthModelCallKind.ListEntities && c.Timestamp is not null);
        Assert.Equal(Snapshot, snapshot.Timestamp);
        Assert.Equal(new[] { 6 }, snapshot.QueryIndexes);

        // The shared entity list precedes the deferred health-filtered per-entity call it feeds.
        var deferredIndexA = groupA.Calls.ToList().FindIndex(c => c.IsDeferredPerEntity);
        var currentListIndexA = groupA.Calls.ToList().FindIndex(c => c.Kind == HealthModelCallKind.ListEntities && c.Timestamp is null);
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
        // H2: a batch with duplicate labels (and absent labels) yields a plan byte-identical to the same
        // batch with every label removed — label never influences grouping, dedupe, routing, or slots.
        var withLabels = RichBatch;
        var withoutLabels = RichBatch
            .Select(q => new HealthModelQuery
            {
                Kind = q.Kind,
                ResourceGroup = q.ResourceGroup,
                HealthModel = q.HealthModel,
                EntityName = q.EntityName,
                SignalName = q.SignalName,
                StartTime = q.StartTime,
                EndTime = q.EndTime,
                Top = q.Top,
                Timestamp = q.Timestamp,
                HealthFilter = q.HealthFilter,
                Label = null,
            })
            .ToList();

        Assert.Equal(Describe(HealthModelQueryPlanner.Plan(withLabels)), Describe(HealthModelQueryPlanner.Plan(withoutLabels)));
    }

    [Fact]
    public void Plan_IsDeterministic_ForSameInput()
    {
        var first = HealthModelQueryPlanner.Plan(RichBatch);
        var second = HealthModelQueryPlanner.Plan(RichBatch);

        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public void Plan_DedupesFieldDifferences_ButNeverDifferentApiContinuations()
    {
        HealthModelQuery[] queries =
        [
            new() { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = ModelA, EntityName = "e1" },
            new() { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = ModelA, EntityName = "e1", Fields = [HealthModelFieldGroup.Full] },
            new() { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = ModelA, EntityName = "e1", NextMarker = "marker-a" },
            new() { Kind = HealthModelQueryKind.EntityHistory, ResourceGroup = Rg, HealthModel = ModelA, EntityName = "e1", NextMarker = "marker-b" },
            new() { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelA, ContinuationToken = "list-a" },
            new() { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelA, ContinuationToken = "list-b" },
        ];

        var plan = HealthModelQueryPlanner.Plan(queries);

        Assert.Empty(plan.Diagnostics);
        var calls = Assert.Single(plan.Groups).Calls;
        var firstPage = Assert.Single(calls, call => call.Kind == HealthModelCallKind.GetHistory && call.NextMarker is null);
        Assert.Equal([0, 1], firstPage.QueryIndexes);
        Assert.Equal(2, calls.Count(call => call.Kind == HealthModelCallKind.GetHistory && call.NextMarker is not null));
        Assert.Equal(2, calls.Count(call => call.Kind == HealthModelCallKind.ListEntities));
    }

    [Theory]
    [InlineData(HealthModelQueryKind.EntityList, HealthModelFieldGroup.Identity)]
    [InlineData(HealthModelQueryKind.EntityGet, HealthModelFieldGroup.Audit)]
    [InlineData(HealthModelQueryKind.EntityHistory, HealthModelFieldGroup.Full)]
    [InlineData(HealthModelQueryKind.SignalHistory, HealthModelFieldGroup.Context)]
    [InlineData(HealthModelQueryKind.SignalRecommendations, HealthModelFieldGroup.Configurations)]
    [InlineData(HealthModelQueryKind.DataAnnotations, HealthModelFieldGroup.Details)]
    public void Plan_AcceptsApplicableFieldGroups(HealthModelQueryKind kind, HealthModelFieldGroup field)
    {
        var query = ValidQuery(kind);
        query.Fields = [field];

        var plan = HealthModelQueryPlanner.Plan([query]);

        Assert.Empty(plan.Diagnostics);
    }

    [Theory]
    [InlineData(HealthModelQueryKind.EntityList, HealthModelFieldGroup.Context)]
    [InlineData(HealthModelQueryKind.EntityHistory, HealthModelFieldGroup.Identity)]
    [InlineData(HealthModelQueryKind.SignalHistory, HealthModelFieldGroup.Details)]
    [InlineData(HealthModelQueryKind.SignalRecommendations, HealthModelFieldGroup.Layout)]
    [InlineData(HealthModelQueryKind.DataAnnotations, HealthModelFieldGroup.Configurations)]
    public void Plan_RejectsKnownButInapplicableFieldGroupAtOffendingIndex(HealthModelQueryKind kind, HealthModelFieldGroup field)
    {
        var query = ValidQuery(kind);
        query.Fields = [field];

        var plan = HealthModelQueryPlanner.Plan(
        [
            new HealthModelQuery { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelB },
            query,
        ]);

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(1, diagnostic.QueryIndex);
        Assert.Contains(field.ToString(), diagnostic.Error);
        Assert.Contains(kind.ToString(), diagnostic.Error);
    }

    [Theory]
    [InlineData(HealthModelQueryKind.EntityList, false, false, "nextMarker")]
    [InlineData(HealthModelQueryKind.EntityGet, true, false, "nextMarker")]
    [InlineData(HealthModelQueryKind.SignalRecommendations, true, false, "nextMarker")]
    [InlineData(HealthModelQueryKind.EntityHistory, false, true, "concrete entityName")]
    [InlineData(HealthModelQueryKind.EntityHistory, true, true, "startTime")]
    public void Plan_RejectsInvalidEntityMarkerUse(
        HealthModelQueryKind kind, bool concreteEntity, bool includeWindow, string expected)
    {
        var query = ValidQuery(kind);
        query.EntityName = concreteEntity ? "e1" : null;
        query.HealthFilter = concreteEntity ? null : HealthModelHealthFilter.NotHealthy;
        query.NextMarker = "marker";
        query.StartTime = includeWindow ? T0 : null;

        var diagnostic = Assert.Single(HealthModelQueryPlanner.Plan([query]).Diagnostics);

        Assert.Contains(expected, diagnostic.Error);
    }

    [Theory]
    [InlineData(HealthModelQueryKind.EntityGet, true)]
    [InlineData(HealthModelQueryKind.EntityHistory, true)]
    [InlineData(HealthModelQueryKind.SignalRecommendations, true)]
    public void Plan_RejectsListContinuationWithoutEntityListOrHealthFilter(HealthModelQueryKind kind, bool concreteEntity)
    {
        var query = ValidQuery(kind);
        query.EntityName = concreteEntity ? "e1" : null;
        query.HealthFilter = concreteEntity ? null : HealthModelHealthFilter.NotHealthy;
        query.ContinuationToken = "list-token";

        var diagnostic = Assert.Single(HealthModelQueryPlanner.Plan([query]).Diagnostics);

        Assert.Contains("continuationToken", diagnostic.Error);
    }

    private static IReadOnlyList<string> Describe(ExecutionPlan plan) =>
    [
        .. plan.Groups.SelectMany(g => g.Calls.Select(c =>
            $"{g.Scope.ResourceGroup}/{g.Scope.HealthModel}|{c.Kind}|{c.EntityName}|{c.SignalName}|{c.Timestamp}|{c.HealthFilter}|{c.NextMarker}|{c.ContinuationToken}|{string.Join(',', c.QueryIndexes)}")),
        .. plan.Diagnostics.Select(d => $"diag:{d.QueryIndex}:{d.Error}"),
    ];

    [Theory]
    [InlineData(HealthModelQueryKind.EntityList, "ListEntities")]
    [InlineData(HealthModelQueryKind.EntityGet, "GetEntity")]
    [InlineData(HealthModelQueryKind.EntityHistory, "GetHistory")]
    [InlineData(HealthModelQueryKind.SignalHistory, "GetSignalHistory")]
    [InlineData(HealthModelQueryKind.SignalRecommendations, "GetSignalRecommendations")]
    [InlineData(HealthModelQueryKind.DataAnnotations, "GetDataAnnotations")]
    public void Plan_DispatchesEachKind_ToTheCorrectCall(HealthModelQueryKind kind, string expectedCall)
    {
        var query = new HealthModelQuery
        {
            Kind = kind,
            ResourceGroup = Rg,
            HealthModel = ModelA,
            EntityName = kind == HealthModelQueryKind.EntityList ? null : "e1",
            SignalName = kind == HealthModelQueryKind.SignalHistory ? "cpu" : null,
            StartTime = T0,
            EndTime = T1,
            Top = 50,
        };

        var plan = HealthModelQueryPlanner.Plan([query]);

        Assert.Empty(plan.Diagnostics);
        var call = Assert.Single(Assert.Single(plan.Groups).Calls);
        Assert.Equal(expectedCall, call.Kind.ToString());
        Assert.Equal(new[] { 0 }, call.QueryIndexes);

        if (kind == HealthModelQueryKind.SignalHistory)
        {
            Assert.Equal("cpu", call.SignalName);
        }

        if (kind is HealthModelQueryKind.EntityHistory or HealthModelQueryKind.DataAnnotations)
        {
            Assert.Equal(T0, call.StartTime);
            Assert.Equal(T1, call.EndTime);
            Assert.Equal(50, call.Top);
        }
    }

    [Theory]
    [InlineData(HealthModelQueryKind.EntityList, "", ModelA, null, null, "resourceGroup and healthModel are required")]
    [InlineData(HealthModelQueryKind.EntityGet, Rg, ModelA, null, null, "entityName is required for entityGet")]
    [InlineData(HealthModelQueryKind.SignalHistory, Rg, ModelA, "e1", null, "signalName is required for signalHistory")]
    [InlineData(HealthModelQueryKind.EntityHistory, Rg, ModelA, null, null, "entityName or healthFilter is required")]
    public void Plan_EmitsDiagnostic_ForInvalidQuery(
        HealthModelQueryKind kind, string resourceGroup, string model, string? entity, string? signal, string expected)
    {
        var query = new HealthModelQuery
        {
            Kind = kind,
            ResourceGroup = resourceGroup,
            HealthModel = model,
            EntityName = entity,
            SignalName = signal,
        };

        var plan = HealthModelQueryPlanner.Plan([query]);

        Assert.Empty(plan.Groups);
        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(0, diagnostic.QueryIndex);
        Assert.Contains(expected, diagnostic.Error);
    }

    [Theory]
    [InlineData(HealthModelQueryKind.EntityGet, "e1", HealthModelHealthFilter.Unhealthy, "mutually exclusive")]
    [InlineData(HealthModelQueryKind.EntityGet, null, HealthModelHealthFilter.NotHealthy, "healthFilter is not supported for entityGet")]
    [InlineData(HealthModelQueryKind.EntityHistory, "e1", HealthModelHealthFilter.Unhealthy, "mutually exclusive")]
    [InlineData(HealthModelQueryKind.EntityList, "e1", HealthModelHealthFilter.Degraded, "mutually exclusive")]
    [InlineData(HealthModelQueryKind.SignalRecommendations, "e1", HealthModelHealthFilter.Unknown, "mutually exclusive")]
    public void Plan_RejectsUnsupportedTargetingCombination(
        HealthModelQueryKind kind, string? entity, HealthModelHealthFilter filter, string expected)
    {
        var query = new HealthModelQuery
        {
            Kind = kind,
            ResourceGroup = Rg,
            HealthModel = ModelA,
            EntityName = entity,
            SignalName = kind == HealthModelQueryKind.SignalHistory ? "cpu" : null,
            HealthFilter = filter,
        };

        var plan = HealthModelQueryPlanner.Plan([query]);

        Assert.Empty(plan.Groups);
        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(0, diagnostic.QueryIndex);
        Assert.Contains(expected, diagnostic.Error);
    }

    [Fact]
    public void Plan_RejectsOutOfRangeHealthFilter_RatherThanMatchingNothing()
    {
        var query = new HealthModelQuery
        {
            Kind = HealthModelQueryKind.EntityHistory,
            ResourceGroup = Rg,
            HealthModel = ModelA,
            HealthFilter = (HealthModelHealthFilter)77,
        };

        var plan = HealthModelQueryPlanner.Plan([query]);

        Assert.Empty(plan.Groups);
        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Contains("'77' is not a valid health filter", diagnostic.Error);
    }

    [Fact]
    public void Plan_ShareOneListCall_ForEntityListQueriesThatDifferOnlyByHealthFilter()
    {
        IReadOnlyList<HealthModelQuery> batch =
        [
            new() { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelA },
            new() { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelA, HealthFilter = HealthModelHealthFilter.Unhealthy },
            new() { Kind = HealthModelQueryKind.EntityList, ResourceGroup = Rg, HealthModel = ModelA, HealthFilter = HealthModelHealthFilter.NotHealthy },
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

    private static HealthModelQuery ValidQuery(HealthModelQueryKind kind) => new()
    {
        Kind = kind,
        ResourceGroup = Rg,
        HealthModel = ModelA,
        EntityName = kind == HealthModelQueryKind.EntityList ? null : "e1",
        SignalName = kind == HealthModelQueryKind.SignalHistory ? "cpu" : null,
    };
}

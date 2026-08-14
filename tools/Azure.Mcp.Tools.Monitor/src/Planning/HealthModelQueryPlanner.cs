// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Pure, stateless transformation from a batch of <see cref="HealthModelQuery"/> into an immutable
/// <see cref="ExecutionPlan"/>. It normalizes queries, groups them by (resource group, health model) so
/// calls against one model are contiguous and share a single model resolution, deduplicates calls that
/// reduce to the same Azure Resource Manager request, reuses one entity list per model to resolve
/// health-filtered queries, and orders each group so the entity list precedes the per-entity calls that
/// depend on it.
///
/// The planner has no I/O, service, ARM, or network dependency; it is a deterministic function of its
/// input and is exercised directly by unit tests. It carries no per-kind field-combination rules: each
/// query type admits only the inputs its kind accepts, so those combinations are rejected at
/// deserialization rather than re-checked here.
/// </summary>
internal static class HealthModelQueryPlanner
{
    internal static ExecutionPlan Plan(IReadOnlyList<HealthModelQuery> queries)
    {
        ArgumentNullException.ThrowIfNull(queries);

        var diagnostics = new List<PlanDiagnostic>();
        var normalized = new List<NormalizedQuery>();

        for (var index = 0; index < queries.Count; index++)
        {
            var query = queries[index];
            if (HealthModelQueryNormalizer.TryNormalize(query, out var inputs, out var error))
            {
                normalized.Add(new NormalizedQuery(
                    query.QueryKind,
                    inputs,
                    index,
                    MapKind(query.QueryKind),
                    new PlanScope(query.ResourceGroup, query.HealthModel),
                    IsDeferred: IsPerEntity(query.QueryKind) && string.IsNullOrWhiteSpace(inputs.EntityName)));
            }
            else
            {
                diagnostics.Add(new PlanDiagnostic(index, query.Kind, error));
            }
        }

        var groups = new List<PlanGroup>();
        foreach (var scope in DistinctScopesInOrder(normalized))
        {
            groups.Add(BuildGroup(scope, [.. normalized.Where(n => n.Scope == scope)]));
        }

        return new ExecutionPlan(
            groups,
            diagnostics,
            queries.Count,
            [.. queries.Select((_, index) => ShapeOf(normalized, index))],
            [.. queries.Select((_, index) => ListFilterOf(normalized, index))]);
    }

    private static HealthModelResultShape ShapeOf(List<NormalizedQuery> normalized, int index) =>
        normalized.FirstOrDefault(n => n.Index == index)?.Inputs.Shape ?? HealthModelResultShape.Compact;

    /// <summary>
    /// The health filter an entity-list slot applies to the shared page it received. Only entity lists
    /// filter in place; every other kind fans out instead.
    /// </summary>
    private static HealthModelHealthFilter? ListFilterOf(List<NormalizedQuery> normalized, int index) =>
        normalized.FirstOrDefault(n => n.Index == index) is { Kind: HealthModelQueryKind.EntityList } entityList
            ? entityList.Inputs.WhereHealth
            : null;

    private static PlanGroup BuildGroup(PlanScope scope, IReadOnlyList<NormalizedQuery> scopeQueries)
    {
        // 0) Model-scope collection lists (relationships, signal definitions): one call per distinct
        //    (collection, as-of, page). They depend on nothing and nothing depends on them.
        var modelScopeCalls = DeduplicateCalls(
            scopeQueries.Where(n => HealthModelCallKinds.IsModelScopeList(n.CallKind)),
            keySelector: n => (n.CallKind, n.Inputs.AsOf, n.Inputs.Cursor),
            callFactory: (n, indexes) => ListCall(n.CallKind, scope, n.Inputs.AsOf, n.Inputs.Cursor, indexes));

        // 1) Explicit entity-list queries, one call per distinct as-of and page.
        var listCalls = DeduplicateCalls(
            scopeQueries.Where(n => n.CallKind == HealthModelCallKind.ListEntities),
            keySelector: n => (n.Inputs.AsOf, n.Inputs.Cursor),
            callFactory: (n, indexes) => ListCall(HealthModelCallKind.ListEntities, scope, n.Inputs.AsOf, n.Inputs.Cursor, indexes));

        // 2) One shared current-time entity-list page gates each distinct health-filter discovery page.
        foreach (var cursor in scopeQueries
                     .Where(query => query.IsDeferred)
                     .Select(query => query.Inputs.Cursor)
                     .Distinct(StringComparer.Ordinal))
        {
            if (!listCalls.Any(call => call.AsOf is null && string.Equals(call.Cursor, cursor, StringComparison.Ordinal)))
            {
                listCalls = [.. listCalls, ListCall(HealthModelCallKind.ListEntities, scope, asOf: null, cursor, queryIndexes: [])];
            }
        }

        // 3) Concrete entity-scoped calls (fixed entity), deduplicated by call shape. Entity get is
        //     entity-scoped but never deferred, so it belongs here rather than with the fan-out kinds.
        var concreteCalls = DeduplicateCalls(
            scopeQueries.Where(n => !n.IsDeferred && (IsPerEntity(n.Kind) || n.Kind == HealthModelQueryKind.EntityGet)),
            keySelector: n => (n.CallKind, n.Inputs.EntityName, n.Inputs.SignalName, n.Inputs.From, n.Inputs.To, n.Inputs.Size, n.Inputs.Cursor),
            callFactory: (n, indexes) => PerEntityCall(scope, n, n.Inputs.EntityName, healthFilter: null, indexes));

        // 4) Deferred per-entity calls (health-filtered), deduplicated by call shape.
        var deferredCalls = DeduplicateCalls(
            scopeQueries.Where(n => n.IsDeferred),
            keySelector: n => (n.CallKind, (string?)null, n.Inputs.SignalName, n.Inputs.From, n.Inputs.To, n.Inputs.Size, n.Inputs.WhereHealth, n.Inputs.Cursor),
            callFactory: (n, indexes) => PerEntityCall(scope, n, entityName: null, n.Inputs.WhereHealth, indexes));

        // Ordering: model-scope lists, then entity lists, then concrete per-entity, then deferred per-entity.
        var ordered = new List<PlannedCall>(modelScopeCalls.Count + listCalls.Count + concreteCalls.Count + deferredCalls.Count);
        ordered.AddRange(modelScopeCalls);
        ordered.AddRange(listCalls);
        ordered.AddRange(concreteCalls);
        ordered.AddRange(deferredCalls);

        return new PlanGroup(scope, ordered);
    }

    private static List<PlannedCall> DeduplicateCalls<TKey>(
        IEnumerable<NormalizedQuery> source,
        Func<NormalizedQuery, TKey> keySelector,
        Func<NormalizedQuery, IReadOnlyList<int>, PlannedCall> callFactory)
        where TKey : notnull
    {
        var order = new List<TKey>();
        var firstByKey = new Dictionary<TKey, NormalizedQuery>();
        var indexesByKey = new Dictionary<TKey, List<int>>();

        foreach (var norm in source)
        {
            var key = keySelector(norm);
            if (!indexesByKey.TryGetValue(key, out var indexes))
            {
                indexes = [];
                indexesByKey[key] = indexes;
                firstByKey[key] = norm;
                order.Add(key);
            }
            indexes.Add(norm.Index);
        }

        return [.. order.Select(key => callFactory(firstByKey[key], indexesByKey[key]))];
    }

    private static PlannedCall ListCall(
        HealthModelCallKind kind, PlanScope scope, DateTimeOffset? asOf, string? cursor, IReadOnlyList<int> queryIndexes) =>
        new(kind, scope, EntityName: null, SignalName: null,
            From: null, To: null, Size: null, AsOf: asOf, HealthFilter: null, cursor, queryIndexes);

    private static PlannedCall PerEntityCall(
        PlanScope scope, NormalizedQuery norm, string? entityName, HealthModelHealthFilter? healthFilter,
        IReadOnlyList<int> queryIndexes) =>
        new(norm.CallKind, scope, entityName, norm.Inputs.SignalName,
            norm.Inputs.From, norm.Inputs.To, norm.Inputs.Size, AsOf: null, healthFilter,
            norm.Inputs.Cursor, queryIndexes);

    private static IEnumerable<PlanScope> DistinctScopesInOrder(IEnumerable<NormalizedQuery> normalized)
    {
        var seen = new HashSet<PlanScope>();
        foreach (var norm in normalized)
        {
            if (seen.Add(norm.Scope))
            {
                yield return norm.Scope;
            }
        }
    }

    private static bool IsPerEntity(HealthModelQueryKind kind) =>
        kind is HealthModelQueryKind.EntityHistory or HealthModelQueryKind.SignalHistory
            or HealthModelQueryKind.SignalRecommendations or HealthModelQueryKind.DataAnnotations;

    internal static HealthModelCallKind MapKind(HealthModelQueryKind kind) => kind switch
    {
        HealthModelQueryKind.EntityList => HealthModelCallKind.ListEntities,
        HealthModelQueryKind.EntityGet => HealthModelCallKind.GetEntity,
        HealthModelQueryKind.EntityHistory => HealthModelCallKind.GetHistory,
        HealthModelQueryKind.SignalHistory => HealthModelCallKind.GetSignalHistory,
        HealthModelQueryKind.SignalRecommendations => HealthModelCallKind.GetSignalRecommendations,
        HealthModelQueryKind.DataAnnotations => HealthModelCallKind.GetDataAnnotations,
        HealthModelQueryKind.RelationshipList => HealthModelCallKind.ListRelationships,
        HealthModelQueryKind.SignalDefinitionList => HealthModelCallKind.ListSignalDefinitions,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported query kind."),
    };

    private sealed record NormalizedQuery(
        HealthModelQueryKind Kind,
        QueryInputs Inputs,
        int Index,
        HealthModelCallKind CallKind,
        PlanScope Scope,
        bool IsDeferred);
}

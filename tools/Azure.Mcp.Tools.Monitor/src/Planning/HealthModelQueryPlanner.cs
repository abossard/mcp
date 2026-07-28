// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Pure, stateless transformation from a batch of <see cref="HealthModelQuery"/> into an immutable
/// <see cref="ExecutionPlan"/>. It normalizes and validates queries, groups them by (resource group,
/// health model) so calls against one model are contiguous and share a single model resolution,
/// deduplicates calls that reduce to the same Azure Resource Manager request, reuses one entity list
/// per model to resolve health-filtered queries, and orders each group so the entity list precedes the
/// per-entity calls that depend on it.
///
/// The planner has no I/O, service, ARM, or network dependency; it is a deterministic function of its
/// input and is exercised directly by unit tests.
/// </summary>
internal static class HealthModelQueryPlanner
{
    private static readonly HealthModelQueryKind[] DeferrablePerEntityKinds =
    [
        HealthModelQueryKind.EntityHistory,
        HealthModelQueryKind.SignalHistory,
        HealthModelQueryKind.SignalRecommendations,
        HealthModelQueryKind.DataAnnotations,
    ];

    internal static ExecutionPlan Plan(IReadOnlyList<HealthModelQuery> queries)
    {
        ArgumentNullException.ThrowIfNull(queries);

        var diagnostics = new List<PlanDiagnostic>();
        var normalized = new List<NormalizedQuery>();

        for (var index = 0; index < queries.Count; index++)
        {
            var query = queries[index];
            if (TryNormalize(query, index, out var norm, out var error))
            {
                normalized.Add(norm);
            }
            else
            {
                diagnostics.Add(new PlanDiagnostic(index, query.Kind, error));
            }
        }

        var groups = new List<PlanGroup>();
        foreach (var scope in DistinctScopesInOrder(normalized))
        {
            var scopeQueries = normalized.Where(n => n.Scope == scope).ToList();
            groups.Add(BuildGroup(scope, scopeQueries));
        }

        return new ExecutionPlan(
            groups,
            diagnostics,
            queries.Count,
            queries.Select(query => HealthModelResultShape.From(query.Fields)).ToList(),
            queries.Select(query => query.Kind == HealthModelQueryKind.EntityList ? query.HealthFilter : null).ToList());
    }

    private static bool TryNormalize(HealthModelQuery query, int index, out NormalizedQuery norm, out string error)
    {
        norm = default!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(query.ResourceGroup) || string.IsNullOrWhiteSpace(query.HealthModel))
        {
            error = "resourceGroup and healthModel are required.";
            return false;
        }

        var scope = new PlanScope(query.ResourceGroup, query.HealthModel);

        if (!TryValidateFields(query, out error) || !TryValidatePaging(query, out error) ||
            !TryValidateTargeting(query, out error))
        {
            return false;
        }

        switch (query.Kind)
        {
            case HealthModelQueryKind.EntityList:
                norm = new NormalizedQuery(query, index, HealthModelCallKind.ListEntities, scope, IsDeferred: false);
                return true;

            case HealthModelQueryKind.EntityGet:
                if (query.HealthFilter is not null)
                {
                    error = "healthFilter is not supported for entityGet; use entityList with healthFilter to read " +
                        "every matching entity from a single list call.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(query.EntityName))
                {
                    error = "entityName is required for entityGet.";
                    return false;
                }
                norm = new NormalizedQuery(query, index, HealthModelCallKind.GetEntity, scope, IsDeferred: false);
                return true;

            default:
                if (!DeferrablePerEntityKinds.Contains(query.Kind))
                {
                    error = $"Unsupported query kind '{query.Kind}'.";
                    return false;
                }

                if (query.Kind == HealthModelQueryKind.SignalHistory && string.IsNullOrWhiteSpace(query.SignalName))
                {
                    error = "signalName is required for signalHistory.";
                    return false;
                }

                var hasEntity = !string.IsNullOrWhiteSpace(query.EntityName);
                if (!hasEntity && query.HealthFilter is null)
                {
                    error = $"entityName or healthFilter is required for {ToCamel(query.Kind)}.";
                    return false;
                }

                norm = new NormalizedQuery(query, index, MapKind(query.Kind), scope, IsDeferred: !hasEntity);
                return true;
        }
    }

    private static PlanGroup BuildGroup(PlanScope scope, IReadOnlyList<NormalizedQuery> scopeQueries)
    {
        // 1) Explicit entity-list queries, one call per distinct timestamp (deduplicated, indexes merged).
        var listCalls = DeduplicateCalls(
            scopeQueries.Where(n => n.CallKind == HealthModelCallKind.ListEntities),
            keySelector: n => new ListPageKey(n.Query.Timestamp, n.Query.ContinuationToken),
            callFactory: (n, indexes) => ListCall(scope, n.Query.Timestamp, n.Query.ContinuationToken, indexes));

        // 2) One shared current-time entity-list page gates each distinct health-filter discovery page.
        foreach (var continuationToken in scopeQueries
                     .Where(query => query.IsDeferred)
                     .Select(query => query.Query.ContinuationToken)
                     .Distinct(StringComparer.Ordinal))
        {
            if (!listCalls.Any(call =>
                    call.Timestamp is null &&
                    string.Equals(call.ContinuationToken, continuationToken, StringComparison.Ordinal)))
            {
                listCalls = [.. listCalls, ListCall(scope, timestamp: null, continuationToken, queryIndexes: [])];
            }
        }

        // 3) Concrete per-entity calls (fixed entity), deduplicated by call shape.
        var concreteCalls = DeduplicateCalls(
            scopeQueries.Where(n => !n.IsDeferred && n.CallKind != HealthModelCallKind.ListEntities),
            keySelector: n => (n.CallKind, n.Query.EntityName, n.Query.SignalName, n.Query.StartTime, n.Query.EndTime, n.Query.Top, n.Query.NextMarker),
            callFactory: (n, indexes) => PerEntityCall(scope, n, entityName: n.Query.EntityName, healthFilter: null, indexes));

        // 4) Deferred per-entity calls (health-filtered), deduplicated by call shape.
        var deferredCalls = DeduplicateCalls(
            scopeQueries.Where(n => n.IsDeferred),
            keySelector: n => (n.CallKind, (string?)null, n.Query.SignalName, n.Query.StartTime, n.Query.EndTime, n.Query.Top, n.Query.HealthFilter, n.Query.ContinuationToken),
            callFactory: (n, indexes) => PerEntityCall(scope, n, entityName: null, healthFilter: n.Query.HealthFilter, indexes));

        // Ordering: entity lists first, then concrete per-entity, then deferred per-entity.
        var ordered = new List<PlannedCall>(listCalls.Count + concreteCalls.Count + deferredCalls.Count);
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

        return order.Select(key => callFactory(firstByKey[key], indexesByKey[key])).ToList();
    }

    private static PlannedCall ListCall(
        PlanScope scope, DateTimeOffset? timestamp, string? continuationToken, IReadOnlyList<int> queryIndexes) =>
        new(HealthModelCallKind.ListEntities, scope, EntityName: null, SignalName: null,
            StartTime: null, EndTime: null, Top: null, Timestamp: timestamp, HealthFilter: null,
            NextMarker: null, continuationToken, queryIndexes);

    private static PlannedCall PerEntityCall(
        PlanScope scope, NormalizedQuery norm, string? entityName, HealthModelHealthFilter? healthFilter, IReadOnlyList<int> queryIndexes) =>
        new(norm.CallKind, scope, entityName, norm.Query.SignalName,
            norm.Query.StartTime, norm.Query.EndTime, norm.Query.Top, Timestamp: null, healthFilter,
            norm.Query.NextMarker, norm.Query.ContinuationToken, queryIndexes);

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

    private static HealthModelCallKind MapKind(HealthModelQueryKind kind) => kind switch
    {
        HealthModelQueryKind.EntityList => HealthModelCallKind.ListEntities,
        HealthModelQueryKind.EntityGet => HealthModelCallKind.GetEntity,
        HealthModelQueryKind.EntityHistory => HealthModelCallKind.GetHistory,
        HealthModelQueryKind.SignalHistory => HealthModelCallKind.GetSignalHistory,
        HealthModelQueryKind.SignalRecommendations => HealthModelCallKind.GetSignalRecommendations,
        HealthModelQueryKind.DataAnnotations => HealthModelCallKind.GetDataAnnotations,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported query kind."),
    };

    private static string ToCamel(HealthModelQueryKind kind)
    {
        var name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private sealed record NormalizedQuery(HealthModelQuery Query, int Index, HealthModelCallKind CallKind, PlanScope Scope, bool IsDeferred);

    /// <summary>
    /// Validates the two targeting inputs before any kind-specific rule runs, so an out-of-range enum value or a
    /// query that supplies both targets is reported as an invalid query rather than silently matching nothing or
    /// having one input quietly discarded.
    /// </summary>
    private static bool TryValidateTargeting(HealthModelQuery query, out string error)
    {
        error = string.Empty;

        if (query.HealthFilter is not { } filter)
        {
            return true;
        }

        if (!Enum.IsDefined(filter))
        {
            error = $"healthFilter '{(int)filter}' is not a valid health filter.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.EntityName))
        {
            error = "entityName and healthFilter are mutually exclusive; supply one or the other.";
            return false;
        }

        return true;
    }

    private static bool TryValidatePaging(HealthModelQuery query, out string error)
    {
        error = string.Empty;

        if (query.NextMarker is not null)
        {
            if (query.Kind is not (HealthModelQueryKind.EntityHistory or HealthModelQueryKind.SignalHistory or HealthModelQueryKind.DataAnnotations))
            {
                error = $"nextMarker is not valid for {ToCamel(query.Kind)}.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(query.EntityName) || query.HealthFilter is not null)
            {
                error = "nextMarker requires a concrete entityName and cannot be used with healthFilter.";
                return false;
            }
            if (query.StartTime is not null || query.EndTime is not null)
            {
                error = "nextMarker cannot be combined with startTime or endTime.";
                return false;
            }
        }

        if (query.ContinuationToken is not null &&
            query.Kind != HealthModelQueryKind.EntityList &&
            (query.HealthFilter is null || !string.IsNullOrWhiteSpace(query.EntityName)))
        {
            error = "continuationToken is valid only for entityList or health-filter discovery.";
            return false;
        }

        if (query.NextMarker is not null && query.ContinuationToken is not null)
        {
            error = "nextMarker and continuationToken cannot be combined.";
            return false;
        }

        return true;
    }

    private static bool TryValidateFields(HealthModelQuery query, out string error)
    {
        error = string.Empty;
        if (query.Fields is null)
        {
            return true;
        }

        foreach (var field in query.Fields)
        {
            var applicable = field == HealthModelFieldGroup.Full || query.Kind switch
            {
                HealthModelQueryKind.EntityList or HealthModelQueryKind.EntityGet =>
                    field is HealthModelFieldGroup.Identity or HealthModelFieldGroup.Audit or
                        HealthModelFieldGroup.Signals or HealthModelFieldGroup.Layout,
                HealthModelQueryKind.SignalHistory => field == HealthModelFieldGroup.Context,
                HealthModelQueryKind.SignalRecommendations => field == HealthModelFieldGroup.Configurations,
                HealthModelQueryKind.DataAnnotations => field == HealthModelFieldGroup.Details,
                _ => false,
            };

            if (!applicable)
            {
                error = $"Field group '{field}' is not valid for query kind '{query.Kind}'.";
                return false;
            }
        }

        return true;
    }

    private readonly record struct ListPageKey(DateTimeOffset? Timestamp, string? ContinuationToken);
}

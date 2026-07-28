// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Services;
using Azure.ResourceManager.CloudHealth;

namespace Azure.Mcp.Tools.Monitor.Planning;

internal static class HealthModelQueryExecutor
{
    internal static async Task<IReadOnlyList<HealthModelQueryResult>> ExecuteAsync(
        ExecutionPlan plan, IHealthModelCallRunner runner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runner);

        var results = new HealthModelQueryResult[plan.QueryCount];

        foreach (var diagnostic in plan.Diagnostics)
        {
            results[diagnostic.QueryIndex] = QueryFailure(
                diagnostic.QueryIndex, ToCamel(diagnostic.Kind.ToString()), diagnostic.Error);
        }

        foreach (var group in plan.Groups)
        {
            var gates = new Dictionary<EntityListPageKey, EntityListGate>();
            foreach (var call in group.Calls)
            {
                await ExecuteCallAsync(
                    call, runner, gates, results, plan.ResultShapes, plan.ListFilters, cancellationToken);
            }
        }

        return results;
    }

    private static async Task ExecuteCallAsync(
        PlannedCall call,
        IHealthModelCallRunner runner,
        Dictionary<EntityListPageKey, EntityListGate> gates,
        HealthModelQueryResult[] results,
        IReadOnlyList<HealthModelResultShape> resultShapes,
        IReadOnlyList<HealthModelHealthFilter?> listFilters,
        CancellationToken cancellationToken)
    {
        var kind = KindLabel(call.Kind);

        if (call.Kind == HealthModelCallKind.ListEntities)
        {
            await ExecuteListAsync(call, runner, gates, results, resultShapes, listFilters, kind, cancellationToken);
            return;
        }

        if (call.IsDeferredPerEntity)
        {
            var gate = GetGate(gates, call.ContinuationToken);
            var discoveryPage = await ResolveGateAsync(call, runner, gate, cancellationToken);
            if (gate.Failed)
            {
                AssignAll(results, call.QueryIndexes, index => QueryFailure(index, kind, gate.Error!));
                return;
            }

            var targets = discoveryPage!.Items
                .Where(entity => MatchesFilter(entity, call.HealthFilter!.Value))
                .Select(entity => entity.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToList();

            var nodes = await FanOutAsync(call, runner, targets, cancellationToken);
            var queryPage = QueryPage(discoveryPage);
            AssignSuccess(results, call.QueryIndexes, resultShapes, kind, nodes, queryPage);
            return;
        }

        var concreteNodes = await FanOutAsync(call, runner, [call.EntityName!], cancellationToken);
        AssignSuccess(results, call.QueryIndexes, resultShapes, kind, concreteNodes, page: null);
    }

    private static async Task ExecuteListAsync(
        PlannedCall call,
        IHealthModelCallRunner runner,
        Dictionary<EntityListPageKey, EntityListGate> gates,
        HealthModelQueryResult[] results,
        IReadOnlyList<HealthModelResultShape> resultShapes,
        IReadOnlyList<HealthModelHealthFilter?> listFilters,
        string kind,
        CancellationToken cancellationToken)
    {
        HealthModelEntityListPage page;
        try
        {
            page = await runner.ListEntitiesAsync(
                call.Scope, call.Timestamp, call.ContinuationToken, cancellationToken);
            HealthModelPaginator.EnsureMarkerAdvanced(call.ContinuationToken, page.ContinuationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (call.Timestamp is null)
            {
                GetGate(gates, call.ContinuationToken).SetFailure(ex.Message);
            }
            AssignAll(results, call.QueryIndexes, index => QueryFailure(index, kind, ex.Message));
            return;
        }

        if (call.Timestamp is null)
        {
            GetGate(gates, call.ContinuationToken).SetSuccess(page);
        }

        var nodes = page.Items
            .Select(entity => new HealthModelEntityResult
            {
                EntityName = entity.Name,
                Success = true,
                Entity = entity,
            })
            .ToList();

        // Queries that differ only by health filter share this one list call; each slot keeps the entities its own
        // filter selects, and its returnedCount reports what that caller actually received.
        AssignAll(results, call.QueryIndexes, index =>
        {
            var selected = listFilters[index] is { } filter
                ? nodes.Where(node => MatchesFilter(node.Entity!, filter)).ToList()
                : nodes;

            return QuerySuccess(
                index,
                kind,
                ShapeNodes(selected, resultShapes[index]),
                new HealthModelQueryPage(
                    Complete: string.IsNullOrEmpty(page.ContinuationToken),
                    ReturnedCount: selected.Count,
                    ContinuationToken: EmptyToNull(page.ContinuationToken)));
        });
    }

    private static async Task<HealthModelEntityListPage?> ResolveGateAsync(
        PlannedCall call,
        IHealthModelCallRunner runner,
        EntityListGate gate,
        CancellationToken cancellationToken)
    {
        if (gate.Resolved)
        {
            return gate.Page;
        }

        try
        {
            var page = await runner.ListEntitiesAsync(
                call.Scope, timestamp: null, call.ContinuationToken, cancellationToken);
            HealthModelPaginator.EnsureMarkerAdvanced(call.ContinuationToken, page.ContinuationToken);
            gate.SetSuccess(page);
            return page;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            gate.SetFailure(ex.Message);
            return null;
        }
    }

    private static async Task<IReadOnlyList<HealthModelEntityResult>> FanOutAsync(
        PlannedCall call,
        IHealthModelCallRunner runner,
        IReadOnlyList<string> targets,
        CancellationToken cancellationToken)
    {
        var nodes = new List<HealthModelEntityResult>(targets.Count);
        foreach (var entityName in targets)
        {
            try
            {
                nodes.Add(await BuildNodeAsync(call, runner, entityName, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                nodes.Add(new HealthModelEntityResult
                {
                    EntityName = entityName,
                    Success = false,
                    Error = ex.Message,
                    SignalName = call.Kind == HealthModelCallKind.GetSignalHistory ? call.SignalName : null,
                });
            }
        }
        return nodes;
    }

    private static async Task<HealthModelEntityResult> BuildNodeAsync(
        PlannedCall call,
        IHealthModelCallRunner runner,
        string entityName,
        CancellationToken cancellationToken)
    {
        switch (call.Kind)
        {
            case HealthModelCallKind.GetEntity:
                return new HealthModelEntityResult
                {
                    EntityName = entityName,
                    Success = true,
                    Entity = await runner.GetEntityAsync(call.Scope, entityName, cancellationToken),
                };

            case HealthModelCallKind.GetHistory:
                {
                    var payload = await runner.GetHistoryAsync(
                        call.Scope, entityName, call.StartTime, call.EndTime, call.Top,
                        call.NextMarker, cancellationToken);
                    HealthModelPaginator.EnsureMarkerAdvanced(call.NextMarker, payload.NextMarker);
                    return new HealthModelEntityResult
                    {
                        EntityName = entityName,
                        Success = true,
                        History = payload,
                        Page = EntityPage(payload.History.Count, payload.NextMarker),
                    };
                }

            case HealthModelCallKind.GetSignalHistory:
                {
                    var payload = await runner.GetSignalHistoryAsync(
                        call.Scope, entityName, call.SignalName!, call.StartTime, call.EndTime, call.Top,
                        call.NextMarker, cancellationToken);
                    HealthModelPaginator.EnsureMarkerAdvanced(call.NextMarker, payload.NextMarker);
                    return new HealthModelEntityResult
                    {
                        EntityName = entityName,
                        Success = true,
                        SignalName = call.SignalName,
                        SignalHistory = payload,
                        Page = EntityPage(payload.History.Count, payload.NextMarker),
                    };
                }

            case HealthModelCallKind.GetSignalRecommendations:
                return new HealthModelEntityResult
                {
                    EntityName = entityName,
                    Success = true,
                    Recommendations = await runner.GetSignalRecommendationsAsync(
                        call.Scope, entityName, cancellationToken),
                };

            case HealthModelCallKind.GetDataAnnotations:
                {
                    var payload = await runner.GetDataAnnotationsAsync(
                        call.Scope, entityName, call.StartTime, call.EndTime, call.Top,
                        call.NextMarker, cancellationToken);
                    HealthModelPaginator.EnsureMarkerAdvanced(call.NextMarker, payload.NextMarker);
                    return new HealthModelEntityResult
                    {
                        EntityName = entityName,
                        Success = true,
                        Annotations = payload,
                        Page = EntityPage(payload.Annotations.Count, payload.NextMarker),
                    };
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(call), call.Kind, "Unsupported call kind.");
        }
    }

    private static HealthModelQueryPage QueryPage(HealthModelEntityListPage page) =>
        new(
            Complete: string.IsNullOrEmpty(page.ContinuationToken),
            ReturnedCount: page.Items.Count,
            ContinuationToken: EmptyToNull(page.ContinuationToken));

    private static HealthModelEntityPage EntityPage(int returnedCount, string? nextMarker) =>
        new(
            Complete: string.IsNullOrEmpty(nextMarker),
            ReturnedCount: returnedCount,
            NextMarker: EmptyToNull(nextMarker));

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static bool MatchesFilter(HealthModelEntityData entity, HealthModelHealthFilter filter)
    {
        var state = entity.Properties?.HealthState?.ToString();
        return filter switch
        {
            HealthModelHealthFilter.Unhealthy => Equals(state, "Unhealthy"),
            HealthModelHealthFilter.Degraded => Equals(state, "Degraded"),
            HealthModelHealthFilter.Unknown => Equals(state, "Unknown"),
            HealthModelHealthFilter.NotHealthy =>
                Equals(state, "Unhealthy") || Equals(state, "Degraded") || Equals(state, "Unknown"),
            _ => false,
        };

        static bool Equals(string? value, string expected) =>
            string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssignSuccess(
        HealthModelQueryResult[] results,
        IReadOnlyList<int> queryIndexes,
        IReadOnlyList<HealthModelResultShape> resultShapes,
        string kind,
        IReadOnlyList<HealthModelEntityResult> nodes,
        HealthModelQueryPage? page) =>
        AssignAll(results, queryIndexes, index =>
            QuerySuccess(index, kind, ShapeNodes(nodes, resultShapes[index]), page));

    private static IReadOnlyList<HealthModelEntityResult> ShapeNodes(
        IReadOnlyList<HealthModelEntityResult> nodes,
        HealthModelResultShape shape) =>
        nodes.Select(node => new HealthModelEntityResult
        {
            EntityName = node.EntityName,
            Success = node.Success,
            Error = node.Error,
            SignalName = node.SignalName,
            Entity = node.Entity,
            History = node.History,
            SignalHistory = node.SignalHistory,
            Recommendations = node.Recommendations,
            Annotations = node.Annotations,
            Page = node.Page,
            Shape = shape,
        }).ToList();

    private static void AssignAll(
        HealthModelQueryResult[] results,
        IReadOnlyList<int> queryIndexes,
        Func<int, HealthModelQueryResult> factory)
    {
        foreach (var index in queryIndexes)
        {
            results[index] = factory(index);
        }
    }

    private static HealthModelQueryResult QuerySuccess(
        int queryIndex,
        string kind,
        IReadOnlyList<HealthModelEntityResult> entities,
        HealthModelQueryPage? page) =>
        new()
        {
            QueryIndex = queryIndex,
            Kind = kind,
            Success = true,
            Entities = entities,
            Page = page,
        };

    private static HealthModelQueryResult QueryFailure(int queryIndex, string kind, string error) =>
        new() { QueryIndex = queryIndex, Kind = kind, Success = false, Error = error };

    private static EntityListGate GetGate(
        Dictionary<EntityListPageKey, EntityListGate> gates,
        string? continuationToken)
    {
        var key = new EntityListPageKey(continuationToken);
        if (!gates.TryGetValue(key, out var gate))
        {
            gate = new EntityListGate();
            gates[key] = gate;
        }
        return gate;
    }

    private static string KindLabel(HealthModelCallKind kind) => kind switch
    {
        HealthModelCallKind.ListEntities => "entityList",
        HealthModelCallKind.GetEntity => "entityGet",
        HealthModelCallKind.GetHistory => "entityHistory",
        HealthModelCallKind.GetSignalHistory => "signalHistory",
        HealthModelCallKind.GetSignalRecommendations => "signalRecommendations",
        HealthModelCallKind.GetDataAnnotations => "dataAnnotations",
        _ => kind.ToString(),
    };

    private static string ToCamel(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private readonly record struct EntityListPageKey(string? ContinuationToken);

    private sealed class EntityListGate
    {
        internal bool Resolved { get; private set; }
        internal bool Failed { get; private set; }
        internal HealthModelEntityListPage? Page { get; private set; }
        internal string? Error { get; private set; }

        internal void SetSuccess(HealthModelEntityListPage page)
        {
            Page = page;
            Resolved = true;
        }

        internal void SetFailure(string error)
        {
            Error = error;
            Failed = true;
            Resolved = true;
        }
    }
}

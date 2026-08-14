// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.Mcp.Tools.Monitor.Services;
using Azure.ResourceManager.CloudHealth;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// An in-memory health model that counts every call. Counting is the point: "no writes happened" has to be
/// proved by a zero counter, because an assertion that no exception was thrown would pass just as happily
/// against a run that wrote everything successfully.
/// </summary>
internal sealed class FakeHealthModelWriteRunner : IHealthModelWriteRunner
{
    private static readonly Dictionary<HealthModelResourceKind, string> ResourceTypes = new()
    {
        [HealthModelResourceKind.Entity] = "Microsoft.CloudHealth/healthmodels/entities",
        [HealthModelResourceKind.Relationship] = "Microsoft.CloudHealth/healthmodels/relationships",
        [HealthModelResourceKind.SignalDefinition] = "Microsoft.CloudHealth/healthmodels/signaldefinitions",
    };

    private readonly Dictionary<PlanScope, Dictionary<HealthModelResourceKind, List<HealthModelResourceSnapshot>>> _stores = [];

    private readonly Dictionary<string, int> _attempts = new(StringComparer.Ordinal);

    internal int PageSize { get; set; } = 100;

    /// <summary>
    /// Puts the fake behind the same SDK bridge the real runner uses: <c>Read&lt;T&gt;</c> on the way in and
    /// <c>Wire</c> on the way out, plus the envelope a resource provider stamps on what it stores. Without
    /// it the fake keeps the caller's own JSON, which trivially equals itself, so no amount of normalisation
    /// the SDK performs can ever be observed and an idempotency test proves nothing about the shipped path.
    /// </summary>
    internal bool RoundTripThroughSdk { get; set; }

    internal int PutCallCount { get; private set; }

    internal int DeleteCallCount { get; private set; }

    internal int WriteCallCount => PutCallCount + DeleteCallCount;

    internal Dictionary<HealthModelResourceKind, int> ListCallCounts { get; } = [];

    /// <summary>Every write in the order it was issued, as <c>put|delete:kind:name</c>.</summary>
    internal List<string> Calls { get; } = [];

    /// <summary>
    /// Writes the fake service rejects, as <c>put:kind:name</c> or, to fail one health model while an
    /// identically named resource in another still succeeds, <c>healthModel/put:kind:name</c>.
    /// </summary>
    internal HashSet<string> FailOn { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Writes the fake service rejects exactly once, in the same forms as <see cref="FailOn"/>. This is
    /// what separates a failed write from a failed recovery: the same call has to be able to fail and then
    /// succeed, or a restore can never be observed to work.
    /// </summary>
    internal HashSet<string> FailOnce { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Writes the fake service rejects on exactly the nth attempt of that call, keyed as in
    /// <see cref="FailOn"/>. An earlier element has to be able to succeed on the same resource a later one
    /// fails on, or a restore can never be observed against a body an earlier element already changed.
    /// </summary>
    internal Dictionary<string, int> FailAt { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Properties the fake service fills in on every PUT that does not carry them, modelling an RP that
    /// defaults its own fields. Without this the fake stores the caller's body verbatim, which is the one
    /// service behaviour under which a broken "create" idempotency cannot be observed.
    /// </summary>
    internal Dictionary<string, JsonNode> ServerDefaults { get; } = [];

    internal HealthModelResourceSnapshot? Find(HealthModelResourceKind kind, string name) =>
        Find(HealthModelGraphFixture.Scope, kind, name);

    /// <summary>Every scope starts as its own copy of the fixture, so the models cannot alias each other.</summary>
    internal HealthModelResourceSnapshot? Find(PlanScope scope, HealthModelResourceKind kind, string name) =>
        Store(scope)[kind].FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));

    internal void Mutate(HealthModelResourceKind kind, string name, string property, JsonNode? value)
    {
        var collection = Store(HealthModelGraphFixture.Scope)[kind];
        var existing = Find(kind, name)!;
        var body = existing.Body.DeepClone().AsObject();
        body["properties"]![property] = value;
        collection[collection.IndexOf(existing)] = existing with { Body = body };
    }

    private Dictionary<HealthModelResourceKind, List<HealthModelResourceSnapshot>> Store(PlanScope scope)
    {
        if (_stores.TryGetValue(scope, out var store))
        {
            return store;
        }

        var snapshot = HealthModelGraphFixture.Snapshot();
        store = new Dictionary<HealthModelResourceKind, List<HealthModelResourceSnapshot>>
        {
            [HealthModelResourceKind.Entity] = [.. snapshot.Entities.Select(r => Persist(scope, r))],
            [HealthModelResourceKind.Relationship] = [.. snapshot.Relationships.Select(r => Persist(scope, r))],
            [HealthModelResourceKind.SignalDefinition] = [.. snapshot.SignalDefinitions.Select(r => Persist(scope, r))],
        };
        _stores[scope] = store;
        return store;
    }

    public Task<HealthModelResourcePage> ListAsync(
        PlanScope scope, HealthModelResourceKind kind, string? continuationToken, CancellationToken cancellationToken)
    {
        ListCallCounts[kind] = ListCallCounts.GetValueOrDefault(kind) + 1;

        var collection = Store(scope)[kind];
        var offset = continuationToken is null ? 0 : int.Parse(continuationToken);
        var page = collection.Skip(offset).Take(PageSize).ToList();
        var next = offset + page.Count;

        return Task.FromResult(new HealthModelResourcePage(
            page, next < collection.Count ? next.ToString() : null));
    }

    public Task PutAsync(
        PlanScope scope, HealthModelResourceKind kind, string name, JsonObject body, CancellationToken cancellationToken)
    {
        PutCallCount++;
        var call = $"put:{kind}:{name}";
        Calls.Add(call);
        var attempt = _attempts[call] = _attempts.GetValueOrDefault(call) + 1;

        if (FailOn.Contains(call) || FailOn.Contains($"{scope.HealthModel}/{call}") ||
            FailOnce.Remove(call) || FailOnce.Remove($"{scope.HealthModel}/{call}") ||
            (FailAt.TryGetValue(call, out var at) && at == attempt))
        {
            throw new InvalidOperationException($"the service rejected '{name}'.");
        }

        var collection = Store(scope)[kind];
        var existing = Find(scope, kind, name);
        var stored = Persist(scope, new HealthModelResourceSnapshot(kind, name, Default(body)));
        if (existing is null)
        {
            collection.Add(stored);
        }
        else
        {
            collection[collection.IndexOf(existing)] = stored;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        PlanScope scope, HealthModelResourceKind kind, string name, CancellationToken cancellationToken)
    {
        DeleteCallCount++;
        Calls.Add($"delete:{kind}:{name}");

        if (Find(scope, kind, name) is { } existing)
        {
            Store(scope)[kind].Remove(existing);
        }

        return Task.CompletedTask;
    }

    private JsonObject Default(JsonObject body)
    {
        var stored = body.DeepClone().AsObject();
        var properties = stored["properties"] as JsonObject;
        foreach (var (name, value) in ServerDefaults)
        {
            if (properties is not null && properties[name] is null)
            {
                properties[name] = value.DeepClone();
            }
        }

        return stored;
    }

    /// <summary>
    /// What the service ends up holding: its own envelope stamped on, then the body as the SDK bridge
    /// renders it. This is the composition of <c>Read&lt;T&gt;</c> on put and <c>Wire</c> on list that the
    /// real runner performs, collapsed to the one point where the state is kept.
    /// </summary>
    private HealthModelResourceSnapshot Persist(PlanScope scope, HealthModelResourceSnapshot resource)
    {
        if (!RoundTripThroughSdk)
        {
            return resource;
        }

        var type = ResourceTypes[resource.Kind];
        var body = resource.Body.DeepClone().AsObject();
        body["id"] = $"/subscriptions/sub/resourceGroups/{scope.ResourceGroup}/providers/" +
            $"Microsoft.CloudHealth/healthmodels/{scope.HealthModel}/{type.Split('/')[^1]}/{resource.Name}";
        body["name"] = resource.Name;
        body["type"] = type;

        return resource with
        {
            Body = resource.Kind switch
            {
                HealthModelResourceKind.Entity => Wire<HealthModelEntityData>(body),
                HealthModelResourceKind.Relationship => Wire<HealthModelRelationshipData>(body),
                _ => Wire<HealthModelSignalDefinitionData>(body),
            },
        };
    }

    private static JsonObject Wire<T>(JsonObject body) where T : IPersistableModel<T> =>
        HealthModelWireCodec.Wire(
            HealthModelWireCodec.Read<T>(BinaryData.FromString(body.ToJsonString())));
}

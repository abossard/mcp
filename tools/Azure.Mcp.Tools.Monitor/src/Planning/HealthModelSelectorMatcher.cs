// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Resolves a selector against a snapshot. Matching is exact-value only: a substring or regex selector
/// would make it impossible for a caller to read a what-if and be sure the same set is hit on apply.
/// </summary>
internal static class HealthModelSelectorMatcher
{
    internal static bool TryMatch(
        HealthModelSelector? selector,
        HealthModelGraphSnapshot snapshot,
        out SelectorMatch match,
        out string error)
    {
        match = null!;
        error = string.Empty;

        var declared = new List<HealthModelResourceKind>(3);
        if (selector?.Entity is not null)
        {
            declared.Add(HealthModelResourceKind.Entity);
        }
        if (selector?.Relationship is not null)
        {
            declared.Add(HealthModelResourceKind.Relationship);
        }
        if (selector?.SignalDefinition is not null)
        {
            declared.Add(HealthModelResourceKind.SignalDefinition);
        }

        if (declared.Count != 1)
        {
            error = declared.Count == 0
                ? "select must name exactly one of 'entity', 'relationship' or 'signalDefinition'."
                : $"select names {declared.Count} target kinds; it must name exactly one.";
            return false;
        }

        var kind = declared[0];
        var criteria = kind switch
        {
            HealthModelResourceKind.Entity => EntityCriteria(selector!.Entity!),
            HealthModelResourceKind.Relationship => RelationshipCriteria(selector!.Relationship!),
            _ => SignalDefinitionCriteria(selector!.SignalDefinition!),
        };

        if (criteria.Count == 0)
        {
            error = $"select.{Name(kind)} names no criteria; it must name at least one.";
            return false;
        }

        var description = $"{Name(kind)} {{{string.Join(", ", criteria.Select(c => c.Description))}}}";
        var matches = snapshot.Collection(kind)
            .Where(resource => criteria.All(c => c.Predicate(resource)))
            .ToList();

        match = new SelectorMatch(kind, description, matches);
        return true;
    }

    private static string Name(HealthModelResourceKind kind) => kind switch
    {
        HealthModelResourceKind.Entity => "entity",
        HealthModelResourceKind.Relationship => "relationship",
        _ => "signalDefinition",
    };

    private sealed record Criterion(string Description, Func<HealthModelResourceSnapshot, bool> Predicate);

    private static List<Criterion> EntityCriteria(EntitySelector selector)
    {
        var criteria = new List<Criterion>();
        AddNames(criteria, selector.Names);
        AddProperty(criteria, "displayName", selector.DisplayName);
        AddProperty(criteria, "discoveredBy", selector.DiscoveredBy);

        if (selector.Tags is { Count: > 0 })
        {
            var tags = selector.Tags;
            criteria.Add(new Criterion(
                $"tags: {string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}"))}",
                resource => tags.All(tag =>
                    Property(resource, "tags") is JsonObject bag &&
                    bag.TryGetPropertyValue(tag.Key, out var value) &&
                    string.Equals(value?.GetValue<string>(), tag.Value, StringComparison.Ordinal))));
        }

        if (selector.All == true)
        {
            criteria.Add(new Criterion("all: true", _ => true));
        }

        return criteria;
    }

    private static List<Criterion> RelationshipCriteria(RelationshipSelector selector)
    {
        var criteria = new List<Criterion>();
        AddNames(criteria, selector.Names);
        AddProperty(criteria, "parentEntityName", selector.Parent, "parent");
        AddProperty(criteria, "childEntityName", selector.Child, "child");
        AddProperty(criteria, "discoveredBy", selector.DiscoveredBy);
        return criteria;
    }

    private static List<Criterion> SignalDefinitionCriteria(SignalDefinitionSelector selector)
    {
        var criteria = new List<Criterion>();
        AddNames(criteria, selector.Names);
        AddProperty(criteria, "signalKind", selector.SignalKind);
        AddProperty(criteria, "displayName", selector.DisplayName);
        return criteria;
    }

    private static void AddNames(List<Criterion> criteria, IReadOnlyList<string>? names)
    {
        if (names is not { Count: > 0 })
        {
            return;
        }

        criteria.Add(new Criterion(
            $"names: [{string.Join(", ", names)}]",
            resource => names.Contains(resource.Name, StringComparer.Ordinal)));
    }

    private static void AddProperty(
        List<Criterion> criteria, string property, string? expected, string? label = null)
    {
        if (expected is null)
        {
            return;
        }

        criteria.Add(new Criterion(
            $"{label ?? property}: {expected}",
            resource => Property(resource, property) is JsonValue value &&
                value.TryGetValue<string>(out var actual) &&
                string.Equals(actual, expected, StringComparison.Ordinal)));
    }

    private static JsonNode? Property(HealthModelResourceSnapshot resource, string name) =>
        resource.Body["properties"] is JsonObject properties &&
        properties.TryGetPropertyValue(name, out var value)
            ? value
            : null;
}

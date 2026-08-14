// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;
using Azure.Mcp.Tools.Monitor.Planning;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// Contract tests for the batch input. Each kind admits only its own inputs, so a misplaced field is a
/// parse error naming the field rather than a value silently dropped on the floor.
/// </summary>
public class HealthModelQueryParserTests
{
    private const string Scope = """ "resourceGroup":"rg1","healthModel":"modelA" """;

    private static bool Parse(string json, out IReadOnlyList<HealthModelQuery> queries, out string error) =>
        HealthModelQueryParser.TryParse(json, out queries, out error);

    [Theory]
    // A field that belongs to a different kind.
    [InlineData("entityList", """ "signal":"cpu" """, "signal")]
    [InlineData("entityList", """ "window":{"from":"2026-07-01T00:00:00Z"} """, "window")]
    [InlineData("entityGet", """ "page":{"size":10} """, "page")]
    [InlineData("entityGet", """ "entity":"e1","target":{"entity":"e1"} """, "target")]
    [InlineData("entityHistory", """ "target":{"entity":"e1"},"signal":"cpu" """, "signal")]
    [InlineData("entityHistory", """ "target":{"entity":"e1"},"asOf":"2026-07-01T00:00:00Z" """, "asOf")]
    [InlineData("signalHistory", """ "target":{"entity":"e1"},"signal":"cpu","whereHealth":"unhealthy" """, "whereHealth")]
    [InlineData("relationshipList", """ "window":{"from":"2026-07-01T00:00:00Z"} """, "window")]
    [InlineData("signalDefinitionList", """ "target":{"entity":"e1"} """, "target")]
    // A field that belongs to no kind at all.
    [InlineData("entityList", """ "nextMarker":"m" """, "nextMarker")]
    [InlineData("signalHistory", """ "target":{"entity":"e1"},"signal":"cpu","top":10 """, "top")]
    // A misplaced field inside a nested value object.
    [InlineData("entityHistory", """ "target":{"entity":"e1"},"page":{"marker":"m"} """, "marker")]
    [InlineData("entityList", """ "page":{"size":10} """, "size")]
    public void TryParse_RejectsAFieldThatDoesNotBelongToTheKind_ByName(string kind, string extra, string offendingField)
    {
        // The offender sits second so its sibling proves isolation: one bad element must not fail the batch.
        var json = $$"""[{"kind":"entityList",{{Scope}}},{"kind":"{{kind}}",{{Scope}},{{extra}}}]""";

        Assert.True(Parse(json, out var queries, out var batchError));
        Assert.Equal(string.Empty, batchError);
        Assert.Equal(2, queries.Count);

        // The sibling is a real query that plans; only the offender is malformed.
        Assert.IsType<EntityListQuery>(queries[0]);
        var error = Assert.IsType<MalformedQuery>(queries[1]).Error;
        Assert.Contains(offendingField, error, StringComparison.Ordinal);
        Assert.Contains(kind, error, StringComparison.Ordinal);

        // It fails on its own result slot, at its own input position, and the sibling still plans.
        var plan = HealthModelQueryPlanner.Plan(queries);
        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(1, diagnostic.QueryIndex);
        Assert.Single(Assert.Single(plan.Groups).Calls);
    }

    [Fact]
    public void TryParse_ReadsTheDiscriminatorFromAnyPosition()
    {
        var first = $$"""[{"kind":"signalHistory",{{Scope}},"target":{"entity":"e1"},"signal":"cpu"}]""";
        var last = $$"""[{ {{Scope}},"target":{"entity":"e1"},"signal":"cpu","kind":"signalHistory"}]""";

        Assert.True(Parse(first, out var fromFirst, out _));
        Assert.True(Parse(last, out var fromLast, out _));

        // Identical inputs must produce an identical plan regardless of where the caller put "kind".
        Assert.Equal(Describe(fromFirst), Describe(fromLast));
        Assert.Equal("signalHistory", Assert.IsType<SignalHistoryQuery>(fromLast[0]).Kind);
    }

    [Theory]
    [InlineData("""[{"resourceGroup":"rg1","healthModel":"modelA"}]""")]
    [InlineData("""[{"kind":"","resourceGroup":"rg1","healthModel":"modelA"}]""")]
    [InlineData("""[{"kind":"entityHistoryList","resourceGroup":"rg1","healthModel":"modelA"}]""")]
    public void TryParse_RejectsAMissingOrUnknownKind_AndListsTheAcceptedSet(string json)
    {
        Assert.True(Parse(json, out var queries, out _));
        var error = Assert.IsType<MalformedQuery>(Assert.Single(queries)).Error;
        Assert.All(HealthModelQueryParser.Kinds, kind => Assert.Contains(kind, error, StringComparison.Ordinal));

        // An unreadable query is a failed query, not a failed batch.
        var diagnostic = Assert.Single(HealthModelQueryPlanner.Plan(queries).Diagnostics);
        Assert.Equal(0, diagnostic.QueryIndex);
    }

    [Fact]
    public void TryParse_ReportsTheOffendingQueryByItsInputPosition()
    {
        var json = $$"""
            [{"kind":"entityList",{{Scope}}},
             {"kind":"entityList",{{Scope}},"signal":"cpu"}]
            """;

        Assert.True(Parse(json, out var queries, out _));
        Assert.Contains("queries[1]", Assert.IsType<MalformedQuery>(queries[1]).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_RejectsTheWholeBatch_OnlyForBatchLevelProblems()
    {
        // A batch that is not a non-empty JSON array has no slots to isolate into, so it fails as a whole.
        Assert.False(Parse("""{"kind":"entityList"}""", out _, out var notAnArray));
        Assert.Contains("array", notAnArray, StringComparison.Ordinal);

        Assert.False(Parse("[]", out _, out var empty));
        Assert.Contains("at least one", empty, StringComparison.Ordinal);

        Assert.False(Parse("[{", out _, out var malformedJson));
        Assert.Contains("valid JSON", malformedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_AcceptsEveryKindAtItsMinimalValidShape()
    {
        var json = $$"""
            [{"kind":"entityList",{{Scope}}},
             {"kind":"entityGet",{{Scope}},"entity":"e1"},
             {"kind":"entityHistory",{{Scope}},"target":{"entity":"e1"},"label":"h"},
             {"kind":"signalHistory",{{Scope}},"target":{"whereHealth":"notHealthy"},"signal":"cpu"},
             {"kind":"signalRecommendations",{{Scope}},"target":{"entity":"e1"},"label":"r"},
             {"kind":"dataAnnotations",{{Scope}},"target":{"entity":"e1"},"label":"a"},
             {"kind":"relationshipList",{{Scope}}},
             {"kind":"signalDefinitionList",{{Scope}}}]
            """;

        Assert.True(Parse(json, out var queries, out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(HealthModelQueryParser.Kinds.Length, queries.Count);
        Assert.Equal<IEnumerable<string>>(HealthModelQueryParser.Kinds, queries.Select(q => q.Kind).ToArray());

        // Every accepted kind plans without a diagnostic, so the schema's minimal shape is genuinely runnable.
        Assert.Empty(HealthModelQueryPlanner.Plan(queries).Diagnostics);
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<HealthModelQuery> queries) =>
    [
        .. HealthModelQueryPlanner.Plan(queries).Groups.SelectMany(g => g.Calls.Select(c =>
            $"{g.Scope.ResourceGroup}/{g.Scope.HealthModel}|{c.Kind}|{c.EntityName}|{c.SignalName}|{c.AsOf}|{c.HealthFilter}|{c.Cursor}|{string.Join(',', c.QueryIndexes)}")),
    ];
}

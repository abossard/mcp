// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// The option description is the only contract an MCP client sees for the batch, because the option value
/// is a JSON string and the generated input schema can say no more than <c>"type": "string"</c>. These
/// tests hold it to being a real, complete, affordable JSON Schema rather than prose.
/// </summary>
public class HealthModelQuerySchemaTests
{
    /// <summary>
    /// A ceiling just above the measured size, so any growth has to be argued for. The blueprint's Q6 asked
    /// for half of the 6,538-character prose this replaced; the schema lands near 4,900 instead, because
    /// eight closed branches each restate the discriminator, the model scope, and a required list.
    /// Compacting further meant <c>allOf</c> + <c>unevaluatedProperties</c> indirection, which costs exactly
    /// the readability this change exists to buy.
    /// </summary>
    private const int Budget = 5100;

    private static JsonDocument Parse() => JsonDocument.Parse(HealthModelQuerySchema.Schema);

    [Fact]
    public void Schema_IsAParseableArraySchema_WithOneBranchPerKind()
    {
        using var document = Parse();
        var root = document.RootElement;

        Assert.Equal("array", root.GetProperty("type").GetString());

        var branches = root.GetProperty("items").GetProperty("oneOf").EnumerateArray().ToList();
        Assert.Equal(HealthModelQueryParser.Kinds.Length, branches.Count);

        // Every accepted kind has a branch, and each branch pins its own discriminator with a const.
        Assert.Equal<IEnumerable<string>>(
            HealthModelQueryParser.Kinds,
            branches.Select(b => b.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()!).ToArray());
    }

    [Fact]
    public void Schema_ClosesEveryObject_SoAMisplacedFieldIsASchemaViolation()
    {
        using var document = Parse();

        // The runtime rejects unmapped members; the schema must say so too, or a client sees no reason why.
        foreach (var branch in document.RootElement.GetProperty("items").GetProperty("oneOf").EnumerateArray())
        {
            Assert.False(branch.GetProperty("additionalProperties").GetBoolean());
            Assert.Contains("kind", branch.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        }

        foreach (var definition in document.RootElement.GetProperty("$defs").EnumerateObject())
        {
            if (definition.Value.TryGetProperty("type", out var type) && type.GetString() == "object")
            {
                Assert.False(definition.Value.GetProperty("additionalProperties").GetBoolean());
            }
        }
    }

    [Fact]
    public void Schema_FitsTheDescriptionBudget()
    {
        Assert.True(
            HealthModelQuerySchema.Schema.Length <= Budget,
            $"schema is {HealthModelQuerySchema.Schema.Length} characters, budget is {Budget}");

    }

    [Fact]
    public void Schema_AdvertisesExactlyTheSelectionValuesTheTypesAccept()
    {
        // A schema enum that drifts from its C# enum is a lie to the caller: it either forbids a value the
        // parser accepts, or offers one that fails.
        Dictionary<string, string[]> expected = new()
        {
            ["entityList"] = Names<EntitySelection>(),
            ["entityGet"] = Names<EntitySelection>(),
            ["entityHistory"] = Names<FullSelection>(),
            ["signalHistory"] = Names<SignalHistorySelection>(),
            ["signalRecommendations"] = Names<RecommendationSelection>(),
            ["dataAnnotations"] = Names<AnnotationSelection>(),
            ["relationshipList"] = Names<FullSelection>(),
            ["signalDefinitionList"] = Names<FullSelection>(),
        };

        using var document = Parse();
        var defs = document.RootElement.GetProperty("$defs");

        foreach (var branch in document.RootElement.GetProperty("items").GetProperty("oneOf").EnumerateArray())
        {
            var kind = branch.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()!;
            var select = branch.GetProperty("properties").GetProperty("select");
            if (select.TryGetProperty("$ref", out var reference))
            {
                select = defs.GetProperty(reference.GetString()!.Split('/')[^1]);
            }

            var advertised = select.GetProperty("items").GetProperty("enum").EnumerateArray()
                .Select(v => v.GetString()!).ToArray();
            Assert.Equal<IEnumerable<string>>(expected[kind], advertised);
        }

        // The health filter enum is shared, and must equally match its C# source.
        Assert.Equal<IEnumerable<string>>(
            Names<HealthModelHealthFilter>(),
            defs.GetProperty("h").GetProperty("enum").EnumerateArray().Select(v => v.GetString()!).ToArray());
    }

    private static string[] Names<T>() where T : struct, Enum =>
        [.. Enum.GetNames<T>().Select(n => char.ToLowerInvariant(n[0]) + n[1..])];

    [Fact]
    public void Schema_StatesTheBehaviorsACallerCannotInferFromTheShape()
    {
        // Silent-empty signal names, the from-now 30-day anchor, and page size being a page rather than a
        // total are all invisible in the type system, so they have to be said somewhere a client will read.
        Assert.Contains("EMPTY history", HealthModelQuerySchema.Schema, StringComparison.Ordinal);
        Assert.Contains("30 days of the CURRENT time", HealthModelQuerySchema.Schema, StringComparison.Ordinal);
        Assert.Contains("not a total limit", HealthModelQuerySchema.Schema, StringComparison.Ordinal);
    }
}

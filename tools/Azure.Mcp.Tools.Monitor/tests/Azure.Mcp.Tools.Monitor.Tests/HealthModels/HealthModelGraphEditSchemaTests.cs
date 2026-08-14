// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// The option description is the only contract an MCP client sees for the change set, because the option
/// value is a JSON string and the generated input schema can say no more than <c>"type": "string"</c>.
/// These tests hold it to being a real, complete, affordable JSON Schema rather than prose.
/// </summary>
public class HealthModelGraphEditSchemaTests
{
    /// <summary>
    /// A ceiling above the measured size, so any growth has to be argued for. Discriminating on the
    /// operation alone keeps this at four branches; the three resource kinds ride inside a closed
    /// <c>select</c>/<c>resource</c> sub-union rather than multiplying the branch count by three. Raised
    /// from 5200 in C6: at 5197 characters the schema had three characters of room, so a one-word wording
    /// fix broke the build rather than being weighed against the budget, which is the opposite of what a
    /// budget is for. 6000 buys the C6 wording plus roughly 400 characters of deliberate headroom for
    /// wording — not for new branches, which the branch count and closed-union tests above hold instead.
    /// </summary>
    private const int Budget = 6000;

    private static JsonDocument Parse() => JsonDocument.Parse(HealthModelGraphEditSchema.Schema);

    [Fact]
    public void Schema_IsAParseableArraySchema_WithOneBranchPerKind()
    {
        using var document = Parse();
        var root = document.RootElement;

        Assert.Equal("array", root.GetProperty("type").GetString());

        var branches = root.GetProperty("items").GetProperty("oneOf").EnumerateArray().ToList();
        Assert.Equal(HealthModelChangeParser.Kinds.Length, branches.Count);

        Assert.Equal<IEnumerable<string>>(
            HealthModelChangeParser.Kinds,
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

    /// <summary>
    /// G26: "exactly one of" is enforced at runtime by the selector matcher and the resource reader. A
    /// caller reading only the schema has to be able to see the same rule, or a two-kind selector looks
    /// legal right up until it is rejected.
    /// </summary>
    [Theory]
    [InlineData("r")]
    [InlineData("sel")]
    public void Schema_StructurallyAllowsOnlyOneKindPerUnion(string definition)
    {
        using var document = Parse();
        var union = document.RootElement.GetProperty("$defs").GetProperty(definition);

        Assert.Equal(1, union.GetProperty("minProperties").GetInt32());
        Assert.Equal(1, union.GetProperty("maxProperties").GetInt32());
        Assert.Equal(3, union.GetProperty("properties").EnumerateObject().Count());
    }

    [Fact]
    public void Schema_FitsTheDescriptionBudget()
    {
        Assert.True(
            HealthModelGraphEditSchema.Schema.Length <= Budget,
            $"schema is {HealthModelGraphEditSchema.Schema.Length} characters, budget is {Budget}, " +
            $"headroom is {Budget - HealthModelGraphEditSchema.Schema.Length}");
    }

    [Fact]
    public void Schema_AdvertisesExactlyTheSelectorFieldsTheTypesAccept()
    {
        // A schema field set that drifts from its C# type is a lie to the caller: it either forbids a field
        // the parser accepts, or offers one that fails.
        Dictionary<string, string[]> expected = new()
        {
            ["entity"] = ["names", "displayName", "tags", "discoveredBy", "all"],
            ["relationship"] = ["names", "parent", "child", "discoveredBy"],
            ["signalDefinition"] = ["names", "signalKind", "displayName"],
        };

        using var document = Parse();
        var defs = document.RootElement.GetProperty("$defs");
        var selector = defs.GetProperty("sel").GetProperty("properties");

        foreach (var (resourceKind, fields) in expected)
        {
            var reference = selector.GetProperty(resourceKind).GetProperty("$ref").GetString()!;
            var advertised = defs.GetProperty(reference.Split('/')[^1]).GetProperty("properties")
                .EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal<IEnumerable<string>>(fields, advertised);
        }
    }

    [Fact]
    public void Schema_StatesTheBehaviorsACallerCannotInferFromTheShape()
    {
        // What-if by default, the declared affected count an apply must carry, create being a merge with
        // don't-care absence and null-removes, merge-patch null/array semantics, the depth the server-owned
        // deny list is anchored at, exhaustive selection, and rename expansion are all invisible in the type
        // system, so they have to be said somewhere a client will read.
        Assert.StartsWith("{", HealthModelGraphEditSchema.Schema, StringComparison.Ordinal);
        foreach (var statement in new[]
        {
            "writes NOTHING",
            "affectedCount",
            "FULLY enumerated",
            "REMOVES the property",
            "REPLACES wholesale",
            "This is a MERGE, not a full-body replace",
            "OMIT is don't-care",
            "explicit null REMOVES it",
            "server-owned ONLY at the body root and directly under properties",
            "DEEPER, e.g. properties.tags.name, they are ordinary caller data",
            "REJECTED, never cascaded",
            "allowEmptyMatch is true",
            "create-time immutable",
            "puts the pre-batch body back",
            "RESOURCE LOST",
            "an earlier element failed to write is skipped",
            "pre-batch body is NOT put back",
            "EXECUTION, not input, order",
        })
        {
            Assert.Contains(statement, HealthModelGraphEditSchema.Schema, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Saying it somewhere is not saying it where a caller reads it. A client reading the rename branch
    /// never sees the patch branch, and rename expands into repoints that are replaces, so a failed rename
    /// surfaces the same restore and RESOURCE LOST outcomes. Asserted per branch object, because a
    /// whole-string substring check passes no matter which branch the sentence sits in.
    /// </summary>
    [Theory]
    [InlineData("patch", true)]
    [InlineData("rename", true)]
    [InlineData("create", false)]
    [InlineData("delete", false)]
    public void Schema_WarnsAboutRestoreAndResourceLoss_InEveryBranchThatCanCauseIt(string kind, bool expected)
    {
        using var document = Parse();

        var branch = document.RootElement.GetProperty("items").GetProperty("oneOf").EnumerateArray()
            .Single(b => b.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == kind);
        var description = branch.GetProperty("description").GetString()!;

        Assert.Equal(expected, description.Contains("pre-batch body", StringComparison.Ordinal));
        Assert.Equal(expected, description.Contains("RESOURCE LOST", StringComparison.Ordinal));
    }

    /// <summary>
    /// G47: a target can come back skipped rather than done, which is an outcome the shape cannot express
    /// and the caller has to plan for. The rename branch needs it in its own words because a rename that
    /// creates the new name and then skips the repoints leaves both names live — a state neither the
    /// caller nor the what-if asked for. Located by parsing the branch out of the schema, so the assertion
    /// cannot be satisfied by the sentence sitting in some other branch.
    /// </summary>
    [Theory]
    [InlineData("patch", "an earlier element failed to write is skipped")]
    [InlineData("rename", "is skipped instead")]
    public void Schema_StatesTheSkipOutcome_InEveryBranchThatCanProduceIt(string kind, string statement)
    {
        using var document = Parse();

        var branch = document.RootElement.GetProperty("items").GetProperty("oneOf").EnumerateArray()
            .Single(b => b.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == kind);

        Assert.Contains(statement, branch.GetProperty("description").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// G48: supersession is reported ON an element by whatever later element took its write away, so the
    /// branches that can take a write away have to say so — the <c>delete</c> branch above all, since a
    /// delete is why the annotation exists at all. A <c>rename</c> reaches it through its repoints, which
    /// are replaces, so it also has the withheld-restore outcome. Located by parsing the branch out of the
    /// schema, because a whole-string check passes no matter which branch the sentence sits in.
    /// </summary>
    [Theory]
    [InlineData("patch", "reports supersededBy")]
    [InlineData("delete", "reports supersededBy")]
    [InlineData("rename", "reports supersededBy")]
    [InlineData("rename", "RESTORE SKIPPED")]
    public void Schema_StatesTheSupersessionOutcome_InEveryBranchThatCanProduceIt(string kind, string statement)
    {
        using var document = Parse();

        Assert.Contains(statement, Branch(document, kind), StringComparison.Ordinal);
    }

    /// <summary>
    /// The negative half of the row above: a <c>create</c> only ever PUTs, so it can neither delete a
    /// resource nor fail after deleting one. A branch that claimed supersession anyway would send a caller
    /// looking for an annotation that cannot appear.
    /// </summary>
    [Fact]
    public void Schema_DoesNotClaimSupersession_InTheCreateBranchThatCannotCauseIt()
    {
        using var document = Parse();

        Assert.DoesNotContain("supersededBy", Branch(document, "create"), StringComparison.Ordinal);
    }

    /// <summary>
    /// G49: <c>affectedCount</c> is echoed from a what-if into the apply, which reads as a promise that the
    /// apply does what the preview said. It only holds while every write succeeds — a failure turns the
    /// targets after it into skipped ones — and the caller reads that in the root description, next to the
    /// echo it is a bound on.
    /// </summary>
    [Fact]
    public void Schema_BoundsWhatTheWhatIfPredicts_WhereTheAffectedCountEchoIsDescribed()
    {
        using var document = Parse();
        var description = document.RootElement.GetProperty("description").GetString()!;

        Assert.Contains(
            "What-if predicts the apply only while every write succeeds", description, StringComparison.Ordinal);
        Assert.Contains("affectedCount is an upper bound", description, StringComparison.Ordinal);
    }

    private static string Branch(JsonDocument document, string kind) =>
        document.RootElement.GetProperty("items").GetProperty("oneOf").EnumerateArray()
            .Single(b => b.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == kind)
            .GetProperty("description").GetString()!;
}

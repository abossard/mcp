// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// Contract tests for the change set input. Each operation admits only its own inputs, so a misplaced
/// field is a parse error naming the field rather than a value silently dropped on the floor.
/// </summary>
public class HealthModelChangeParserTests
{
    private const string Scope = """ "resourceGroup":"rg1","healthModel":"modelA" """;

    /// <summary>A well-formed sibling, so a rejected element never takes the batch down with it.</summary>
    private const string Sibling = """{"kind":"delete","resourceGroup":"rg1","healthModel":"modelA","select":{"entity":{"all":true}}}""";

    private static bool Parse(string json, out IReadOnlyList<HealthModelChange> changes, out string error) =>
        HealthModelChangeParser.TryParse(json, out changes, out error);

    [Theory]
    // A field that belongs to a different operation.
    [InlineData("patch", """ "select":{"entity":{"all":true}},"patch":{"properties":{}},"newName":"x" """, "newName")]
    [InlineData("delete", """ "select":{"entity":{"all":true}},"patch":{"properties":{}} """, "patch")]
    [InlineData("rename", """ "select":{"entity":{"all":true}},"newName":"x","properties":{} """, "properties")]
    [InlineData("create", """ "name":"e1","resource":{"entity":{"properties":{}}},"select":{"entity":{"all":true}} """, "select")]
    [InlineData("create", """ "name":"e1","resource":{"entity":{"properties":{}}},"allowEmptyMatch":true """, "allowEmptyMatch")]
    [InlineData("patch", """ "select":{"entity":{"all":true}},"patch":{"properties":{}},"resource":{"entity":{"properties":{}}} """, "resource")]
    [InlineData("rename", """ "select":{"entity":{"all":true}},"newName":"x","name":"e1" """, "name")]
    [InlineData("delete", """ "select":{"entity":{"all":true}},"newName":"x" """, "newName")]
    // A field that belongs to no operation at all.
    [InlineData("create", """ "name":"e1","resource":{"entity":{"properties":{}}},"cascade":true """, "cascade")]
    [InlineData("patch", """ "select":{"entity":{"all":true}},"patch":{"properties":{}},"mode":"apply" """, "mode")]
    // A misplaced field inside a nested selector or resource union.
    [InlineData("patch", """ "select":{"entity":{"name":"e1"}},"patch":{"properties":{}} """, "name")]
    [InlineData("delete", """ "select":{"relationship":{"tags":{"a":"b"}}} """, "tags")]
    [InlineData("delete", """ "select":{"signalDefinition":{"discoveredBy":"rule"}} """, "discoveredBy")]
    [InlineData("create", """ "name":"e1","resource":{"entity":{"props":{}}} """, "props")]
    public void TryParse_RejectsAFieldThatDoesNotBelongToTheKind_ByName(string kind, string body, string offendingField)
    {
        // The offender sits second so its sibling proves isolation: one bad element must not fail the batch.
        var json = $$"""[{{Sibling}},{"kind":"{{kind}}",{{Scope}},{{body}}}]""";

        Assert.True(Parse(json, out var changes, out var batchError));
        Assert.Equal(string.Empty, batchError);
        Assert.Equal(2, changes.Count);

        // The sibling is a real change element; only the offender is malformed.
        Assert.IsType<DeleteChange>(changes[0]);
        var error = Assert.IsType<MalformedChange>(changes[1]).Error;
        Assert.Contains(offendingField, error, StringComparison.Ordinal);
        Assert.Contains(kind, error, StringComparison.Ordinal);
        Assert.Contains("changes[1]", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_ReadsTheDiscriminatorFromAnyPosition()
    {
        const string first = """[{"kind":"rename","resourceGroup":"rg1","healthModel":"modelA","select":{"entity":{"names":["e1"]}},"newName":"e2"}]""";
        const string last = """[{"resourceGroup":"rg1","healthModel":"modelA","select":{"entity":{"names":["e1"]}},"newName":"e2","kind":"rename"}]""";

        Assert.True(Parse(first, out var fromFirst, out _));
        Assert.True(Parse(last, out var fromLast, out _));

        var a = Assert.IsType<RenameChange>(fromFirst[0]);
        var b = Assert.IsType<RenameChange>(fromLast[0]);
        Assert.Equal(a.NewName, b.NewName);
        Assert.Equal("rename", b.Kind);
        Assert.Equal(["e1"], b.Select!.Entity!.Names!);
    }

    [Theory]
    [InlineData("""[{"resourceGroup":"rg1","healthModel":"modelA"}]""")]
    [InlineData("""[{"kind":"","resourceGroup":"rg1","healthModel":"modelA"}]""")]
    [InlineData("""[{"kind":"upsert","resourceGroup":"rg1","healthModel":"modelA"}]""")]
    public void TryParse_RejectsAMissingOrUnknownKind_AndListsTheAcceptedSet(string json)
    {
        Assert.True(Parse(json, out var changes, out _));
        var error = Assert.IsType<MalformedChange>(Assert.Single(changes)).Error;

        foreach (var kind in HealthModelChangeParser.Kinds)
        {
            Assert.Contains(kind, error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("", "required")]
    [InlineData("   ", "required")]
    [InlineData("[]", "at least one")]
    [InlineData("""{"kind":"delete"}""", "must be a JSON array")]
    [InlineData("""[{"kind":"delete",""", "valid JSON array")]
    public void TryParse_RejectsABatchThatIsNotAUsableArray(string? json, string expected)
    {
        Assert.False(Parse(json!, out var changes, out var error));
        Assert.Empty(changes);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_ReadsEveryOperationAndKeepsInputOrder()
    {
        const string json = """
            [
              {"kind":"create","resourceGroup":"rg1","healthModel":"modelA","name":"e9","resource":{"entity":{"properties":{"displayName":"E9"}}},"label":"a"},
              {"kind":"patch","resourceGroup":"rg1","healthModel":"modelA","select":{"entity":{"names":["e1"]}},"patch":{"properties":{"displayName":null}}},
              {"kind":"rename","resourceGroup":"rg1","healthModel":"modelA","select":{"signalDefinition":{"signalKind":"azureResourceMetric"}},"newName":"sd9","allowEmptyMatch":true},
              {"kind":"delete","resourceGroup":"rg1","healthModel":"modelA","select":{"relationship":{"parent":"e1"}},"label":"a"}
            ]
            """;

        Assert.True(Parse(json, out var changes, out _));

        Assert.Equal(
            [typeof(CreateChange), typeof(PatchChange), typeof(RenameChange), typeof(DeleteChange)],
            changes.Select(c => c.GetType()));
        Assert.Equal(["a", null, null, "a"], changes.Select(c => c.Label));

        // The official body travels verbatim, and a null patch value survives parsing as an explicit null.
        Assert.Equal("E9", ((CreateChange)changes[0]).Resource!.Entity!.Properties!["displayName"]!.GetValue<string>());
        var patched = ((PatchChange)changes[1]).Patch!.Properties!.AsObject();
        Assert.True(patched.ContainsKey("displayName"));
        Assert.Null(patched["displayName"]);

        Assert.True(((RenameChange)changes[2]).AllowEmptyMatch);
        Assert.Equal("e1", ((DeleteChange)changes[3]).Select!.Relationship!.Parent);
    }
}

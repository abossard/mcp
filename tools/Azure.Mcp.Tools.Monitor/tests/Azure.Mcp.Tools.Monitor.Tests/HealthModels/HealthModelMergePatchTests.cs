// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Planning;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// RFC 7386 semantics and the server-owned property guard. These decide whether re-applying a change set
/// is a no-op or silently deletes state the caller never mentioned.
/// </summary>
public class HealthModelMergePatchTests
{
    private const string EntityBody = """
        {
          "id":"/subscriptions/s/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/entities/e1",
          "name":"e1",
          "type":"Microsoft.CloudHealth/healthmodels/entities",
          "properties":{
            "displayName":"Frontend",
            "healthObjective":99.9,
            "provisioningState":"Succeeded",
            "healthState":"Healthy",
            "canvasPosition":{"x":10,"y":20},
            "impact":"standard",
            "signalGroups":[{"signals":[{"signalName":"cpu"}]}],
            "tags":{"tier":"web"}
          }
        }
        """;

    private static JsonObject Body() => JsonNode.Parse(EntityBody)!.AsObject();

    private static JsonObject Patch(string json) =>
        HealthModelMergePatch.Apply(Body(), JsonNode.Parse(json))!.AsObject();

    [Theory]
    // A null value removes the property rather than writing a literal null.
    [InlineData("""{"properties":{"impact":null}}""", "properties.impact", null)]
    [InlineData("""{"properties":{"canvasPosition":null}}""", "properties.canvasPosition", null)]
    // A nested object merges, leaving its untouched siblings in place.
    [InlineData("""{"properties":{"canvasPosition":{"x":99}}}""", "properties.canvasPosition.x", "99")]
    [InlineData("""{"properties":{"tags":{"owner":"team"}}}""", "properties.tags.owner", "\"team\"")]
    // An array replaces wholesale instead of merging element by element.
    [InlineData("""{"properties":{"signalGroups":[]}}""", "properties.signalGroups", "[]")]
    [InlineData("""{"properties":{"signalGroups":[{"signals":[]}]}}""", "properties.signalGroups", """[{"signals":[]}]""")]
    // A brand new property is added.
    [InlineData("""{"properties":{"icon":{"iconName":"web"}}}""", "properties.icon.iconName", "\"web\"")]
    public void Apply_FollowsMergePatchSemantics(string patch, string path, string? expected)
    {
        var result = Patch(patch);

        var node = Resolve(result, path);
        if (expected is null)
        {
            Assert.Null(node);
        }
        else
        {
            Assert.Equal(expected, node!.ToJsonString());
        }
    }

    [Fact]
    public void Apply_MergingANestedObject_KeepsTheSiblingsItDidNotName()
    {
        var result = Patch("""{"properties":{"canvasPosition":{"x":99}}}""");

        Assert.Equal("99", Resolve(result, "properties.canvasPosition.x")!.ToJsonString());
        Assert.Equal("20", Resolve(result, "properties.canvasPosition.y")!.ToJsonString());
        Assert.Equal("\"Frontend\"", Resolve(result, "properties.displayName")!.ToJsonString());
        // Replacing an array must not reach inside the array to merge it.
        Assert.Equal("""[{"signals":[{"signalName":"cpu"}]}]""", Resolve(result, "properties.signalGroups")!.ToJsonString());
    }

    [Theory]
    // Writing back the value that is already there is not a change.
    [InlineData("""{"properties":{"displayName":"Frontend"}}""")]
    [InlineData("""{"properties":{"canvasPosition":{"x":10,"y":20}}}""")]
    [InlineData("""{"properties":{"signalGroups":[{"signals":[{"signalName":"cpu"}]}]}}""")]
    [InlineData("""{"properties":{}}""")]
    public void Diff_ReportsNothingWhenThePatchChangesNothing(string patch)
    {
        Assert.Empty(HealthModelMergePatch.Diff(Body(), Patch(patch)));
    }

    [Fact]
    public void Diff_ReportsOneEntryPerChangedProperty_AndNeverAServerOwnedOne()
    {
        // The body carries provisioningState and healthState; a display-name patch must not stir them.
        var result = Patch("""{"properties":{"displayName":"Web front end"}}""");

        var change = Assert.Single(HealthModelMergePatch.Diff(Body(), result));
        Assert.Equal("properties.displayName", change.Path);
        Assert.Equal("\"Frontend\"", change.Before!.ToJsonString());
        Assert.Equal("\"Web front end\"", change.After!.ToJsonString());

        // A body whose only differences are server-owned is not a change at all.
        var drifted = Body();
        drifted["properties"]!["provisioningState"] = "Updating";
        drifted["properties"]!["healthState"] = "Degraded";
        drifted["properties"]!["discoveredBy"] = "rule-1";
        drifted["name"] = "renamed-by-nobody";
        Assert.Empty(HealthModelMergePatch.Diff(Body(), drifted));
    }

    [Theory]
    [InlineData("""{"properties":{"provisioningState":"Succeeded"}}""", "provisioningState")]
    [InlineData("""{"properties":{"healthState":"Healthy"}}""", "healthState")]
    [InlineData("""{"properties":{"discoveredBy":"rule-1"}}""", "discoveredBy")]
    [InlineData("""{"systemData":{"createdBy":"me"}}""", "systemData")]
    [InlineData("""{"id":"/subscriptions/s"}""", "id")]
    [InlineData("""{"name":"e2"}""", "name")]
    [InlineData("""{"type":"Microsoft.CloudHealth/healthmodels/entities"}""", "type")]
    public void TryValidateWritable_RejectsAServerOwnedProperty_ByName(string patch, string offending)
    {
        Assert.False(HealthModelMergePatch.TryValidateWritable(JsonNode.Parse(patch), out var error));
        Assert.Contains(offending, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"properties":{"displayName":"x"}}""")]
    [InlineData("""{"properties":{"canvasPosition":{"x":1,"y":2}}}""")]
    // An array is opaque replacement data, so a server-owned name inside one is not a patch path.
    [InlineData("""{"properties":{"signalGroups":[{"name":"g1"}]}}""")]
    public void TryValidateWritable_AcceptsAPatchThatOnlyNamesWritableProperties(string patch)
    {
        Assert.True(HealthModelMergePatch.TryValidateWritable(JsonNode.Parse(patch), out var error));
        Assert.Equal(string.Empty, error);
    }

    /// <summary>
    /// G21: the reserved tokens are reserved at the two paths the service owns them, not as bare key names.
    /// Azure tag keys are caller-chosen, so a tag called <c>name</c> is ordinary data that must be both
    /// writable and visible in the what-if.
    /// </summary>
    [Theory]
    [InlineData("name", "my-team")]
    [InlineData("type", "internal")]
    [InlineData("id", "svc-42")]
    [InlineData("provisioningState", "mine")]
    [InlineData("healthState", "mine")]
    [InlineData("discoveredBy", "me")]
    [InlineData("systemData", "mine")]
    public void ReservedTokenUnderTags_IsWritableAndVisibleInTheDiff(string tagKey, string tagValue)
    {
        var patch = "{\"properties\":{\"tags\":{\"" + tagKey + "\":\"" + tagValue + "\"}}}";

        Assert.True(HealthModelMergePatch.TryValidateWritable(JsonNode.Parse(patch), out var error), error);

        var change = Assert.Single(HealthModelMergePatch.Diff(Body(), Patch(patch)));
        Assert.Equal($"properties.tags.{tagKey}", change.Path);
        Assert.Null(change.Before);
        Assert.Equal($"\"{tagValue}\"", change.After!.ToJsonString());
    }

    [Fact]
    public void ReservedTokenNestedUnderAWritableObject_IsOrdinaryData()
    {
        // properties.icon is caller data all the way down, so 'name' inside it is a writable path.
        const string patch = """{"properties":{"icon":{"name":"web","type":"builtin"}}}""";

        Assert.True(HealthModelMergePatch.TryValidateWritable(JsonNode.Parse(patch), out var error), error);
        Assert.Equal(
            ["properties.icon.name", "properties.icon.type"],
            HealthModelMergePatch.Diff(Body(), Patch(patch)).Select(c => c.Path).ToArray());
    }

    [Fact]
    public void AChangedTagIsReported_WhileTheSameTokenAtAServiceOwnedPathStaysInvisible()
    {
        var before = Body();
        before["properties"]!["tags"]!["name"] = "old-team";

        var after = before.DeepClone().AsObject();
        after["properties"]!["tags"]!["name"] = "new-team";
        // Drift the service owns at both anchored paths must stay out of the diff.
        after["properties"]!["provisioningState"] = "Updating";
        after["name"] = "renamed-by-nobody";

        var change = Assert.Single(HealthModelMergePatch.Diff(before, after));
        Assert.Equal("properties.tags.name", change.Path);
        Assert.Equal("\"old-team\"", change.Before!.ToJsonString());
        Assert.Equal("\"new-team\"", change.After!.ToJsonString());
    }

    /// <summary>
    /// G36: arrays are compared, never walked into, and null members inside an element are ignored so the
    /// SDK's materialised nulls do not churn a re-apply. That masking is deliberate and narrow: every
    /// genuine array difference must still be reported, or a caller's edit silently never happens.
    /// </summary>
    [Theory]
    // A scalar element's value, and the order and the number of them.
    [InlineData("[1,2,3]", "[1,2,4]")]
    [InlineData("[1,2,3]", "[3,2,1]")]
    [InlineData("[1,2]", "[1,2,3]")]
    // An object element's value, at the surface and deep inside it.
    [InlineData("""[{"a":1}]""", """[{"a":2}]""")]
    [InlineData("""[{"a":{"b":1}}]""", """[{"a":{"b":2}}]""")]
    [InlineData("""[{"a":"x"}]""", """[{"a":null}]""")]
    [InlineData("""[{"a":1,"b":2}]""", """[{"a":1,"b":null}]""")]
    // A null ELEMENT is a position, not a property state, so it is data like any other.
    [InlineData("[1,null,2]", "[1,2]")]
    [InlineData("""["a"]""", "[null]")]
    [InlineData("[]", """[{"a":null}]""")]
    // Nesting, and the array itself being replaced by something that is not one.
    [InlineData("""[{"a":[1,2]}]""", """[{"a":[1,3]}]""")]
    [InlineData("""[{"a":1},{"b":2}]""", """[{"b":2},{"a":1}]""")]
    [InlineData("[[1,2]]", "[[1,3]]")]
    [InlineData("[1]", "1")]
    public void Diff_ReportsAGenuineArrayDifference(string before, string after)
    {
        var change = Assert.Single(HealthModelMergePatch.Diff(Wrap(before), Wrap(after)));

        Assert.Equal("properties.signalGroups", change.Path);
        Assert.Equal(before, change.Before!.ToJsonString());
        Assert.Equal(after, change.After!.ToJsonString());
    }

    [Theory]
    // The F1 shape: the SDK materialises an unset member of an array element as an explicit null, in both
    // directions and at any depth. Absent and null are the same property state, so this is not a change.
    [InlineData("""[{"a":1}]""", """[{"a":1,"b":null}]""")]
    [InlineData("""[{"a":1,"b":null}]""", """[{"a":1}]""")]
    [InlineData("""[{"a":{"b":1}}]""", """[{"a":{"b":1,"c":null}}]""")]
    [InlineData("""[{"a":[{"x":1}]}]""", """[{"a":[{"x":1,"y":null}]}]""")]
    [InlineData("""[{"a":null}]""", "[{}]")]
    public void Diff_IgnoresANullMemberInsideAnArrayElement(string before, string after)
    {
        Assert.Empty(HealthModelMergePatch.Diff(Wrap(before), Wrap(after)));
    }

    /// <summary>Puts the value at the one path the fixture already holds an array at.</summary>
    private static JsonObject Wrap(string signalGroups) =>
        new() { ["properties"] = new JsonObject { ["signalGroups"] = JsonNode.Parse(signalGroups) } };

    private static JsonNode? Resolve(JsonNode node, string path)
    {
        JsonNode? current = node;
        foreach (var segment in path.Split('.'))
        {
            current = current?.AsObject().TryGetPropertyValue(segment, out var value) == true ? value : null;
        }
        return current;
    }
}

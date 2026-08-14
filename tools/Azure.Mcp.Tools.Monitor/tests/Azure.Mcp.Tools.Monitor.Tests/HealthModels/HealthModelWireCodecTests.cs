// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Services;
using Azure.ResourceManager.CloudHealth;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// G28: the whole graph-edit write path assumes the SDK bridge is lossless. The planner merge-patches the
/// wire JSON a read produced and the runner hands the result straight back to the SDK to PUT, so a property
/// the pinned beta build does not recognise would be silently deleted from the service rather than left
/// alone.
/// </summary>
/// <remarks>
/// The comparison is against the <em>caller-authored</em> payload, never against an already-normalised one.
/// A fixed-point assertion (<c>once == twice</c>) is structurally blind to whatever the first normalisation
/// <em>adds</em>, which is how the <c>"signalKind":null</c> the SDK materialises inside
/// <c>properties.signalGroups[].signals[]</c> escaped review: it is stable, so it is a fixed point, and it
/// still made every re-apply churn.
/// </remarks>
public class HealthModelWireCodecTests
{
    private const string Entity = """
        {"name":"web","type":"Microsoft.CloudHealth/healthmodels/entities","properties":{
          "displayName":"Frontend","healthObjective":99.9,"impact":"Standard",
          "canvasPosition":{"x":10,"y":20},"provisioningState":"Succeeded","healthState":"Healthy",
          "signalGroups":{"azureResource":{"azureResourceId":"/subscriptions/s/rg/r","signals":[
            {"name":"cpu","signalKind":"AzureResourceMetric","signalDefinitionName":"cpu-def"}]}},
          "tags":{"tier":"web","name":"platform"}}}
        """;

    private const string EntityWithUnknownProperty = """
        {"name":"web","type":"Microsoft.CloudHealth/healthmodels/entities","properties":{
          "displayName":"Frontend","aPropertyThisBuildHasNeverHeardOf":{"nested":[1,2,3]}}}
        """;

    /// <summary>The shape a caller actually writes: no envelope, no discriminator inside the signal.</summary>
    private const string CallerAuthoredEntity = """
        {"properties":{"displayName":"Cache","signalGroups":{"azureLogAnalytics":{"signals":[
          {"name":"p99","signalDefinitionName":"latency"}]}}}}
        """;

    private const string Relationship = """
        {"name":"web-api","type":"Microsoft.CloudHealth/healthmodels/relationships","properties":{
          "displayName":"web depends on api","parentEntityName":"web","childEntityName":"api",
          "discoveredBy":"rule-1","provisioningState":"Succeeded"}}
        """;

    private const string CallerAuthoredRelationship = """
        {"properties":{"parentEntityName":"web","childEntityName":"cache"}}
        """;

    private const string MetricSignalDefinition = """
        {"name":"cpu","type":"Microsoft.CloudHealth/healthmodels/signaldefinitions","properties":{
          "signalKind":"AzureResourceMetric","displayName":"CPU","dataUnit":"Percentage",
          "metricNamespace":"Microsoft.Compute/virtualMachines","metricName":"Percentage CPU",
          "aggregationType":"Average","refreshInterval":"PT1M","timeGrain":"PT5M",
          "evaluationRules":{"unhealthyRule":{"operator":"GreaterThan","threshold":90}}}}
        """;

    private const string CallerAuthoredSignalDefinition = """
        {"properties":{"signalKind":"LogAnalyticsQuery","displayName":"Latency"}}
        """;

    private const string UnknownKindSignalDefinition = """
        {"name":"future","type":"Microsoft.CloudHealth/healthmodels/signaldefinitions","properties":{
          "signalKind":"SomeKindFromALaterApiVersion","displayName":"Future",
          "aPropertyThisBuildHasNeverHeardOf":"keep me"}}
        """;

    [Theory]
    // Each row names something that must survive intact: a deeply nested reference, a property this build
    // has no type for, an endpoint, a kind-specific property, and a discriminator value from the future.
    [InlineData(Entity, """"signalDefinitionName":"cpu-def"""")]
    [InlineData(Entity, """"name":"platform"""")]
    [InlineData(EntityWithUnknownProperty, "aPropertyThisBuildHasNeverHeardOf")]
    [InlineData(CallerAuthoredEntity, """"signalDefinitionName":"latency"""")]
    [InlineData(Relationship, """"childEntityName":"api"""")]
    [InlineData(CallerAuthoredRelationship, """"childEntityName":"cache"""")]
    [InlineData(MetricSignalDefinition, """"metricName":"Percentage CPU"""")]
    [InlineData(CallerAuthoredSignalDefinition, """"displayName":"Latency"""")]
    [InlineData(UnknownKindSignalDefinition, "SomeKindFromALaterApiVersion")]
    [InlineData(UnknownKindSignalDefinition, "aPropertyThisBuildHasNeverHeardOf")]
    public void WireRead_KeepsEveryCallerAuthoredValue_AddingOnlyNullMembers(
        string payload, string mustSurvive)
    {
        var wired = RoundTrip(payload);

        AssertOnlyNullMembersWereAdded(JsonNode.Parse(payload)!.AsObject(), wired, "$");
        Assert.Contains(mustSurvive, wired.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The oracle, deliberately independent of <c>HealthModelMergePatch.Diff</c>. Comparing against the
    /// planner's own diff engine would only ever prove "invisible to the current diff engine", and any
    /// future weakening of that engine would make this test <em>more</em> likely to pass — which is the
    /// failure mode the test exists to catch. The one permitted difference is a member the SDK materialises
    /// as an explicit JSON null where the caller wrote nothing (<c>"signalKind":null</c> inside a signal,
    /// <c>"type":null</c> and <c>"evaluationRules":null</c> on an object): absent and null are the same
    /// property state. Anything else — a dropped member, a changed value, a changed array length, an added
    /// non-null member — is a loss or an invention.
    /// </summary>
    private static void AssertOnlyNullMembersWereAdded(JsonNode? original, JsonNode? roundTripped, string path)
    {
        if (original is JsonObject before && roundTripped is JsonObject after)
        {
            foreach (var key in before.Select(m => m.Key).Concat(after.Select(m => m.Key)).Distinct(StringComparer.Ordinal))
            {
                var child = $"{path}.{key}";
                if (!before.ContainsKey(key))
                {
                    Assert.True(after[key] is null, $"{child} was added by the round trip as {after[key]}");
                    continue;
                }

                Assert.True(after.ContainsKey(key), $"{child} was dropped by the round trip");
                AssertOnlyNullMembersWereAdded(before[key], after[key], child);
            }

            return;
        }

        if (original is JsonArray beforeItems && roundTripped is JsonArray afterItems)
        {
            Assert.True(beforeItems.Count == afterItems.Count,
                $"{path} changed length from {beforeItems.Count} to {afterItems.Count}");
            for (var i = 0; i < beforeItems.Count; i++)
            {
                AssertOnlyNullMembersWereAdded(beforeItems[i], afterItems[i], $"{path}[{i}]");
            }

            return;
        }

        Assert.True(JsonNode.DeepEquals(original, roundTripped),
            $"{path} changed from {original?.ToJsonString() ?? "null"} to {roundTripped?.ToJsonString() ?? "null"}");
    }

    /// <summary>
    /// Only the service-shaped rows: a caller-authored body is an input that is never read back until the
    /// service has stamped its own <c>id</c>/<c>name</c>/<c>type</c> envelope onto it.
    /// </summary>
    [Theory]
    [InlineData(Entity)]
    [InlineData(Relationship)]
    [InlineData(MetricSignalDefinition)]
    [InlineData(UnknownKindSignalDefinition)]
    public void WireRead_IsStable(string payload) =>
        Assert.Equal(
            RoundTrip(payload).ToJsonString(),
            RoundTrip(RoundTrip(payload).ToJsonString()).ToJsonString(),
            StringComparer.Ordinal);

    /// <summary>Dispatches on the payload's own resource type, exactly as the write runner does.</summary>
    private static JsonObject RoundTrip(string payload) => payload switch
    {
        _ when payload.Contains("/entities", StringComparison.Ordinal) => Wire<HealthModelEntityData>(payload),
        _ when payload.Contains("/relationships", StringComparison.Ordinal) => Wire<HealthModelRelationshipData>(payload),
        _ when payload.Contains("/signaldefinitions", StringComparison.Ordinal) => Wire<HealthModelSignalDefinitionData>(payload),
        // The caller-authored rows carry no envelope, so they are placed by their own required properties.
        _ when payload.Contains("EntityName", StringComparison.Ordinal) => Wire<HealthModelRelationshipData>(payload),
        _ when payload.Contains("signalGroups", StringComparison.Ordinal) => Wire<HealthModelEntityData>(payload),
        _ => Wire<HealthModelSignalDefinitionData>(payload),
    };

    private static JsonObject Wire<T>(string payload) where T : IPersistableModel<T> =>
        HealthModelWireCodec.Wire(HealthModelWireCodec.Read<T>(BinaryData.FromString(payload)));
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Planning;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// One health model with deliberately uneven contents: three entities, two relationships and two signal
/// definitions, so a count that is reported for the wrong collection cannot pass by coincidence. The entity
/// <c>api</c> is the child of one relationship and the parent of the other, which is what makes a rename
/// of it fan out to both.
/// </summary>
internal static class HealthModelGraphFixture
{
    internal static readonly PlanScope Scope = new("rg", "hm");

    private const string Entities = """
        [
          {
            "id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/entities/web",
            "name":"web",
            "type":"Microsoft.CloudHealth/healthmodels/entities",
            "properties":{
              "displayName":"Frontend",
              "healthObjective":99.9,
              "provisioningState":"Succeeded",
              "healthState":"Healthy",
              "impact":"standard",
              "canvasPosition":{"x":10,"y":20},
              "tags":{"tier":"web"}
            }
          },
          {
            "id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/entities/api",
            "name":"api",
            "type":"Microsoft.CloudHealth/healthmodels/entities",
            "properties":{
              "displayName":"API",
              "provisioningState":"Succeeded",
              "healthState":"Degraded",
              "discoveredBy":"rule-1",
              "tags":{"tier":"api","env":"prod"}
            }
          },
          {
            "id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/entities/db",
            "name":"db",
            "type":"Microsoft.CloudHealth/healthmodels/entities",
            "properties":{
              "displayName":"Database",
              "provisioningState":"Succeeded",
              "healthState":"Healthy"
            }
          }
        ]
        """;

    private const string Relationships = """
        [
          {
            "id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/relationships/web-api",
            "name":"web-api",
            "type":"Microsoft.CloudHealth/healthmodels/relationships",
            "properties":{
              "displayName":"web depends on api",
              "parentEntityName":"web",
              "childEntityName":"api",
              "provisioningState":"Succeeded"
            }
          },
          {
            "id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/relationships/api-db",
            "name":"api-db",
            "type":"Microsoft.CloudHealth/healthmodels/relationships",
            "properties":{
              "parentEntityName":"api",
              "childEntityName":"db",
              "discoveredBy":"rule-1",
              "provisioningState":"Succeeded"
            }
          }
        ]
        """;

    private const string SignalDefinitions = """
        [
          {
            "id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/signaldefinitions/cpu",
            "name":"cpu",
            "type":"Microsoft.CloudHealth/healthmodels/signaldefinitions",
            "properties":{
              "signalKind":"AzureResourceMetric",
              "displayName":"CPU",
              "metricNamespace":"Microsoft.Compute/virtualMachines",
              "metricName":"Percentage CPU",
              "provisioningState":"Succeeded"
            }
          },
          {
            "id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/signaldefinitions/requests",
            "name":"requests",
            "type":"Microsoft.CloudHealth/healthmodels/signaldefinitions",
            "properties":{
              "signalKind":"LogAnalyticsQuery",
              "displayName":"Requests",
              "provisioningState":"Succeeded"
            }
          }
        ]
        """;

    internal static HealthModelGraphSnapshot Snapshot() => new(
        Scope,
        Read(HealthModelResourceKind.Entity, Entities),
        Read(HealthModelResourceKind.Relationship, Relationships),
        Read(HealthModelResourceKind.SignalDefinition, SignalDefinitions));

    private static List<HealthModelResourceSnapshot> Read(HealthModelResourceKind kind, string json) =>
        [.. JsonNode.Parse(json)!.AsArray()
            .Select(node => node!.AsObject())
            .Select(body => new HealthModelResourceSnapshot(
                kind, body["name"]!.GetValue<string>(), body))];
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.ResourceManager.CloudHealth;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// Projection tests for the model-scope collection node. Fixtures are real service wire payloads read through
/// the SDK's own deserializer, so the polymorphic <c>signalKind</c> discriminator and its kind-specific
/// properties are exactly what a live response carries.
/// </summary>
public class HealthModelCollectionItemResultSerializationTests
{
    private static readonly ModelReaderWriterOptions JsonWireFormat = new("J");

    private const string RelationshipWire = """
        {
          "id":"/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/relationships/r-hm-leaf",
          "name":"r-hm-leaf",
          "type":"Microsoft.CloudHealth/healthmodels/relationships",
          "systemData":{"createdBy":"operator@example.com","createdByType":"User","createdAt":"2026-07-01T00:00:00Z"},
          "properties":{
            "provisioningState":"Succeeded",
            "displayName":"hm to leaf",
            "parentEntityName":"hm",
            "childEntityName":"hm-leaf-degraded",
            "discoveredBy":"discovery-rule-a",
            "tags":{"owner":"health-platform"}
          }
        }
        """;

    private const string SignalDefinitionWire = """
        {
          "id":"/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/signaldefinitions/availability",
          "name":"availability",
          "type":"Microsoft.CloudHealth/healthmodels/signaldefinitions",
          "systemData":{"createdBy":"operator@example.com","createdByType":"User","createdAt":"2026-07-01T00:00:00Z"},
          "properties":{
            "signalKind":"AzureResourceMetric",
            "provisioningState":"Succeeded",
            "displayName":"Availability",
            "refreshInterval":"PT5M",
            "dataUnit":"Percent",
            "evaluationRules":{"degradedRule":{"operator":"LessThan","threshold":99},"unhealthyRule":{"operator":"LessThan","threshold":95}},
            "metricNamespace":"microsoft.storage/storageaccounts",
            "metricName":"Availability",
            "timeGrain":"PT5M",
            "aggregationType":"Average"
          }
        }
        """;

    private static HealthModelCollectionItemResult RelationshipNode(params HealthModelFieldGroup[] fields) => new()
    {
        Name = "r-hm-leaf",
        Relationship = ModelReaderWriter.Read<HealthModelRelationshipData>(BinaryData.FromString(RelationshipWire), JsonWireFormat)!,
        Shape = HealthModelResultShape.From(fields),
    };

    private static HealthModelCollectionItemResult SignalDefinitionNode(params HealthModelFieldGroup[] fields) => new()
    {
        Name = "availability",
        SignalDefinition = ModelReaderWriter.Read<HealthModelSignalDefinitionData>(BinaryData.FromString(SignalDefinitionWire), JsonWireFormat)!,
        Shape = HealthModelResultShape.From(fields),
    };

    private static string Serialize(HealthModelCollectionItemResult node) =>
        JsonSerializer.Serialize(node, MonitorJsonContext.Default.HealthModelCollectionItemResult);

    [Fact]
    public void Serialize_DefaultRelationship_CarriesBothEndpointsAndNothingElse()
    {
        var compact = Serialize(RelationshipNode());
        using var document = JsonDocument.Parse(compact);

        var root = document.RootElement;
        Assert.Equal(["name", "relationship"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("r-hm-leaf", root.GetProperty("name").GetString());

        var properties = root.GetProperty("relationship").GetProperty("properties");
        Assert.Equal(
            ["displayName", "parentEntityName", "childEntityName", "discoveredBy"],
            properties.EnumerateObject().Select(property => property.Name));
        Assert.Equal("hm", properties.GetProperty("parentEntityName").GetString());
        Assert.Equal("hm-leaf-degraded", properties.GetProperty("childEntityName").GetString());

        // Transport and audit surface stays behind the full group.
        Assert.DoesNotContain("systemData", compact);
        Assert.DoesNotContain("provisioningState", compact);

        var full = Serialize(RelationshipNode(HealthModelFieldGroup.Full));
        Assert.Contains("\"systemData\"", full);
        Assert.Contains("\"provisioningState\"", full);
    }

    [Fact]
    public void Serialize_DefaultSignalDefinition_CarriesKindAndThresholds_WithKindSpecificDetailBehindFull()
    {
        var compact = Serialize(SignalDefinitionNode());
        using var document = JsonDocument.Parse(compact);

        var properties = document.RootElement.GetProperty("signalDefinition").GetProperty("properties");
        Assert.Equal(
            ["displayName", "signalKind", "refreshInterval", "dataUnit", "evaluationRules"],
            properties.EnumerateObject().Select(property => property.Name));

        // The polymorphic discriminator survives the compact projection: it is what makes the definition readable.
        Assert.Equal("AzureResourceMetric", properties.GetProperty("signalKind").GetString());
        Assert.Equal(95, properties.GetProperty("evaluationRules").GetProperty("unhealthyRule").GetProperty("threshold").GetDouble());

        // Kind-specific detail is opt-in, so a query-text signal cannot silently inflate a compact page.
        Assert.DoesNotContain("metricName", compact);
        Assert.DoesNotContain("aggregationType", compact);

        var full = Serialize(SignalDefinitionNode(HealthModelFieldGroup.Full));
        Assert.Contains("\"metricName\":\"Availability\"", full);
        Assert.Contains("\"aggregationType\":\"Average\"", full);
        Assert.Contains("\"signalKind\":\"AzureResourceMetric\"", full);
    }
}

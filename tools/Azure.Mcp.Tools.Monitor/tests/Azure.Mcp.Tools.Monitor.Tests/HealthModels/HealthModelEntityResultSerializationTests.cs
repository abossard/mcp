// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

public class HealthModelEntityResultSerializationTests
{
    private static readonly DateTimeOffset T1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly ModelReaderWriterOptions JsonWireFormat = new("J");

    private const string FullEntityWire = """
        {
          "id":"/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm/entities/hm-b",
          "name":"hm-b",
          "type":"Microsoft.CloudHealth/healthmodels/entities",
          "systemData":{"createdBy":"operator@example.com","createdByType":"User","createdAt":"2026-07-01T00:00:00Z","lastModifiedBy":"operator@example.com","lastModifiedByType":"User","lastModifiedAt":"2026-07-01T00:00:00Z"},
          "properties":{
            "provisioningState":"Succeeded",
            "displayName":"Health model entity B",
            "canvasPosition":{"x":12.5,"y":24.5},
            "healthObjective":99.95,
            "impact":"Standard",
            "tags":{"environment":"production","owner":"health-platform","region":"westeurope","description":"representative recorded fixture data"},
            "discoveredBy":"discovery-rule-a",
            "healthState":"Unhealthy"
          }
        }
        """;

    private static HealthModelEntityData FullEntity =>
        ModelReaderWriter.Read<HealthModelEntityData>(BinaryData.FromString(FullEntityWire), JsonWireFormat)!;

    private static string Serialize(HealthModelEntityResult node) =>
        JsonSerializer.Serialize(node, MonitorJsonContext.Default.HealthModelEntityResult);

    private static HealthModelEntityResult EntityNode(params HealthModelFieldGroup[] fields) => new()
    {
        EntityName = "hm-b",
        Success = true,
        Entity = FullEntity,
        Shape = HealthModelResultShape.From(fields),
    };

    private static HealthModelEntityResult HistoryNode(params HealthModelFieldGroup[] fields) => new()
    {
        EntityName = "hm-b",
        Success = true,
        History = ArmCloudHealthModelFactory.EntityHistoryResult(
            "hm-b",
            [ArmCloudHealthModelFactory.HealthStateTransition(EntityHealthState.Degraded, EntityHealthState.Unhealthy, T1, "cpu>90")],
            "history-next"),
        Page = new HealthModelEntityPage(false, 1, "history-next"),
        Shape = HealthModelResultShape.From(fields),
    };

    private static HealthModelEntityResult SignalHistoryNode(params HealthModelFieldGroup[] fields) => new()
    {
        EntityName = "hm-b",
        Success = true,
        SignalName = "cpu",
        SignalHistory = ArmCloudHealthModelFactory.EntitySignalHistoryResult(
            "hm-b", "cpu",
            [ArmCloudHealthModelFactory.SignalHistoryDataPoint(T1, 42.0, EntityHealthState.Degraded, "request-id=abc")],
            "signal-next"),
        Page = new HealthModelEntityPage(false, 1, "signal-next"),
        Shape = HealthModelResultShape.From(fields),
    };

    private static HealthModelEntityResult AnnotationsNode(params HealthModelFieldGroup[] fields) => new()
    {
        EntityName = "hm-b",
        Success = true,
        Annotations = ArmCloudHealthModelFactory.EntityGetDataAnnotationsResult(
            "hm-b",
            [ArmCloudHealthModelFactory.EntityDataAnnotation("a1", T1, new Dictionary<string, string> { ["deployment"] = "42" }, "deploy")],
            "annotation-next"),
        Page = new HealthModelEntityPage(false, 1, "annotation-next"),
        Shape = HealthModelResultShape.From(fields),
    };

    private static HealthModelEntityResult RecommendationsNode(params HealthModelFieldGroup[] fields) => new()
    {
        EntityName = "hm-b",
        Success = true,
        Recommendations = ArmCloudHealthModelFactory.EntityGetSignalRecommendationsResult([], []),
        Shape = HealthModelResultShape.From(fields),
    };

    [Fact]
    public void Serialize_DefaultEntity_IsCompactAtStablePath()
    {
        var compact = Serialize(EntityNode());
        using var document = JsonDocument.Parse(compact);

        var root = document.RootElement;
        Assert.Equal(["entityName", "success", "entity"], root.EnumerateObject().Select(property => property.Name));
        var entity = root.GetProperty("entity");
        Assert.Equal(["properties"], entity.EnumerateObject().Select(property => property.Name));
        var properties = entity.GetProperty("properties");
        Assert.Equal(
            ["displayName", "impact", "discoveredBy", "healthState"],
            properties.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Unhealthy", properties.GetProperty("healthState").GetString());
        Assert.DoesNotContain("systemData", compact);
        var full = Serialize(EntityNode(HealthModelFieldGroup.Full));
        var compactBytes = Encoding.UTF8.GetByteCount(compact);
        var fullBytes = Encoding.UTF8.GetByteCount(full);
        Assert.True(compactBytes * 3 < fullBytes, $"compact={compactBytes}, full={fullBytes}");
    }

    [Fact]
    public void Serialize_DefaultPageableKinds_EmitOnlyCompactItemsAndPageMetadata()
    {
        using var history = JsonDocument.Parse(Serialize(HistoryNode()));
        var historyRoot = history.RootElement;
        Assert.Equal(
            ["previousState", "newState", "occurredAt", "reason"],
            historyRoot.GetProperty("history").GetProperty("history")[0].EnumerateObject().Select(property => property.Name));
        AssertPage(historyRoot, "history-next");

        using var signal = JsonDocument.Parse(Serialize(SignalHistoryNode()));
        var signalRoot = signal.RootElement;
        Assert.Equal(
            ["occurredAt", "healthState", "value"],
            signalRoot.GetProperty("signalHistory").GetProperty("history")[0].EnumerateObject().Select(property => property.Name));
        AssertPage(signalRoot, "signal-next");

        using var annotations = JsonDocument.Parse(Serialize(AnnotationsNode()));
        var annotationRoot = annotations.RootElement;
        Assert.Equal(
            ["annotationId", "createdAt", "description"],
            annotationRoot.GetProperty("annotations").GetProperty("annotations")[0].EnumerateObject().Select(property => property.Name));
        AssertPage(annotationRoot, "annotation-next");
    }

    [Theory]
    [InlineData(HealthModelFieldGroup.Identity, "id")]
    [InlineData(HealthModelFieldGroup.Audit, "systemData")]
    [InlineData(HealthModelFieldGroup.Signals, "healthObjective")]
    [InlineData(HealthModelFieldGroup.Layout, "canvasPosition")]
    public void Serialize_EntityFieldGroups_AreAdditiveAndClosed(HealthModelFieldGroup group, string expectedProperty)
    {
        var compact = Serialize(EntityNode());
        var expanded = Serialize(EntityNode(group));

        Assert.DoesNotContain($"\"{expectedProperty}\"", compact);
        Assert.Contains($"\"{expectedProperty}\"", expanded);
        Assert.Contains("\"healthState\":\"Unhealthy\"", expanded);
    }

    [Fact]
    public void Serialize_KindSpecificGroups_AddOnlyTheirPayload()
    {
        var signal = Serialize(SignalHistoryNode(HealthModelFieldGroup.Context));
        Assert.Contains("\"additionalContext\":\"request-id=abc\"", signal);
        Assert.DoesNotContain("annotationDetails", signal);

        var annotations = Serialize(AnnotationsNode(HealthModelFieldGroup.Details));
        Assert.Contains("\"annotationDetails\":{\"deployment\":\"42\"}", annotations);
        Assert.DoesNotContain("additionalContext", annotations);
    }

    [Fact]
    public void Serialize_Recommendations_DefaultIsCompact_AndConfigurationsAreOptIn()
    {
        using var compact = JsonDocument.Parse(Serialize(RecommendationsNode()));
        var compactPayload = compact.RootElement.GetProperty("recommendations");
        Assert.True(compactPayload.TryGetProperty("recommendedSignals", out _));
        Assert.False(compactPayload.TryGetProperty("recommendedConfigurations", out _));

        using var expanded = JsonDocument.Parse(
            Serialize(RecommendationsNode(HealthModelFieldGroup.Configurations)));
        Assert.True(expanded.RootElement.GetProperty("recommendations")
            .TryGetProperty("recommendedConfigurations", out _));
    }

    [Fact]
    public void Serialize_NonPageableAndFailedNodes_OmitPage()
    {
        using var recommendations = JsonDocument.Parse(Serialize(RecommendationsNode()));
        Assert.False(recommendations.RootElement.TryGetProperty("page", out _));

        using var failed = JsonDocument.Parse(Serialize(new HealthModelEntityResult
        {
            EntityName = "hm-b",
            Success = false,
            Error = "not found",
        }));
        Assert.False(failed.RootElement.TryGetProperty("page", out _));
    }

    [Theory]
    [MemberData(nameof(FullPayloadCases))]
    public void Serialize_Full_EqualsDirectSdkJson(HealthModelEntityResult node, string payloadName, BinaryData expected)
    {
        using var document = JsonDocument.Parse(Serialize(node));

        Assert.Equal(expected.ToString(), document.RootElement.GetProperty(payloadName).GetRawText());
    }

    public static TheoryData<HealthModelEntityResult, string, BinaryData> FullPayloadCases => new()
    {
        { EntityNode(HealthModelFieldGroup.Full), "entity", DirectWrite(FullEntity) },
        { HistoryNode(HealthModelFieldGroup.Full), "history", DirectWrite(HistoryNode().History!) },
        { SignalHistoryNode(HealthModelFieldGroup.Full), "signalHistory", DirectWrite(SignalHistoryNode().SignalHistory!) },
        { RecommendationsNode(HealthModelFieldGroup.Full), "recommendations", DirectWrite(RecommendationsNode().Recommendations!) },
        { AnnotationsNode(HealthModelFieldGroup.Full), "annotations", DirectWrite(AnnotationsNode().Annotations!) },
    };

    private static BinaryData DirectWrite<T>(T payload)
        where T : class, IJsonModel<T>
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        payload.Write(writer, JsonWireFormat);
        writer.Flush();
        return BinaryData.FromBytes(buffer.WrittenMemory);
    }

    private static void AssertPage(JsonElement root, string expectedMarker)
    {
        var page = root.GetProperty("page");
        Assert.False(page.GetProperty("complete").GetBoolean());
        Assert.Equal(1, page.GetProperty("returnedCount").GetInt32());
        Assert.Equal(expectedMarker, page.GetProperty("cursor").GetString());
    }
}

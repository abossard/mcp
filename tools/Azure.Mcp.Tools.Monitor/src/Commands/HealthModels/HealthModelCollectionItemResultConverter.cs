// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.ResourceManager.CloudHealth;

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

internal sealed class HealthModelCollectionItemResultConverter : JsonConverter<HealthModelCollectionItemResult>
{
    private static readonly ModelReaderWriterOptions JsonWireFormat = new("J");

    /// <summary>
    /// The compact signal-definition keys, in output order: what the signal is, how often it runs, the unit
    /// it reports, and the thresholds that decide its health state. Kind-specific query or metric detail stays
    /// behind the <c>full</c> field group.
    /// </summary>
    private static readonly string[] CompactSignalDefinitionProperties =
    [
        "displayName",
        "signalKind",
        "refreshInterval",
        "dataUnit",
        "evaluationRules",
    ];

    public override void Write(Utf8JsonWriter writer, HealthModelCollectionItemResult value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        WriteString(writer, "name", value.Name);

        if (value.Relationship is not null)
        {
            WriteRelationship(writer, value.Relationship, value.Shape);
        }
        if (value.SignalDefinition is not null)
        {
            WriteSignalDefinition(writer, value.SignalDefinition, value.Shape);
        }

        writer.WriteEndObject();
    }

    private static void WriteRelationship(
        Utf8JsonWriter writer,
        HealthModelRelationshipData relationship,
        HealthModelResultShape shape)
    {
        writer.WritePropertyName("relationship");
        if (shape.Includes(HealthModelFieldGroup.Full))
        {
            ((IJsonModel<HealthModelRelationshipData>)relationship).Write(writer, JsonWireFormat);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("properties");
        writer.WriteStartObject();

        var properties = relationship.Properties;
        if (properties is not null)
        {
            WriteString(writer, "displayName", properties.DisplayName);
            WriteString(writer, "parentEntityName", properties.ParentEntityName);
            WriteString(writer, "childEntityName", properties.ChildEntityName);
            WriteString(writer, "discoveredBy", properties.DiscoveredBy);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteSignalDefinition(
        Utf8JsonWriter writer,
        HealthModelSignalDefinitionData signalDefinition,
        HealthModelResultShape shape)
    {
        writer.WritePropertyName("signalDefinition");
        if (shape.Includes(HealthModelFieldGroup.Full))
        {
            ((IJsonModel<HealthModelSignalDefinitionData>)signalDefinition).Write(writer, JsonWireFormat);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        WriteAllowedProperties(writer, signalDefinition, CompactSignalDefinitionProperties);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Projects a compact view straight from the SDK's own wire JSON rather than from typed properties.
    /// <c>HealthModelSignalDefinitionProperties</c> is an abstract polymorphic bag whose discriminator
    /// (<c>signalKind</c>) is not surfaced on the base type, so reading the serialized form keeps the
    /// projection correct for signal kinds this build has never seen.
    /// </summary>
    private static void WriteAllowedProperties(
        Utf8JsonWriter writer,
        HealthModelSignalDefinitionData signalDefinition,
        string[] allowed)
    {
        var payload = ((IPersistableModel<HealthModelSignalDefinitionData>)signalDefinition).Write(JsonWireFormat);
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var name in allowed)
        {
            if (properties.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
        }
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
    }

    public override HealthModelCollectionItemResult Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "HealthModelCollectionItemResult is output-only; the SDK payloads are never deserialized by MCP.");
}

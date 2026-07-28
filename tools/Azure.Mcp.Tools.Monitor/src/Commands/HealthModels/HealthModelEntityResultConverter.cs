// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

internal sealed class HealthModelEntityResultConverter : JsonConverter<HealthModelEntityResult>
{
    private static readonly ModelReaderWriterOptions JsonWireFormat = new("J");

    public override void Write(Utf8JsonWriter writer, HealthModelEntityResult value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        WriteString(writer, "entityName", value.EntityName);
        writer.WriteBoolean("success", value.Success);
        WriteString(writer, "error", value.Error);
        WriteString(writer, "signalName", value.SignalName);

        if (value.Entity is not null)
        {
            WriteEntity(writer, value.Entity, value.Shape);
        }
        if (value.History is not null)
        {
            WriteHistory(writer, value.History, value.Shape);
        }
        if (value.SignalHistory is not null)
        {
            WriteSignalHistory(writer, value.SignalHistory, value.Shape);
        }
        if (value.Recommendations is not null)
        {
            WriteRecommendations(writer, value.Recommendations, value.Shape);
        }
        if (value.Annotations is not null)
        {
            WriteAnnotations(writer, value.Annotations, value.Shape);
        }
        if (value.Page is not null)
        {
            WritePage(writer, value.Page);
        }

        writer.WriteEndObject();
    }

    private static void WriteEntity(
        Utf8JsonWriter writer,
        HealthModelEntityData entity,
        HealthModelResultShape shape)
    {
        writer.WritePropertyName("entity");
        if (shape.Includes(HealthModelFieldGroup.Full))
        {
            ((IJsonModel<HealthModelEntityData>)entity).Write(writer, JsonWireFormat);
            return;
        }

        writer.WriteStartObject();

        if (shape.Includes(HealthModelFieldGroup.Identity))
        {
            WriteString(writer, "id", entity.Id?.ToString());
            WriteString(writer, "name", entity.Name);
            WriteString(writer, "type", entity.ResourceType.ToString());
        }

        if (shape.Includes(HealthModelFieldGroup.Audit) && entity.SystemData is not null)
        {
            WritePayload(writer, "systemData", entity.SystemData);
        }

        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        var properties = entity.Properties;
        if (properties is not null)
        {
            WriteString(writer, "displayName", properties.DisplayName);
            WriteString(writer, "impact", properties.Impact?.ToString());
            WriteString(writer, "discoveredBy", properties.DiscoveredBy);
            WriteString(writer, "healthState", properties.HealthState?.ToString());

            if (shape.Includes(HealthModelFieldGroup.Audit))
            {
                WriteString(writer, "provisioningState", properties.ProvisioningState?.ToString());
                WriteDictionary(writer, "tags", properties.Tags);
            }
            if (shape.Includes(HealthModelFieldGroup.Signals))
            {
                if (properties.HealthObjective is float healthObjective)
                {
                    writer.WriteNumber("healthObjective", healthObjective);
                }
                WritePayload(writer, "signalGroups", properties.SignalGroups);
                WritePayload(writer, "alerts", properties.Alerts);
            }
            if (shape.Includes(HealthModelFieldGroup.Layout))
            {
                WritePayload(writer, "canvasPosition", properties.CanvasPosition);
                WritePayload(writer, "icon", properties.Icon);
            }
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteHistory(
        Utf8JsonWriter writer,
        EntityHistoryResult history,
        HealthModelResultShape shape)
    {
        if (shape.Includes(HealthModelFieldGroup.Full))
        {
            WritePayload(writer, "history", history);
            return;
        }

        writer.WritePropertyName("history");
        writer.WriteStartObject();
        writer.WritePropertyName("history");
        writer.WriteStartArray();
        foreach (var transition in history.History)
        {
            writer.WriteStartObject();
            WriteString(writer, "previousState", transition.PreviousState.ToString());
            WriteString(writer, "newState", transition.NewState.ToString());
            writer.WriteString("occurredAt", transition.OccurredOn);
            WriteString(writer, "reason", transition.Reason);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSignalHistory(
        Utf8JsonWriter writer,
        EntitySignalHistoryResult signalHistory,
        HealthModelResultShape shape)
    {
        if (shape.Includes(HealthModelFieldGroup.Full))
        {
            WritePayload(writer, "signalHistory", signalHistory);
            return;
        }

        writer.WritePropertyName("signalHistory");
        writer.WriteStartObject();
        writer.WritePropertyName("history");
        writer.WriteStartArray();
        foreach (var point in signalHistory.History)
        {
            writer.WriteStartObject();
            writer.WriteString("occurredAt", point.OccurredOn);
            WriteString(writer, "healthState", point.HealthState.ToString());
            if (point.Value is double value)
            {
                writer.WriteNumber("value", value);
            }
            else
            {
                writer.WriteNull("value");
            }
            if (shape.Includes(HealthModelFieldGroup.Context))
            {
                WriteString(writer, "additionalContext", point.AdditionalContext);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRecommendations(
        Utf8JsonWriter writer,
        EntityGetSignalRecommendationsResult recommendations,
        HealthModelResultShape shape)
    {
        if (shape.Includes(HealthModelFieldGroup.Full))
        {
            WritePayload(writer, "recommendations", recommendations);
            return;
        }

        writer.WritePropertyName("recommendations");
        writer.WriteStartObject();
        WritePayloadArray(writer, "recommendedSignals", recommendations.RecommendedSignals);
        if (shape.Includes(HealthModelFieldGroup.Configurations))
        {
            WritePayloadArray(writer, "recommendedConfigurations", recommendations.RecommendedConfigurations);
        }
        writer.WriteEndObject();
    }

    private static void WriteAnnotations(
        Utf8JsonWriter writer,
        EntityGetDataAnnotationsResult annotations,
        HealthModelResultShape shape)
    {
        if (shape.Includes(HealthModelFieldGroup.Full))
        {
            WritePayload(writer, "annotations", annotations);
            return;
        }

        writer.WritePropertyName("annotations");
        writer.WriteStartObject();
        writer.WritePropertyName("annotations");
        writer.WriteStartArray();
        foreach (var annotation in annotations.Annotations)
        {
            writer.WriteStartObject();
            WriteString(writer, "annotationId", annotation.AnnotationId);
            if (annotation.CreatedOn is DateTimeOffset createdOn)
            {
                writer.WriteString("createdAt", createdOn);
            }
            WriteString(writer, "description", annotation.Description);
            if (shape.Includes(HealthModelFieldGroup.Details))
            {
                WriteDictionary(writer, "annotationDetails", annotation.AnnotationDetails);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePage(Utf8JsonWriter writer, HealthModelEntityPage page)
    {
        writer.WritePropertyName("page");
        writer.WriteStartObject();
        writer.WriteBoolean("complete", page.Complete);
        writer.WriteNumber("returnedCount", page.ReturnedCount);
        WriteString(writer, "nextMarker", page.NextMarker);
        writer.WriteEndObject();
    }

    private static void WritePayloadArray<T>(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<T> payloads)
        where T : class, IJsonModel<T>
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var payload in payloads)
        {
            payload.Write(writer, JsonWireFormat);
        }
        writer.WriteEndArray();
    }

    private static void WritePayload<T>(Utf8JsonWriter writer, string propertyName, T? payload)
        where T : class, IJsonModel<T>
    {
        if (payload is null)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        payload.Write(writer, JsonWireFormat);
    }

    private static void WriteDictionary(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<KeyValuePair<string, string>> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        foreach (var (key, value) in values)
        {
            writer.WriteString(key, value);
        }
        writer.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
    }

    public override HealthModelEntityResult Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "HealthModelEntityResult is output-only; the SDK payloads are never deserialized by MCP.");
}

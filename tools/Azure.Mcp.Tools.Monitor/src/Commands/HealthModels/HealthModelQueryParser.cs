// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

/// <summary>
/// Reads the batch of queries from the caller's JSON array, dispatching each element to the concrete type
/// its <c>kind</c> names.
/// </summary>
/// <remarks>
/// The discriminator is read from a buffered element rather than by the built-in polymorphic reader, which
/// requires the discriminator to be the first property and throws <see cref="NotSupportedException"/>
/// otherwise. Reading it positionally makes property order irrelevant. Every concrete type declares
/// <c>JsonUnmappedMemberHandling.Disallow</c>, so a property belonging to a different kind is reported by
/// name instead of being silently dropped.
/// </remarks>
internal static class HealthModelQueryParser
{
    private const string KindProperty = "kind";

    /// <summary>The accepted <c>kind</c> values, in the order they appear in the schema.</summary>
    internal static readonly string[] Kinds =
    [
        "entityList",
        "entityGet",
        "entityHistory",
        "signalHistory",
        "signalRecommendations",
        "dataAnnotations",
        "relationshipList",
        "signalDefinitionList",
    ];

    internal static bool TryParse(string? json, out IReadOnlyList<HealthModelQuery> queries, out string error)
    {
        queries = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "--queries is required and must be a JSON array of health-model queries.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"--queries must be a valid JSON array of health-model queries. {ex.Message}";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "--queries must be a JSON array of health-model queries.";
                return false;
            }

            // A malformed element occupies its slot rather than failing the batch: results stay
            // one-per-input-query, and its siblings still run.
            var parsed = new List<HealthModelQuery>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                parsed.Add(ParseQuery(element, parsed.Count));
            }

            if (parsed.Count == 0)
            {
                error = "--queries must contain at least one query.";
                return false;
            }

            queries = parsed;
            return true;
        }
    }

    private static HealthModelQuery ParseQuery(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Malformed($"queries[{index}] must be an object.");
        }

        if (!element.TryGetProperty(KindProperty, out var kindElement) ||
            kindElement.ValueKind != JsonValueKind.String ||
            kindElement.GetString() is not { Length: > 0 } kind)
        {
            return Malformed($"queries[{index}] is missing the required 'kind' property. Accepted kinds: {AcceptedKinds}.");
        }

        var raw = element.GetRawText();
        try
        {
            return kind switch
            {
                "entityList" => Read(raw, MonitorJsonContext.Default.EntityListQuery),
                "entityGet" => Read(raw, MonitorJsonContext.Default.EntityGetQuery),
                "entityHistory" => Read(raw, MonitorJsonContext.Default.EntityHistoryQuery),
                "signalHistory" => Read(raw, MonitorJsonContext.Default.SignalHistoryQuery),
                "signalRecommendations" => Read(raw, MonitorJsonContext.Default.SignalRecommendationsQuery),
                "dataAnnotations" => Read(raw, MonitorJsonContext.Default.DataAnnotationsQuery),
                "relationshipList" => Read(raw, MonitorJsonContext.Default.RelationshipListQuery),
                "signalDefinitionList" => Read(raw, MonitorJsonContext.Default.SignalDefinitionListQuery),
                _ => Malformed($"queries[{index}] uses unknown kind '{kind}'. Accepted kinds: {AcceptedKinds}.", kind),
            };
        }
        catch (JsonException ex)
        {
            return Malformed($"queries[{index}] ({kind}) is invalid. {ex.Message}", kind);
        }
    }

    private static MalformedQuery Malformed(string error, string kind = "") =>
        new() { Kind = kind, Error = error };

    private static string AcceptedKinds => string.Join(", ", Kinds);

    private static HealthModelQuery Read<T>(string raw, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : HealthModelQuery =>
        JsonSerializer.Deserialize(raw, typeInfo) ?? throw new JsonException("the query was null.");
}

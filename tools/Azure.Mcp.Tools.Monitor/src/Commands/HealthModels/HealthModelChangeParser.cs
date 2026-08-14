// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

/// <summary>
/// Reads the change set from the caller's JSON array, dispatching each element to the concrete type its
/// <c>kind</c> names.
/// </summary>
/// <remarks>
/// The discriminator is read from a buffered element rather than by the built-in polymorphic reader, which
/// requires the discriminator to be the first property and throws <see cref="NotSupportedException"/>
/// otherwise. Reading it positionally makes property order irrelevant. Every concrete type declares
/// <c>JsonUnmappedMemberHandling.Disallow</c>, so a property belonging to a different operation is reported
/// by name instead of being silently dropped.
/// </remarks>
internal static class HealthModelChangeParser
{
    private const string KindProperty = "kind";

    /// <summary>The accepted <c>kind</c> values, in the order they appear in the schema.</summary>
    internal static readonly string[] Kinds =
    [
        "create",
        "patch",
        "rename",
        "delete",
    ];

    internal static bool TryParse(string? json, out IReadOnlyList<HealthModelChange> changes, out string error)
    {
        changes = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "--changes is required and must be a JSON array of health-model change elements.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"--changes must be a valid JSON array of health-model change elements. {ex.Message}";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "--changes must be a JSON array of health-model change elements.";
                return false;
            }

            // A malformed element occupies its slot rather than failing the batch: results stay
            // one-per-input-element, and its siblings still execute.
            var parsed = new List<HealthModelChange>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                parsed.Add(ParseChange(element, parsed.Count));
            }

            if (parsed.Count == 0)
            {
                error = "--changes must contain at least one change element.";
                return false;
            }

            changes = parsed;
            return true;
        }
    }

    private static HealthModelChange ParseChange(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Malformed($"changes[{index}] must be an object.");
        }

        if (!element.TryGetProperty(KindProperty, out var kindElement) ||
            kindElement.ValueKind != JsonValueKind.String ||
            kindElement.GetString() is not { Length: > 0 } kind)
        {
            return Malformed($"changes[{index}] is missing the required 'kind' property. Accepted kinds: {AcceptedKinds}.");
        }

        var raw = element.GetRawText();
        try
        {
            return kind switch
            {
                "create" => Read(raw, MonitorJsonContext.Default.CreateChange),
                "patch" => Read(raw, MonitorJsonContext.Default.PatchChange),
                "rename" => Read(raw, MonitorJsonContext.Default.RenameChange),
                "delete" => Read(raw, MonitorJsonContext.Default.DeleteChange),
                _ => Malformed($"changes[{index}] uses unknown kind '{kind}'. Accepted kinds: {AcceptedKinds}.", kind),
            };
        }
        catch (JsonException ex)
        {
            return Malformed($"changes[{index}] ({kind}) is invalid. {ex.Message}", kind);
        }
    }

    private static MalformedChange Malformed(string error, string kind = "") =>
        new() { Kind = kind, Error = error };

    private static string AcceptedKinds => string.Join(", ", Kinds);

    private static HealthModelChange Read<T>(string raw, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : HealthModelChange =>
        JsonSerializer.Deserialize(raw, typeInfo) ?? throw new JsonException("the change element was null.");
}

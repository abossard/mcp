// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;

namespace Azure.Mcp.Tools.HealthModels.Models;

internal static class HealthModelsInput
{
    public static IReadOnlyDictionary<string, string>? ParseDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new ArgumentException("Expected a JSON object of string key/value pairs.");

        var result = new Dictionary<string, string>();
        foreach (var pair in node)
        {
            result[pair.Key] = pair.Value?.GetValue<string>() ?? string.Empty;
        }

        return result;
    }

    public static IEnumerable<string>? ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

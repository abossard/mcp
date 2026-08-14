// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// What one sandbox run produced. A script that throws reports <see cref="Error"/> with whatever it logged
/// before failing, so a partial run is still diagnosable.
/// </summary>
public sealed class HealthModelScriptResult
{
    [JsonPropertyName("result")]
    public JsonNode? Result { get; set; }

    [JsonPropertyName("logs")]
    public List<string> Logs { get; set; } = [];

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>Set when the result exceeded the output budget and was capped.</summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Truncated { get; set; }

    /// <summary>
    /// How many Azure calls the script completed. On a script that hit a limit this is the progress
    /// marker: a bare "exceeded the limit" says nothing about how much of a write batch already applied.
    /// </summary>
    [JsonPropertyName("azureCalls")]
    public int AzureCalls { get; set; }
}

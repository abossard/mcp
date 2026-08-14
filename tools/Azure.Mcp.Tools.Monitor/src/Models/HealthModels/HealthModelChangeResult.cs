// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// The result of a single change element, correlated back to the caller by its system-assigned
/// <see cref="ChangeIndex"/> (the element's zero-based input position). Results are returned in input order.
///
/// <see cref="Success"/> is false only for whole-element failures — a malformed element, a rejected
/// read-only property, or a selector that matched nothing. Once targets are resolved the element is
/// <see cref="Success"/> true and an individual target failure is isolated on its own
/// <see cref="HealthModelTargetResult"/> node.
/// </summary>
public sealed class HealthModelChangeResult
{
    /// <summary>The element's stable zero-based input position; results are returned in this order.</summary>
    [JsonPropertyName("changeIndex")]
    public int ChangeIndex { get; set; }

    /// <summary>The optional caller-supplied label, echoed verbatim. Omitted from JSON when absent.</summary>
    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>Every resource this element resolved to, in the order the writes would be issued.</summary>
    [JsonPropertyName("targets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HealthModelTargetResult>? Targets { get; set; }

    /// <summary>The collection sizes the selector was resolved against.</summary>
    [JsonPropertyName("scanned")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HealthModelScanCounts? Scanned { get; set; }
}

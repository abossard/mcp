// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// One resource a change resolved to, with what would happen (or did happen) to it. A failing target is
/// isolated on its own node so its siblings still execute.
/// </summary>
public sealed class HealthModelTargetResult
{
    /// <summary>The resource name the action applies to.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Which collection the target lives in.</summary>
    [JsonPropertyName("resourceKind")]
    public HealthModelResourceKind ResourceKind { get; set; }

    /// <summary>What the change does to this target.</summary>
    [JsonPropertyName("action")]
    public HealthModelChangeAction Action { get; set; }

    /// <summary>The property-level differences this action would write. Empty for a no-op or a delete.</summary>
    [JsonPropertyName("changes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HealthModelPropertyChange>? Changes { get; set; }

    /// <summary>False only when the write for this target was attempted and failed.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>Why the target was not attempted, naming the target it depended on.</summary>
    [JsonPropertyName("skipReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SkipReason { get; set; }

    /// <summary>
    /// Set on a target whose write succeeded and was then removed by a later element, naming that element.
    /// <see cref="Success"/> stays true because the write really did happen; this says it no longer stands,
    /// which is the difference between a write that was undone and one that was never reported at all.
    /// </summary>
    [JsonPropertyName("supersededBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SupersededBy { get; set; }
}

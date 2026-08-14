// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// The batch envelope for a graph edit. The two fail-closed guards (declared affected count, snapshot
/// drift) reject the whole change set before any write, which needs a batch-level slot that a per-element
/// list cannot provide.
/// </summary>
public sealed class HealthModelGraphEditResult
{
    /// <summary>Whether the change set was only computed or also written.</summary>
    [JsonPropertyName("mode")]
    public HealthModelChangeMode Mode { get; set; }

    /// <summary>False only for a whole-batch rejection; per-element outcomes live on their own nodes.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>
    /// The number of targets whose action is not <c>noOp</c>. Echo this back as
    /// <c>expect.affectedCount</c> to apply the same change set.
    /// </summary>
    [JsonPropertyName("affectedCount")]
    public int AffectedCount { get; set; }

    /// <summary>
    /// A hash of the current state of every matched target. Echo it back as <c>expect.snapshot</c> to make
    /// the apply fail if the model drifted in between.
    /// </summary>
    [JsonPropertyName("snapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Snapshot { get; set; }

    /// <summary>One result per input element, in input order.</summary>
    [JsonPropertyName("changes")]
    public IReadOnlyList<HealthModelChangeResult> Changes { get; set; } = [];
}

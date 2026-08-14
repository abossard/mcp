// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// What the caller declares it expects the change set to do. There is no ETag and no server-side what-if
/// on this resource provider, so both guards are client-side and fail closed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HealthModelChangeExpectation
{
    /// <summary>
    /// The number of targets the caller expects to be written, echoed from a prior what-if. Required for
    /// <c>apply</c>; a mismatch fails the whole change set before any write.
    /// </summary>
    [JsonPropertyName("affectedCount")]
    public int? AffectedCount { get; set; }

    /// <summary>
    /// The optional snapshot token from a prior what-if. When supplied and the matched targets no longer
    /// hash to it, the whole change set fails before any write.
    /// </summary>
    [JsonPropertyName("snapshot")]
    public string? Snapshot { get; set; }
}

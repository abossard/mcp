// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// Paging for a kind whose Azure operation has no page-size input. Only the cursor applies, so a size
/// cannot be sent where it would have no effect.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HealthModelCursorPage
{
    /// <summary>
    /// The opaque cursor from a previous response's <c>page.cursor</c>, echoed back verbatim to read the
    /// next page of this collection.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

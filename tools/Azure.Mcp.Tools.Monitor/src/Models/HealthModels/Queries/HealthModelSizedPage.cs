// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// Paging for a kind whose Azure operation accepts a page size, plus the cursor that resumes it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HealthModelSizedPage
{
    /// <summary>The Azure API page size. This is a per-page maximum, not a total limit.</summary>
    [JsonPropertyName("size")]
    public int? Size { get; set; }

    /// <summary>
    /// The opaque cursor from a previous response's <c>page.cursor</c>, echoed back verbatim to read the
    /// next page. What it resumes is fixed by the query's target: one entity's own page when
    /// <see cref="HealthModelTarget.Entity"/> is set, or the shared discovery page when
    /// <see cref="HealthModelTarget.WhereHealth"/> is used.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

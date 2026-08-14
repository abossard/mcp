// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// Upserts one named resource from a full official-shaped body. The write is a PUT, so an existing
/// resource whose body already equals the desired one is reported as a no-op rather than rewritten.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateChange : HealthModelChange
{
    /// <summary>The resource name, which is the last URL path segment and therefore its identity.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The official body, under the key naming the collection it belongs to.</summary>
    [JsonPropertyName("resource")]
    public HealthModelResourceBody? Resource { get; set; }

    internal override HealthModelChangeKind ChangeKind => HealthModelChangeKind.Create;
}

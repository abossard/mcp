// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// Applies one RFC 7386 merge patch to every selected resource: a null value removes the property, a
/// nested object merges, and an array replaces.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PatchChange : HealthModelChange
{
    /// <summary>Which resources to patch.</summary>
    [JsonPropertyName("select")]
    public HealthModelSelector? Select { get; set; }

    /// <summary>The merge patch, shaped like the official body (<c>{"properties":{...}}</c>).</summary>
    [JsonPropertyName("patch")]
    public HealthModelResourceDocument? Patch { get; set; }

    /// <summary>Accept a selector that matches nothing instead of failing this element.</summary>
    [JsonPropertyName("allowEmptyMatch")]
    public bool? AllowEmptyMatch { get; set; }

    internal override HealthModelChangeKind ChangeKind => HealthModelChangeKind.Patch;
}

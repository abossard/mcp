// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// Moves every selected resource to <see cref="NewName"/>. A name is a URL path segment, so this expands
/// into a create under the new name, a repoint of every relationship that referenced the old name, and a
/// delete of the old name — all visible before any write.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RenameChange : HealthModelChange
{
    /// <summary>Which resources to rename.</summary>
    [JsonPropertyName("select")]
    public HealthModelSelector? Select { get; set; }

    /// <summary>The new name.</summary>
    [JsonPropertyName("newName")]
    public string NewName { get; set; } = string.Empty;

    /// <summary>Accept a selector that matches nothing instead of failing this element.</summary>
    [JsonPropertyName("allowEmptyMatch")]
    public bool? AllowEmptyMatch { get; set; }

    internal override HealthModelChangeKind ChangeKind => HealthModelChangeKind.Rename;
}

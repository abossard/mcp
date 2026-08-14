// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// Removes every selected resource. Deleting an entity that is still an endpoint of a relationship is
/// rejected rather than cascaded, so an edge is never silently orphaned.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DeleteChange : HealthModelChange
{
    /// <summary>Which resources to delete.</summary>
    [JsonPropertyName("select")]
    public HealthModelSelector? Select { get; set; }

    /// <summary>Accept a selector that matches nothing instead of failing this element.</summary>
    [JsonPropertyName("allowEmptyMatch")]
    public bool? AllowEmptyMatch { get; set; }

    internal override HealthModelChangeKind ChangeKind => HealthModelChangeKind.Delete;
}

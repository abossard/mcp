// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// Which entities a per-entity query runs against: exactly one named entity, or every entity in a health
/// state. Grouping the two into one object makes the choice explicit rather than leaving two sibling
/// fields that happen to be mutually exclusive.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HealthModelTarget
{
    /// <summary>The single entity to read. Mutually exclusive with <see cref="WhereHealth"/>.</summary>
    [JsonPropertyName("entity")]
    public string? Entity { get; set; }

    /// <summary>
    /// Run against every entity currently in this health state, resolved from one shared entity list.
    /// Mutually exclusive with <see cref="Entity"/>.
    /// </summary>
    [JsonPropertyName("whereHealth")]
    public HealthModelHealthFilter? WhereHealth { get; set; }
}

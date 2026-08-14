// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>Reads one named entity of a health model.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EntityGetQuery : HealthModelQuery
{
    /// <summary>The entity to read.</summary>
    [JsonPropertyName("entity")]
    public string? Entity { get; set; }

    /// <summary>Field groups added to the compact entity payload.</summary>
    [JsonPropertyName("select")]
    public IReadOnlyList<EntitySelection>? Select { get; set; }

    internal override HealthModelQueryKind QueryKind => HealthModelQueryKind.EntityGet;
}

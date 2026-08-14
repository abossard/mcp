// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>Reads the data annotations of one or more entities.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DataAnnotationsQuery : HealthModelQuery
{
    /// <summary>Which entities to read: one named entity, or every entity in a health state.</summary>
    [JsonPropertyName("target")]
    public HealthModelTarget? Target { get; set; }

    /// <summary>Inclusive time range. <c>from</c> must be within 30 days of the current time.</summary>
    [JsonPropertyName("window")]
    public HealthModelWindow? Window { get; set; }

    /// <summary>One page per request; echo the previous response's <c>page.cursor</c> to continue.</summary>
    [JsonPropertyName("page")]
    public HealthModelSizedPage? Page { get; set; }

    /// <summary>Field groups added to the compact annotation payload.</summary>
    [JsonPropertyName("select")]
    public IReadOnlyList<AnnotationSelection>? Select { get; set; }

    internal override HealthModelQueryKind QueryKind => HealthModelQueryKind.DataAnnotations;
}

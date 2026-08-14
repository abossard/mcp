// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>Field groups a data-annotation payload can add to its compact projection.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnnotationSelection>))]
public enum AnnotationSelection
{
    /// <summary>The free-form annotation details. May be kilobytes.</summary>
    Details,

    /// <summary>The exact CloudHealth SDK payload.</summary>
    Full,
}

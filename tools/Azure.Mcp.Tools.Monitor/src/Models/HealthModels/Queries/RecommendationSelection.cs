// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>Field groups a signal-recommendations payload can add to its compact projection.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecommendationSelection>))]
public enum RecommendationSelection
{
    /// <summary>The recommended signal configurations. Collection-scaled.</summary>
    Configurations,

    /// <summary>The exact CloudHealth SDK payload.</summary>
    Full,
}

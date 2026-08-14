// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// The only selection a kind offers when its compact projection has no optional groups: the exact
/// CloudHealth SDK payload.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FullSelection>))]
public enum FullSelection
{
    /// <summary>The exact CloudHealth SDK payload.</summary>
    Full,
}

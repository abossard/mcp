// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>An inclusive time range for the history and annotation kinds.</summary>
/// <remarks>
/// <see cref="From"/> must be within 30 days of the current time; the service measures that window from
/// now, not from <see cref="To"/>, so a <see cref="To"/> in the past does not extend the reach.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HealthModelWindow
{
    /// <summary>Inclusive start of the range.</summary>
    [JsonPropertyName("from")]
    public DateTimeOffset? From { get; set; }

    /// <summary>Inclusive end of the range.</summary>
    [JsonPropertyName("to")]
    public DateTimeOffset? To { get; set; }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// Selects a subset of a health model's entities by health state so that a per-entity query can run
/// against only the matching entities (e.g. "signal history for entities that are not healthy").
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthModelHealthFilter>))]
public enum HealthModelHealthFilter
{
    /// <summary>Entities whose health state is Unhealthy.</summary>
    Unhealthy,

    /// <summary>Entities whose health state is Degraded.</summary>
    Degraded,

    /// <summary>Entities whose health state is Unknown.</summary>
    Unknown,

    /// <summary>
    /// Entities whose health state is Unhealthy, Degraded, or Unknown. This is a closed set: the Azure health
    /// state is an extensible enum, so any other state (for example Deleted, or a state Azure adds later) is
    /// excluded. Omit the filter to receive every entity regardless of state.
    /// </summary>
    NotHealthy,
}

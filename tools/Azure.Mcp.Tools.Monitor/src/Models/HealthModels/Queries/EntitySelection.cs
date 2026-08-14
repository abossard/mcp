// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// Field groups an entity payload can add to its compact projection. Each query kind has its own
/// selection enum, so a group that does nothing for a kind cannot be selected on it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EntitySelection>))]
public enum EntitySelection
{
    /// <summary>Resource id, name, and type.</summary>
    Identity,

    /// <summary>System metadata, provisioning state, and tags.</summary>
    Audit,

    /// <summary>Health objective, signal groups, and alerts. Collection-scaled.</summary>
    Signals,

    /// <summary>Canvas position and icon.</summary>
    Layout,

    /// <summary>The exact CloudHealth SDK payload.</summary>
    Full,
}

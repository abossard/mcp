// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>Whether a graph edit only computes its change set or also writes it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthModelChangeMode>))]
public enum HealthModelChangeMode
{
    /// <summary>Compute and return the change set without issuing a single write. The default.</summary>
    [JsonStringEnumMemberName("whatIf")]
    WhatIf,

    /// <summary>Write the computed change set. Requires a matching declared affected count.</summary>
    [JsonStringEnumMemberName("apply")]
    Apply,
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>Field groups a signal-history payload can add to its compact projection.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SignalHistorySelection>))]
public enum SignalHistorySelection
{
    /// <summary>The per-point additional context string.</summary>
    Context,

    /// <summary>The exact CloudHealth SDK payload.</summary>
    Full,
}

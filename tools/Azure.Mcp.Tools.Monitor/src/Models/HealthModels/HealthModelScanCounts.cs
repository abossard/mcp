// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>
/// How many resources each collection held when the selectors were resolved. Selection runs against a
/// fully enumerated snapshot, never a partial page, and these counts say what that snapshot contained.
/// </summary>
public sealed record HealthModelScanCounts(
    [property: JsonPropertyName("entities")] int Entities,
    [property: JsonPropertyName("relationships")] int Relationships,
    [property: JsonPropertyName("signalDefinitions")] int SignalDefinitions);

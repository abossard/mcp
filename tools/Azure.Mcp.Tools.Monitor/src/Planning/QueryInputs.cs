// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// The call inputs a typed query flattens to. Every kind produces one of these, leaving unused inputs null,
/// so grouping, deduplication, and ordering work over one uniform shape.
/// </summary>
internal sealed record QueryInputs(
    string? EntityName = null,
    string? SignalName = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? Size = null,
    DateTimeOffset? AsOf = null,
    HealthModelHealthFilter? WhereHealth = null,
    string? Cursor = null,
    HealthModelResultShape? Shape = null);

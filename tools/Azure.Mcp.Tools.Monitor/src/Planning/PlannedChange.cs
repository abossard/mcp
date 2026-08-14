// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>One input change element resolved against the snapshot.</summary>
internal sealed record PlannedChange(
    int ChangeIndex,
    string? Label,
    string RawKind,
    HealthModelChangeKind? Kind,
    bool Success,
    string? Error,
    IReadOnlyList<PlannedTarget> Targets,
    HealthModelScanCounts Scanned);

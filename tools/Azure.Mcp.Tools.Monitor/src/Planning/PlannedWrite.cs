// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>A single service call the plan intends to make.</summary>
internal sealed record PlannedWrite(
    HealthModelResourceKind Kind,
    string Name,
    PlannedOperation Operation,
    JsonObject? Body);

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>One resource as it currently stands, held as the wire JSON body the service returned.</summary>
internal sealed record HealthModelResourceSnapshot(
    HealthModelResourceKind Kind,
    string Name,
    JsonObject Body);

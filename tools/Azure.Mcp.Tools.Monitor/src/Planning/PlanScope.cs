// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// The (resource group, health model) a group of planned calls executes against. Calls that share a
/// scope also share a single model resolution.
/// </summary>
internal sealed record PlanScope(string ResourceGroup, string HealthModel);

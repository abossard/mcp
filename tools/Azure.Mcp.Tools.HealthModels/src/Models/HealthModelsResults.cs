// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;

namespace Azure.Mcp.Tools.HealthModels.Models;

public sealed record HealthModelsListResult(List<JsonNode> Items, int Count);

public sealed record HealthModelsItemResult(JsonNode Result);

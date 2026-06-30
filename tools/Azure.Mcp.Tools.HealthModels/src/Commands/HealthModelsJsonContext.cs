// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Mcp.Tools.HealthModels.Models;

namespace Azure.Mcp.Tools.HealthModels.Commands;

[JsonSerializable(typeof(HealthModelsItemResult))]
[JsonSerializable(typeof(HealthModelsListResult))]
[JsonSerializable(typeof(JsonNode))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class HealthModelsJsonContext : JsonSerializerContext;

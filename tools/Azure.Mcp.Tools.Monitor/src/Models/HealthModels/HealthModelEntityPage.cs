// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

public sealed record HealthModelEntityPage(
    [property: JsonPropertyName("complete")] bool Complete,
    [property: JsonPropertyName("returnedCount")] int ReturnedCount,
    [property: JsonPropertyName("nextMarker")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextMarker);

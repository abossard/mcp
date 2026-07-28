// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

public sealed record HealthModelQueryPage(
    [property: JsonPropertyName("complete")] bool Complete,
    [property: JsonPropertyName("returnedCount")] int ReturnedCount,
    [property: JsonPropertyName("continuationToken")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContinuationToken);

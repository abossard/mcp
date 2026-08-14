// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>One page of a child collection, with the token that reaches the next one.</summary>
internal sealed record HealthModelResourcePage(
    IReadOnlyList<HealthModelResourceSnapshot> Items,
    string? ContinuationToken);

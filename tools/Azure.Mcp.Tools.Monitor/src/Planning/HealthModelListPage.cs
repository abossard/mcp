// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// One Azure Resource Manager page of a model-scope collection, with the service's own continuation left
/// untouched. Shared by the relationship and signal-definition list calls, which differ only in item type.
/// </summary>
internal sealed record HealthModelListPage<T>(
    IReadOnlyList<T> Items,
    string? ContinuationToken);

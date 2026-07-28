// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.ResourceManager.CloudHealth;

namespace Azure.Mcp.Tools.Monitor.Planning;

internal sealed record HealthModelEntityListPage(
    IReadOnlyList<HealthModelEntityData> Items,
    string? ContinuationToken);

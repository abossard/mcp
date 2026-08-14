// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>The two calls the service offers: a full-body upsert, or a delete by name.</summary>
internal enum PlannedOperation
{
    Put,
    Delete,
}

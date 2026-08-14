// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// What a selector resolved to: the collection it addressed, a rendering of the criteria it used (so a
/// zero-match error can quote the selector back), and the matched resources.
/// </summary>
internal sealed record SelectorMatch(
    HealthModelResourceKind Kind,
    string Description,
    IReadOnlyList<HealthModelResourceSnapshot> Matches);

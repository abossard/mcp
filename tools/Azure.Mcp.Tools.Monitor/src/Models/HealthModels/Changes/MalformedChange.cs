// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// A change element that could not be read into its concrete type, carrying the reason. It occupies its
/// input slot so a malformed element fails on its own result node instead of rejecting the whole batch.
/// </summary>
public sealed class MalformedChange : HealthModelChange
{
    /// <summary>Why this element could not be read, phrased for the caller that sent it.</summary>
    public string Error { get; set; } = string.Empty;

    internal override HealthModelChangeKind ChangeKind =>
        throw new InvalidOperationException("A malformed change is never planned.");
}

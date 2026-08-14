// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// A write an element has already issued and been told succeeded: which element issued it, and the result
/// node that told the caller so. Holding the node is what lets a later element that removes the write say so
/// on the element that made it, instead of leaving a success in the response that is no longer true.
/// </summary>
internal sealed record CommittedWrite(string Element, HealthModelTargetResult Reported)
{
    /// <summary>
    /// Records that a later element took the write away. The wording is deliberately about the resource
    /// rather than the content: a rename carries the body to a new name, so claiming the write was lost
    /// would be wrong, while claiming it is no longer on that resource is true in every case.
    /// </summary>
    internal void Supersede(string cause) =>
        Reported.SupersededBy = $"{cause}, so this element's write is no longer present on that resource.";
}

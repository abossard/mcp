// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Why a resource is off limits to the elements that come after it: which element accounted for it, and
/// whether that element actually issued a write. A blocker that was itself skipped never reached the
/// service, so describing it as having failed to write would name a cause that never happened.
/// </summary>
internal sealed record BlockedWrite(string Element, bool Attempted)
{
    /// <summary>Names the element that blocked a resource, for a later element on that same resource.</summary>
    internal string ByElement(string resource) => Attempted
        ? $"{Element} failed to write {resource}"
        : $"{Element} was not attempted for {resource}";

    /// <summary>Names the dependency that blocked a target, where the element behind it is not the point.</summary>
    internal string ByDependency(PlannedDependency dependency) => Attempted
        ? $"{dependency.Description} failed to write"
        : $"{dependency.Description} was itself not attempted";
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

internal sealed class HealthModelResultShape(IReadOnlySet<HealthModelFieldGroup> fields)
{
    private readonly IReadOnlySet<HealthModelFieldGroup> _fields = fields;

    internal static HealthModelResultShape Compact { get; } = new(new HashSet<HealthModelFieldGroup>());

    internal static HealthModelResultShape From(IEnumerable<HealthModelFieldGroup>? fields)
    {
        if (fields is null)
        {
            return Compact;
        }

        var selected = fields.ToHashSet();
        return selected.Count == 0 ? Compact : new HealthModelResultShape(selected);
    }

    internal bool Includes(HealthModelFieldGroup field) => _fields.Contains(field);
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// RFC 7386 JSON Merge Patch over the resource body's wire JSON, plus the server-owned property guard.
/// </summary>
/// <remarks>
/// The patch is applied to the wire JSON rather than to the typed SDK graph because
/// <c>HealthModelSignalDefinitionProperties</c> is an abstract polymorphic type whose discriminator is not
/// on the base type; round-tripping through the serialized form keeps properties this build has never seen.
/// Arrays are replaced wholesale, which is why the walk never descends into one: an object inside an array
/// is opaque patch data, not a path a caller can address.
/// </remarks>
internal static class HealthModelMergePatch
{
    /// <summary>
    /// Properties the resource provider owns. They are rejected as patch input and excluded from every
    /// diff, so a body that echoes them back never shows as churn the caller did not ask for.
    /// </summary>
    internal static readonly string[] ServerOwnedProperties =
    [
        "provisioningState",
        "healthState",
        "discoveredBy",
        "systemData",
        "id",
        "name",
        "type",
    ];

    private const string PropertiesPrefix = "properties";

    /// <summary>
    /// The deny list is anchored to the two places the resource provider actually owns — the envelope root
    /// and directly under <c>properties</c>. Deeper, these are ordinary caller data: <c>name</c>,
    /// <c>type</c> and <c>id</c> are all legal Azure tag keys, and suppressing them there would reject a
    /// tag the caller owns and hide its change from the what-if.
    /// </summary>
    private static bool IsServerOwned(string prefix, string key) =>
        (prefix.Length == 0 || string.Equals(prefix, PropertiesPrefix, StringComparison.Ordinal)) &&
        ServerOwnedProperties.Contains(key, StringComparer.Ordinal);

    /// <summary>Rejects a patch that names a server-owned property, reporting the offending path.</summary>
    internal static bool TryValidateWritable(JsonNode? patch, out string error)
    {
        error = string.Empty;
        return Walk(patch, string.Empty, ref error);

        static bool Walk(JsonNode? node, string prefix, ref string error)
        {
            if (node is not JsonObject jsonObject)
            {
                return true;
            }

            foreach (var (key, value) in jsonObject)
            {
                var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
                if (IsServerOwned(prefix, key))
                {
                    error = $"'{path}' is set by the service and cannot be written.";
                    return false;
                }

                if (!Walk(value, path, ref error))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Applies <paramref name="patch"/> to <paramref name="target"/>: a null value removes the property, a
    /// nested object merges, and anything else (including an array) replaces. Neither input is mutated.
    /// </summary>
    internal static JsonNode? Apply(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObject)
        {
            return patch?.DeepClone();
        }

        var result = target is JsonObject targetObject
            ? targetObject.DeepClone().AsObject()
            : new JsonObject();

        foreach (var (key, value) in patchObject)
        {
            if (value is null)
            {
                result.Remove(key);
                continue;
            }

            result[key] = Apply(result[key], value);
        }

        return result;
    }

    /// <summary>
    /// Reports every property-level difference between two resource bodies, as dotted paths from the body
    /// root. Server-owned properties are skipped at the paths the service owns them, not by bare key name.
    /// </summary>
    internal static IReadOnlyList<HealthModelPropertyChange> Diff(JsonObject? before, JsonObject? after)
    {
        var changes = new List<HealthModelPropertyChange>();
        DiffObject(before, after, string.Empty, changes);
        return changes;
    }

    private static void DiffObject(
        JsonObject? before, JsonObject? after, string prefix, List<HealthModelPropertyChange> changes)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in before ?? [])
        {
            keys.Add(key);
        }
        foreach (var (key, _) in after ?? [])
        {
            keys.Add(key);
        }

        foreach (var key in keys)
        {
            if (IsServerOwned(prefix, key))
            {
                continue;
            }

            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            var beforeValue = before?[key];
            var afterValue = after?[key];

            if (beforeValue is JsonObject or null && afterValue is JsonObject or null &&
                (beforeValue is not null || afterValue is not null))
            {
                DiffObject(beforeValue as JsonObject, afterValue as JsonObject, path, changes);
                continue;
            }

            if (JsonNode.DeepEquals(beforeValue, afterValue) || ArraysMatch(beforeValue, afterValue))
            {
                continue;
            }

            changes.Add(new HealthModelPropertyChange
            {
                Path = path,
                Before = beforeValue?.DeepClone(),
                After = afterValue?.DeepClone(),
            });
        }
    }

    /// <summary>
    /// Compares two arrays with the same rule <see cref="DiffObject"/> already applies to objects: a
    /// member whose value is JSON null and a member that is absent are the same property state.
    /// </summary>
    /// <remarks>
    /// Arrays are opaque values here — an object inside one is patch data, not an addressable path — so
    /// they are compared, never walked into. Without this rule they are compared by raw
    /// <see cref="JsonNode.DeepEquals(JsonNode?, JsonNode?)"/>, and the SDK bridge materialises explicit
    /// nulls for unset members inside array elements (<c>"signalKind":null</c> in every element of
    /// <c>properties.signalGroups[].signals[]</c>). The caller's array then never equals the stored one,
    /// so every re-apply reports an update and issues a PUT that changes nothing.
    /// A null <em>element</em> is left alone: its position is data, not a property state.
    /// </remarks>
    private static bool ArraysMatch(JsonNode? before, JsonNode? after) =>
        before is JsonArray && after is JsonArray &&
        JsonNode.DeepEquals(WithoutNullMembers(before), WithoutNullMembers(after));

    private static JsonNode? WithoutNullMembers(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                var members = new JsonObject();
                foreach (var (key, value) in jsonObject)
                {
                    if (value is not null)
                    {
                        members[key] = WithoutNullMembers(value);
                    }
                }
                return members;

            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    items.Add(WithoutNullMembers(item));
                }
                return items;

            default:
                return node?.DeepClone();
        }
    }
}

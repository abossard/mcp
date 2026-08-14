// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// The operation a single <see cref="HealthModelChange"/> requests. The union discriminates on the
/// operation only; the resource type it applies to travels inside the element's selector or resource body.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthModelChangeKind>))]
public enum HealthModelChangeKind
{
    /// <summary>Upsert one named resource from a full official-shaped body.</summary>
    Create,

    /// <summary>Apply an RFC 7386 merge patch to every selected resource.</summary>
    Patch,

    /// <summary>Move every selected resource to a new name, repointing the edges that reference it.</summary>
    Rename,

    /// <summary>Remove every selected resource.</summary>
    Delete,
}

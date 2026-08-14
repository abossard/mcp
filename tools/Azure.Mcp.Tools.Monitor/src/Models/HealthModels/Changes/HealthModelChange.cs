// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// The shared shape of every graph-edit element: what to do (<see cref="Kind"/>), which model to do it in,
/// and an echo-only <see cref="Label"/>.
/// </summary>
/// <remarks>
/// Each <see cref="HealthModelChangeKind"/> has its own concrete type carrying only the inputs that
/// operation accepts, so an input that does not apply cannot be expressed. Concrete types declare
/// <c>JsonUnmappedMemberHandling.Disallow</c>, which rejects a misplaced property by name; that attribute
/// is repeated per type because it does not inherit from this base. <see cref="Kind"/> is a declared
/// property rather than a <c>JsonPolymorphic</c> discriminator: the built-in polymorphic reader throws when
/// the discriminator is not the first JSON property, which no generated payload can guarantee.
/// </remarks>
public abstract class HealthModelChange
{
    /// <summary>The operation to perform. Selects which concrete change type applies.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Optional caller-supplied label, echoed verbatim on the result and never used for planning, ordering,
    /// or slot assignment. Duplicate and absent labels are valid.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>The resource group that contains the health model.</summary>
    [JsonPropertyName("resourceGroup")]
    public string ResourceGroup { get; set; } = string.Empty;

    /// <summary>The health model name.</summary>
    [JsonPropertyName("healthModel")]
    public string HealthModel { get; set; } = string.Empty;

    /// <summary>The kind this concrete type implements, used to plan the change.</summary>
    internal abstract HealthModelChangeKind ChangeKind { get; }
}

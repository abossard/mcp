// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

/// <summary>
/// The shared shape of every health-model query: what to run (<see cref="Kind"/>), which model to run it
/// against, and an echo-only <see cref="Label"/>.
/// </summary>
/// <remarks>
/// Each <see cref="HealthModelQueryKind"/> has its own concrete type carrying only the inputs that kind
/// accepts, so an input that does not apply is not merely ignored, it cannot be expressed. Concrete types
/// declare <c>JsonUnmappedMemberHandling.Disallow</c>, which rejects an unknown or misplaced property by
/// name; that attribute is deliberately repeated per type because it does not inherit from this base.
/// <see cref="Kind"/> is a declared property rather than a <c>JsonPolymorphic</c> discriminator: the
/// built-in polymorphic reader throws when the discriminator is not the first JSON property, which no
/// generated payload can guarantee.
/// </remarks>
public abstract class HealthModelQuery
{
    /// <summary>The read operation to perform. Selects which concrete query type applies.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Optional caller-supplied label, echoed verbatim on the result and never used for planning, dedupe,
    /// routing, or slot assignment. Duplicate and absent labels are valid.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>The resource group that contains the health model.</summary>
    [JsonPropertyName("resourceGroup")]
    public string ResourceGroup { get; set; } = string.Empty;

    /// <summary>The health model name.</summary>
    [JsonPropertyName("healthModel")]
    public string HealthModel { get; set; } = string.Empty;

    /// <summary>The kind this concrete type implements, used to plan the query.</summary>
    internal abstract HealthModelQueryKind QueryKind { get; }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>The health-model child collection a change targets.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthModelResourceKind>))]
public enum HealthModelResourceKind
{
    /// <summary>A model entity (<c>.../healthmodels/{model}/entities/{name}</c>).</summary>
    [JsonStringEnumMemberName("entity")]
    Entity,

    /// <summary>A parent/child edge (<c>.../healthmodels/{model}/relationships/{name}</c>).</summary>
    [JsonStringEnumMemberName("relationship")]
    Relationship,

    /// <summary>A model-scope signal definition (<c>.../healthmodels/{model}/signaldefinitions/{name}</c>).</summary>
    [JsonStringEnumMemberName("signalDefinition")]
    SignalDefinition,
}

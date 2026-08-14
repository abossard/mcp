// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

/// <summary>What a change does to one matched target.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthModelChangeAction>))]
public enum HealthModelChangeAction
{
    /// <summary>The target does not exist yet and is written for the first time.</summary>
    [JsonStringEnumMemberName("create")]
    Create,

    /// <summary>The target exists and is written in place with a changed body.</summary>
    [JsonStringEnumMemberName("update")]
    Update,

    /// <summary>The target exists but a create-time-immutable field changed, so it is deleted then written.</summary>
    [JsonStringEnumMemberName("replace")]
    Replace,

    /// <summary>The target is removed.</summary>
    [JsonStringEnumMemberName("delete")]
    Delete,

    /// <summary>The target already matches the desired state, so no write is issued.</summary>
    [JsonStringEnumMemberName("noOp")]
    NoOp,

    /// <summary>The target was not attempted because a target it depends on failed.</summary>
    [JsonStringEnumMemberName("skipped")]
    Skipped,
}

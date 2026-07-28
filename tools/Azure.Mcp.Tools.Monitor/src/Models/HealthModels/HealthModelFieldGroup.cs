// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels;

[JsonConverter(typeof(JsonStringEnumConverter<HealthModelFieldGroup>))]
public enum HealthModelFieldGroup
{
    Identity,
    Audit,
    Signals,
    Layout,
    Context,
    Details,
    Configurations,
    Full,
}

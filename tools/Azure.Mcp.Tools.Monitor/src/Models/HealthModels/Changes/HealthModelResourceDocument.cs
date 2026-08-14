// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

/// <summary>
/// The official resource body, whose only writable member is the <c>properties</c> bag. The envelope is
/// closed so a misspelled key is reported by name, while the bag itself stays open: signal-definition
/// properties are an abstract polymorphic type whose members this build cannot enumerate ahead of time.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HealthModelResourceDocument
{
    [JsonPropertyName("properties")]
    public JsonNode? Properties { get; set; }
}

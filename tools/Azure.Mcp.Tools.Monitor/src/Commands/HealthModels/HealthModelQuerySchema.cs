// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

/// <summary>
/// The JSON Schema for the <c>--queries</c> array, published as the option description so the contract is
/// machine-readable instead of prose. One <c>oneOf</c> branch per kind, each closed with
/// <c>additionalProperties: false</c> and listing exactly the inputs that kind accepts.
/// </summary>
/// <remarks>
/// The option value is a JSON string, so the generated MCP input schema can say no more than
/// <c>"type": "string"</c>; this description is therefore the only contract a client sees. Descriptions are
/// spent only on behavior that the shape cannot express: the silent-empty signal name, the from-now
/// 30-day anchor, and page size being a page rather than a total.
/// </remarks>
internal static class HealthModelQuerySchema
{
    public const string Schema = """
    {
      "type":"array","minItems":1,
      "description":"Read-only Azure Monitor Health Model queries. Input-ordered results, correlated by zero-based queryIndex. Each call returns ONE Azure page; echo page.cursor to continue. Nothing is aggregated or capped silently.",
      "items":{"oneOf":[
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel"],"properties":{"kind":{"const":"entityList"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"asOf":{"$ref":"#/$defs/t"},"whereHealth":{"$ref":"#/$defs/h"},"page":{"$ref":"#/$defs/c"},"select":{"$ref":"#/$defs/es"}}},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","entity"],"properties":{"kind":{"const":"entityGet"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"entity":{"$ref":"#/$defs/s"},"select":{"$ref":"#/$defs/es"}}},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","target"],"properties":{"kind":{"const":"entityHistory"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"target":{"$ref":"#/$defs/tg"},"window":{"$ref":"#/$defs/w"},"page":{"$ref":"#/$defs/p"},"select":{"$ref":"#/$defs/f"}}},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","target","signal"],"properties":{"kind":{"const":"signalHistory"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"signal":{"$ref":"#/$defs/s"},"target":{"$ref":"#/$defs/tg"},"window":{"$ref":"#/$defs/w"},"page":{"$ref":"#/$defs/p"},"select":{"type":"array","items":{"enum":["context","full"]}}},"description":"signal is scoped to the TARGET ENTITY: read it from that entity's own signalGroups via select:[\"signals\"]. An unrecognized name returns an EMPTY history, not an error."},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","target"],"properties":{"kind":{"const":"signalRecommendations"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"target":{"$ref":"#/$defs/tg"},"select":{"type":"array","items":{"enum":["configurations","full"]}}}},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","target"],"properties":{"kind":{"const":"dataAnnotations"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"target":{"$ref":"#/$defs/tg"},"window":{"$ref":"#/$defs/w"},"page":{"$ref":"#/$defs/p"},"select":{"type":"array","items":{"enum":["details","full"]}}}},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel"],"properties":{"kind":{"const":"relationshipList"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"asOf":{"$ref":"#/$defs/t"},"page":{"$ref":"#/$defs/c"},"select":{"$ref":"#/$defs/f"}},"description":"Parent/child edges. Join with an entity's signalGroups.dependencies rule to explain a rollup."},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel"],"properties":{"kind":{"const":"signalDefinitionList"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"asOf":{"$ref":"#/$defs/t"},"page":{"$ref":"#/$defs/c"},"select":{"$ref":"#/$defs/f"}},"description":"Signal definitions and thresholds. Empty when signals are declared inline on entities."}
      ]},
      "$defs":{
        "s":{"type":"string"},
        "t":{"type":"string","format":"date-time","description":"Collection as of this instant."},
        "h":{"enum":["unhealthy","degraded","unknown","notHealthy"],"description":"notHealthy=unhealthy+degraded+unknown; other states excluded."},
        "f":{"type":"array","items":{"enum":["full"]}},
        "es":{"type":"array","items":{"enum":["identity","audit","signals","layout","full"]}},
        "tg":{"type":"object","additionalProperties":false,"description":"Exactly one of entity or whereHealth.","properties":{"entity":{"$ref":"#/$defs/s"},"whereHealth":{"$ref":"#/$defs/h"}}},
        "w":{"type":"object","additionalProperties":false,"description":"from must be within 30 days of the CURRENT time; anchored to now, so a past 'to' does not extend the reach.","properties":{"from":{"$ref":"#/$defs/t"},"to":{"$ref":"#/$defs/t"}}},
        "p":{"type":"object","additionalProperties":false,"description":"size is the Azure page size, not a total limit. cursor cannot be combined with window.","properties":{"size":{"type":"integer"},"cursor":{"$ref":"#/$defs/s"}}},
        "c":{"type":"object","additionalProperties":false,"properties":{"cursor":{"$ref":"#/$defs/s"}}}
      }
    }
    """;
}

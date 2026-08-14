// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

/// <summary>
/// The JSON Schema for the <c>--changes</c> array, published as the option description so the contract is
/// machine-readable instead of prose. One <c>oneOf</c> branch per operation, each closed with
/// <c>additionalProperties: false</c> and listing exactly the inputs that operation accepts.
/// </summary>
/// <remarks>
/// The union discriminates on the operation only; the resource type travels inside a closed
/// <c>select</c>/<c>resource</c> sub-union, which keeps four branches instead of twelve. Descriptions are
/// spent only on behavior the shape cannot express: what-if being the default, the declared affected count
/// an apply must carry, create being a merge with don't-care absence, merge-patch null/array semantics,
/// the depth the server-owned deny list is anchored at, the exhaustive (not paged) selection snapshot,
/// rename expanding into create + repoint + delete, what a FAILED replace leaves behind, when a target
/// is skipped rather than run, which elements a supersession is reported on, and the one condition under
/// which the what-if predicts the apply.
/// </remarks>
internal static class HealthModelGraphEditSchema
{
    public const string Schema = """
    {
      "type":"array","minItems":1,
      "description":"EXPERIMENTAL. Batched selector-targeted edits to one health model's entities, relationships and signal definitions. Input-ordered results, correlated by zero-based changeIndex. Defaults to --mode whatIf, which computes the change set and writes NOTHING; --mode apply additionally REQUIRES --expect {\"affectedCount\":N} echoed from the what-if and fails the WHOLE set with zero writes when it differs. Selectors resolve against a FULLY enumerated snapshot of each collection, not one page. What-if predicts the apply only while every write succeeds: a failure turns later targets into skipped, so affectedCount is an upper bound.",
      "items":{"oneOf":[
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","name","resource"],"properties":{"kind":{"const":"create"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"name":{"$ref":"#/$defs/s"},"resource":{"$ref":"#/$defs/r"}},"description":"Ensure one named resource matches the official PUT body. This is a MERGE, not a full-body replace: a property you OMIT is don't-care and is left as it is, and an explicit null REMOVES it. A body already satisfied is reported as noOp, so re-applying a set is safe."},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","select","patch"],"properties":{"kind":{"const":"patch"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"select":{"$ref":"#/$defs/sel"},"patch":{"$ref":"#/$defs/b"},"allowEmptyMatch":{"type":"boolean"}},"description":"RFC 7386 merge patch: a null value REMOVES the property, a nested object merges, an array REPLACES wholesale. Changing a relationship's parentEntityName or childEntityName is a replace (delete+create) because endpoints are create-time immutable. When a replace's re-create FAILS the tool puts the pre-batch body back and reports it restored; if that restore also fails the error is prefixed RESOURCE LOST: the resource is gone and its previous body survives only in this response. A later element targeting a resource an earlier element failed to write is skipped naming that element; earlier means EXECUTION, not input, order (creates before deletes, by kind). A later write is never blocked, but when one FAILS on a resource an earlier element already wrote, the pre-batch body is NOT put back, because that would discard the earlier write: the resource is left as the failed element left it, and every earlier writer reports supersededBy."},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","select","newName"],"properties":{"kind":{"const":"rename"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"select":{"$ref":"#/$defs/sel"},"newName":{"$ref":"#/$defs/s"},"allowEmptyMatch":{"type":"boolean"}},"description":"A name is a URL path segment, so this expands into create-under-the-new-name, a repoint of every relationship referencing the old name, and delete-of-the-old-name. A failed repoint restores the pre-batch body, or reports RESOURCE LOST: if that fails. If an earlier element wrote that relationship the restore is withheld (RESTORE SKIPPED) and every earlier writer reports supersededBy. A repoint of a relationship an earlier element failed to write is skipped instead, and the delete-of-the-old-name is skipped with it, which leaves the rename half done with both names present."},
        {"type":"object","additionalProperties":false,"required":["kind","resourceGroup","healthModel","select"],"properties":{"kind":{"const":"delete"},"label":{"$ref":"#/$defs/s"},"resourceGroup":{"$ref":"#/$defs/s"},"healthModel":{"$ref":"#/$defs/s"},"select":{"$ref":"#/$defs/sel"},"allowEmptyMatch":{"type":"boolean"}},"description":"Deleting an entity that is still an endpoint of a relationship is REJECTED, never cascaded. A delete of a resource an earlier element wrote reports supersededBy on every such element."}
      ]},
      "$defs":{
        "s":{"type":"string"},
        "a":{"type":"array","items":{"$ref":"#/$defs/s"}},
        "b":{"type":"object","additionalProperties":false,"required":["properties"],"properties":{"properties":{"type":"object"}},"description":"The official body. provisioningState, healthState, discoveredBy, systemData, id, name and type are server-owned ONLY at the body root and directly under properties: naming one THERE REJECTS the element, and none of them ever appear in a diff. Any DEEPER, e.g. properties.tags.name, they are ordinary caller data that is written and diffed."},
        "r":{"type":"object","additionalProperties":false,"minProperties":1,"maxProperties":1,"description":"Exactly one of entity, relationship or signalDefinition.","properties":{"entity":{"$ref":"#/$defs/b"},"relationship":{"$ref":"#/$defs/b"},"signalDefinition":{"$ref":"#/$defs/b"}}},
        "sel":{"type":"object","additionalProperties":false,"minProperties":1,"maxProperties":1,"description":"Exactly one of entity, relationship or signalDefinition. Exact match only; no globs or regexes. Matching NOTHING fails the element unless allowEmptyMatch is true.","properties":{"entity":{"$ref":"#/$defs/es"},"relationship":{"$ref":"#/$defs/rs"},"signalDefinition":{"$ref":"#/$defs/ds"}}},
        "es":{"type":"object","additionalProperties":false,"properties":{"names":{"$ref":"#/$defs/a"},"displayName":{"$ref":"#/$defs/s"},"tags":{"type":"object"},"discoveredBy":{"$ref":"#/$defs/s"},"all":{"type":"boolean"}}},
        "rs":{"type":"object","additionalProperties":false,"properties":{"names":{"$ref":"#/$defs/a"},"parent":{"$ref":"#/$defs/s"},"child":{"$ref":"#/$defs/s"},"discoveredBy":{"$ref":"#/$defs/s"}}},
        "ds":{"type":"object","additionalProperties":false,"properties":{"names":{"$ref":"#/$defs/a"},"signalKind":{"$ref":"#/$defs/s"},"displayName":{"$ref":"#/$defs/s"}}}
      }
    }
    """;
}

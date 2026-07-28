// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.Monitor.Options.HealthModels;

public sealed class HealthModelQueryOptions : ISubscriptionOption
{
    [Option(Description = """
        A JSON array of Azure Monitor Health Model read queries to run in a single batch. Each element is an object:
        {"kind": one of entityList|entityGet|entityHistory|signalHistory|signalRecommendations|dataAnnotations,
        "resourceGroup": string, "healthModel": string, "label"?: optional free-form string echoed verbatim on the result
        (duplicates and omissions allowed; never used for routing), "entityName"?: string (required for entityGet and per-entity
        kinds unless healthFilter is used), "signalName"?: string (required for signalHistory), "startTime"?: ISO-8601,
        "endTime"?: ISO-8601, "top"?: integer, "timestamp"?: ISO-8601 (point-in-time entity list snapshot for entityList),
        "healthFilter"?: unhealthy|degraded|unknown|notHealthy (notHealthy is the closed set unhealthy+degraded+unknown and
        excludes any other state such as deleted; on entityList it keeps only entities in that health state from the page
        already fetched; on a per-entity kind it runs that kind against every entity matching the state instead of one
        entityName; mutually exclusive with entityName and not supported on entityGet, which returns one named entity),
        "fields"?: closed array of the field groups below, "nextMarker"?: opaque per-entity
        continuation for a concrete entityHistory|signalHistory|dataAnnotations query, "continuationToken"?: opaque entity-list
        continuation for entityList or health-filter discovery}. A nextMarker resume must use entityName and cannot include
        startTime/endTime. The marker is passed unchanged. top is the Azure API page size, not a total limit.

        Output is compact by default. Selectable additive groups and rough costs are: identity (entityList/entityGet,
        +233-247 B/item, medium); audit (entityList/entityGet, +75-222 B/item, medium); signals (entityList/entityGet,
        about +207 B/item on a one-signal fixture and collection-scaled, large); layout (entityList/entityGet, +0-62 B/item,
        small); context (signalHistory, about +66 B/item, small); details (dataAnnotations, about +77 B/item,
        large and may be kilobytes); configurations (signalRecommendations, collection-scaled/unmeasured, large); full
        (all kinds, exact CloudHealth SDK JSON: entity payload about +638 B/item, large; history wrapper <100 B/node, small;
        signal context about +66 B/item, small; annotation details about +77 B/item and small typically, but potentially large;
        recommendation configurations unmeasured and collection-scaled, large). Bands: small <100 B/item,
        medium 100-300 B/item, large >300 B/item or collection-scaled. These costs were measured on one recorded fixture
        and one real session; real models vary — treat as order-of-magnitude. Item count can dominate bytes.

        Every successful pageable query/entity node has page.complete and page.returnedCount and exposes the exact API token
        when incomplete. On a health-filtered entityList, returnedCount is the post-filter count while complete and
        continuationToken stay the API's answer for the unfiltered page. Continue with continuationToken or nextMarker until
        every relevant page.complete is true. No caller
        id is needed: queryIndex is the zero-based input position. Results remain input-ordered with per-entity error isolation.
        """)]
    public required string Queries { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }

    [Option(Description = OptionDescriptions.Subscription)]
    public string? Subscription { get; set; }

    [OptionContainer(Prefix = "retry")]
    public RetryPolicyOptions? RetryPolicy { get; set; }
}

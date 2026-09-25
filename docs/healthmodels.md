# Azure Monitor Health Models: queries and graph edits

Use Health Models to inspect a service's health, its dependencies, and the signals behind a state change.

| Resource | What it tells you |
|---|---|
| Health model | The model's identity and configuration |
| Entity | A component's current health, signals, and history |
| Relationship | Which parent depends on which child |
| Signal definition | A reusable signal configuration and its evaluation thresholds |
| Authentication setting / discovery rule | How the model authenticates and discovers resources |

## Choose an interface

| Feature | `query --code` | `query --queries` | `graphedit` | `sdk --code` |
|---|---|---|---|---|
| Best for | Computing an answer from several reads | A known batch of read requests | Reviewing changes before applying them | Existing scripts that mix reads and writes |
| Input | JavaScript | JSON array of typed queries | JSON array of typed changes | JavaScript |
| Reads | All 18 read operations | Eight query kinds | Reads the graph to plan changes | The original 11 read operations |
| Writes | None | None | Create, patch, rename, delete | Immediate resource writes, annotations, health reports |
| Batching | Up to four concurrent reads | Deduplicates matching requests and shares entity discovery | Plans changes and orders dependent writes | Host calls execute one at a time |
| Result | The value your script returns | One result per input, in input order | Target actions, property differences, and errors | The value your script returns |
| Write protection | Read-only API | Read-only API | Preview by default; apply requires the expected affected count | No preview gate or batch rollback |

For a model list or a single model summary, use `healthmodels list` or `healthmodels get`.
`graphedit` and `sdk` are experimental.

**One tool call does not mean one Azure request.** Typed batches save requests through deduplication.
Read-code batches overlap independent requests and let you return a summary instead of whole responses.
`Promise.all` does not make the legacy `sdk` host calls concurrent.

In this guide:

- [Setup](#setup)
- [The same read, two ways](#the-same-read-two-ways)
- [Read-code recipes](#read-code-recipes)
- [The same edit, two ways](#the-same-edit-two-ways)
- [API and input reference](#api-and-input-reference)
- [Results, errors, and limits](#results-errors-and-limits)

## Setup

Use an `azmcp` build containing these commands and an authenticated Azure identity.
For local build instructions, see [CONTRIBUTING.md](../CONTRIBUTING.md).

The commands below use Bash:

```bash
SUBSCRIPTION="your-subscription-id-or-name"
TENANT="your-tenant-id"
COMMON=(--subscription "$SUBSCRIPTION" --tenant "$TENANT")

azmcp monitor healthmodels list "${COMMON[@]}"
azmcp monitor healthmodels get "${COMMON[@]}" \
  --resource-group "example-rg" --health-model "shop"
```

Replace `example-rg`, `shop`, and entity names such as `api` with resources in your subscription.
Pass `--tenant` when you need to select the subscription's tenant; an identity from the wrong tenant can
produce an authorization error even when your intended identity has access.

For MCP calls, use the same option names without `--`: `subscription`, `tenant`, `code`, and so on.
`queries`, `changes`, and `expect` are strings containing JSON. Supply **one non-empty input** to
`query`: either `code` or `queries`, never both.
Each invocation uses the selected subscription and tenant. You can address several resource groups
and models within that scope in one script or typed batch.

## The same read, two ways

Goal: inspect entities, dependency edges, and signal definitions in one invocation.

| | Read-code mode | Typed batch mode |
|---|---|---|
| Fetching | Three promises, with overlapping I/O | Three typed requests planned by the tool |
| Processing | Count, filter, and join inside the script | Process the returned query results in your caller |
| Pagination | Each collection returns `nextLink` | Each pageable result reports `page.complete` and `page.cursor` |
| Choose it when | You want a computed answer | You want schema-checked requests and separate result slots |

### Read-only JavaScript

```bash
azmcp monitor healthmodels query "${COMMON[@]}" --code '
const rg = "example-rg", model = "shop";
const [entities, relationships, definitions] = await Promise.all([
  client.entities.listByHealthModel(rg, model),
  client.relationships.listByHealthModel(rg, model),
  client.signalDefinitions.listByHealthModel(rg, model)
]);
return {
  counts: {
    entities: entities.value.length,
    relationships: relationships.value.length,
    signalDefinitions: definitions.value.length
  },
  next: {
    entities: entities.nextLink,
    relationships: relationships.nextLink,
    signalDefinitions: definitions.nextLink
  }
};
'
```

The counts cover the returned pages. A non-null value in `next` means that collection has more data.
The summary appears under `.results.result`; `.results.azureCalls` counts the three successful reads.

Illustrative `.results.result` for three complete collections:

```json
{
  "counts": { "entities": 3, "relationships": 2, "signalDefinitions": 1 },
  "next": { "entities": null, "relationships": null, "signalDefinitions": null }
}
```

### Typed batch JSON

```bash
azmcp monitor healthmodels query "${COMMON[@]}" --queries '[
  {"kind":"entityList","resourceGroup":"example-rg","healthModel":"shop","label":"entities"},
  {"kind":"relationshipList","resourceGroup":"example-rg","healthModel":"shop","label":"dependencies"},
  {"kind":"signalDefinitionList","resourceGroup":"example-rg","healthModel":"shop","label":"signals"}
]' | jq '.results[] | {queryIndex, label, kind, success, page}'
```

Results have `queryIndex` values `0`, `1`, and `2`. Labels are optional, may repeat, and do not affect
deduplication. Each query returns its own data, rather than a combined summary.

The planner can reuse the same entity-list request for several health-filtered queries. It still needs
a separate history request for each distinct entity. Neither read mode follows all pages for you.

## Read-code recipes

For the JavaScript recipes below, save the code as `query.js` and run:

```bash
azmcp monitor healthmodels query "${COMMON[@]}" --code "$(cat query.js)"
```

The shell reads the file. JavaScript runs inside the .NET Jint interpreter, including in the NativeAOT
server; it does not need Node.js. Use `return` to choose what reaches the caller and `console.log` for
diagnostics.

### Discover unhealthy components, then read their history

This performs one discovery read, then starts history reads for matching entities. It returns transition
counts and continuation markers instead of every history record.

```javascript
const rg = "example-rg", model = "shop";
const entities = await client.entities.listByHealthModel(rg, model);
const states = new Set(["Unhealthy", "Degraded", "Unknown"]);
const selected = entities.value.filter(e => states.has(e.properties.healthState));

const histories = await Promise.all(selected.map(async entity => {
  const history = await client.entities.getHistory(rg, model, entity.name, { top: 10 });
  return {
    entity: entity.name,
    state: entity.properties.healthState,
    returnedTransitions: history.history.length,
    nextMarker: history.nextMarker ?? null
  };
}));

return { entityPageCursor: entities.nextLink, histories };
```

The runtime admits at most four reads at once. `Promise.all` preserves the input order, even when Azure
finishes requests in a different order. These counts describe the returned history pages, not total
history or uptime.

The typed equivalent uses `target.whereHealth`:

```bash
azmcp monitor healthmodels query "${COMMON[@]}" --queries '[
  {
    "kind":"entityHistory",
    "resourceGroup":"example-rg",
    "healthModel":"shop",
    "target":{"whereHealth":"notHealthy"},
    "page":{"size":10}
  }
]'
```

`notHealthy` means `Unhealthy`, `Degraded`, or `Unknown`. It excludes other states, including `Deleted`.
When a typed query targets a health filter, its outer page describes entity discovery; each returned
entity's page describes that entity's history. Use the outer cursor with the same filtered query to
discover more entities. To continue one entity's history, submit a query with `target.entity` and that
entity node's `page.cursor`.

### Keep successful reads when another read fails

Use `Promise.allSettled` when a missing or inaccessible entity should not discard the other results.

```javascript
const rg = "example-rg", model = "shop";
const names = ["api", "database", "missing-entity"];
const outcomes = await Promise.allSettled(
  names.map(name => client.entities.get(rg, model, name))
);

return outcomes.map((outcome, index) =>
  outcome.status === "fulfilled"
    ? { entity: names[index], health: outcome.value.properties.healthState }
    : {
        entity: names[index],
        error: {
          operation: outcome.reason.operation,
          status: outcome.reason.status,
          code: outcome.reason.code,
          message: outcome.reason.message
        }
      }
);
```

`Promise.all` rejects when a read fails. An uncaught rejection produces `.results.error`.
`Promise.allSettled` lets you decide how to report each failure. Azure errors carry the HTTP status and
service code; local validation errors have a null HTTP status.

### Count entities across several pages

Each list call returns one page. This example reads up to five pages, counts states, and returns a cursor
if more pages remain. The five-page bound belongs to this example, not the tool.

```javascript
const rg = "example-rg", model = "shop";
let cursor = null; // To resume, set this to the previous result's nextCursor.
let pages = 0, entities = 0;
const byState = {};

do {
  const page = await client.entities.listByHealthModel(
    rg, model, cursor ? { cursor } : undefined
  );
  for (const entity of page.value) {
    const state = entity.properties.healthState ?? "Unspecified";
    byState[state] = (byState[state] ?? 0) + 1;
  }
  entities += page.value.length;
  cursor = page.nextLink;
  pages++;
} while (cursor && pages < 5);

return { pages, entities, byState, complete: !cursor, nextCursor: cursor };
```

To resume a **history** read, use the response's `nextMarker` in the next request body:

```javascript
const rg = "example-rg", model = "shop", entity = "api";
const first = await client.entities.getHistory(rg, model, entity, { top: 1 });
const second = first.nextMarker
  ? await client.entities.getHistory(rg, model, entity, {
      top: 1, nextMarker: first.nextMarker
    })
  : null;
return { first, second };
```

Keep markers unchanged. Do not combine a history marker with `startTime` or `endTime`, or a list cursor
with `asOf`. The read-code interface rejects list continuations outside the configured ARM endpoint,
collection, subscription, model, or API version.

### Inspect a dependency rollup

Read the root's rule and the states of the children referenced by the returned relationship page.
This shows the inputs to the rollup; it does not reimplement Azure's health calculation.

```javascript
const rg = "example-rg", model = "shop", rootName = "shop";
const [root, relationships] = await Promise.all([
  client.entities.get(rg, model, rootName),
  client.relationships.listByHealthModel(rg, model)
]);
const edges = relationships.value.filter(r => r.properties.parentEntityName === rootName);
const children = await Promise.all(edges.map(async edge => {
  const child = await client.entities.get(rg, model, edge.properties.childEntityName);
  return { name: child.name, health: child.properties.healthState };
}));
return {
  root: root.name,
  health: root.properties.healthState,
  rule: root.properties.signalGroups?.dependencies ?? null,
  children,
  relationshipPageCursor: relationships.nextLink
};
```

### Inspect discovery and authentication configuration

These reads are available in `query --code`, not in the typed query kinds or the legacy SDK facade.

```javascript
const rg = "example-rg", model = "shop";
const [authentication, discovery] = await Promise.all([
  client.authenticationSettings.listByHealthModel(rg, model),
  client.discoveryRules.listByHealthModel(rg, model)
]);
const rule = discovery.value.length
  ? await client.discoveryRules.get(rg, model, discovery.value[0].name)
  : null;
return {
  authentication: authentication.value,
  firstDiscoveryRule: rule,
  next: { authentication: authentication.nextLink, discovery: discovery.nextLink }
};
```

An empty discovery-rule collection is valid. Authentication-setting reads return configuration such as
managed-identity references, not the credential used by the tool.

## The same edit, two ways

Goal: change entity `api`'s display name to `Checkout API`.

| | `graphedit` | `sdk --code` |
|---|---|---|
| First invocation | Shows the proposed change; writes nothing | Applies the write as the script runs |
| Approval | Reuse the preview's affected count and optional snapshot | Caller must approve the script before execution |
| Input | A typed merge patch and exact selector | Read the entity, edit its properties, write it back |
| Failure | Inspect target actions, errors, and any recovery outcome | Earlier successful writes remain applied |

**The apply command and SDK script below modify Azure resources. Use a test model.**

### Preview and apply with `graphedit`

```bash
CHANGES='[
  {
    "kind":"patch",
    "resourceGroup":"example-rg",
    "healthModel":"shop",
    "select":{"entity":{"names":["api"]}},
    "patch":{"properties":{"displayName":"Checkout API"}}
  }
]'

PREVIEW=$(azmcp monitor healthmodels graphedit "${COMMON[@]}" --changes "$CHANGES")
printf '%s\n' "$PREVIEW" | jq '.results | {success, affectedCount, snapshot, changes}'
```

Review the actions and property differences. The default mode is `whatIf`, so this has not written
anything. After approving the preview, apply the **same** `CHANGES`:

```bash
EXPECT=$(printf '%s\n' "$PREVIEW" |
  jq -ce '.results | select(.success == true) | {affectedCount, snapshot}') &&
azmcp monitor healthmodels graphedit "${COMMON[@]}" \
  --mode apply --expect "$EXPECT" --changes "$CHANGES"
```

A missing or different `affectedCount` prevents writes. Including `snapshot` also rejects a changed
selection snapshot. These guards do not make the batch a transaction or guarantee that every write
will succeed.

### Apply immediately with `sdk --code`

Save this as `update.js`:

```javascript
const rg = "example-rg", model = "shop", name = "api";
const entity = await client.entities.get(rg, model, name);
entity.properties.displayName = "Checkout API";
await client.entities.createOrUpdate(rg, model, name, { properties: entity.properties });
const updated = await client.entities.get(rg, model, name);
return { name: updated.name, displayName: updated.properties.displayName };
```

```bash
azmcp monitor healthmodels sdk "${COMMON[@]}" --code "$(cat update.js)"
```

The bridge strips server-owned fields from resource-write bodies. Preserve the writable properties you
need when calling `createOrUpdate`; it sends a resource PUT, not the graph editor's sparse merge.
`createOrUpdate` returns no resource value, so read the resource again when you need confirmation.

The SDK facade also exposes `createOrUpdate` and `delete` on relationships and signal definitions, plus
`entities.delete`, `entities.addDataAnnotation`, and `entities.ingestHealthReport`. The two action
payloads pass through as supplied. There is no preview, affected-count guard, or batch rollback.

### Create an entity and its dependency edge

Pass this array as `graphedit --changes`. The tool previews both changes and orders the entity before
the relationship. Reuse the preview/apply procedure above to apply them.

```json
[
  {
    "kind":"create",
    "resourceGroup":"example-rg",
    "healthModel":"shop",
    "name":"database",
    "resource":{"entity":{"properties":{"displayName":"Orders database","impact":"Standard"}}}
  },
  {
    "kind":"create",
    "resourceGroup":"example-rg",
    "healthModel":"shop",
    "name":"api-to-database",
    "resource":{"relationship":{"properties":{"parentEntityName":"api","childEntityName":"database"}}}
  }
]
```

Here `api` must already exist. Entity names need at least three characters; `db` is too short.
`impact` accepts `Standard`, `Limited`, or `Suppressed`. It is not a health state such as `Degraded`.

## API and input reference

### Read operations side by side

`rg`, `model`, and the resource-name arguments are strings. `options` and `body` are optional except
the signal-history body, which must name a signal.

| Method on `client` in `query --code` | Typed `--queries` kind | Also in `sdk --code` |
|---|---|---|
| `healthModels.get(rg, model)` | Use the separate `healthmodels get` command | Yes |
| `healthModels.listByResourceGroup(rg, options?)` | Use the separate `healthmodels list` command | Yes |
| `healthModels.listBySubscription(options?)` | Use the separate `healthmodels list` command | Yes |
| `entities.get(rg, model, entity)` | `entityGet` | Yes |
| `entities.listByHealthModel(rg, model, options?)` | `entityList` | Yes |
| `entities.getHistory(rg, model, entity, body?)` | `entityHistory` | Yes |
| `entities.getSignalHistory(rg, model, entity, body)` | `signalHistory` | Yes |
| `entities.getSignalRecommendations(rg, model, entity)` | `signalRecommendations` | Yes |
| `entities.getDataAnnotations(rg, model, entity, body?)` | `dataAnnotations` | Yes |
| `relationships.get(rg, model, relationship)` | Not available | No |
| `relationships.listByHealthModel(rg, model, options?)` | `relationshipList` | Yes |
| `signalDefinitions.get(rg, model, signalDefinition)` | Not available | No |
| `signalDefinitions.listByHealthModel(rg, model, options?)` | `signalDefinitionList` | Yes |
| `authenticationSettings.get(rg, model, authenticationSetting)` | Not available | No |
| `authenticationSettings.listByHealthModel(rg, model, options?)` | Not available | No |
| `discoveryRules.get(rg, model, discoveryRule)` | Not available | No |
| `discoveryRules.listByHealthModel(rg, model, options?)` | Not available | No |
| `operations.list()` | Not available | No |

The separate `list` and `get` commands return model summaries, not the full SDK payload returned by code.
History, recommendations, signal history, and annotations use read-only POST operations underneath.
HTTP POST does not imply a write for these four APIs.

### Read-code options and response fields

| Calls | Accepted options |
|---|---|
| Health-model lists; authentication-setting list | `{ cursor }` |
| Entity, relationship, signal-definition, discovery-rule lists | `{ asOf }` or `{ cursor }`, not both |
| Entity history and data annotations | `{ startTime, endTime, top, nextMarker }`; marker excludes time bounds |
| Signal history | The history fields plus required `signalName` |
| Provider operations | No options or cursor argument |

List pages expose `value` and `nextLink`. History and annotation responses expose `nextMarker`.
`top` limits a history page, not the total number of records or calls.

| Data | Fields to use |
|---|---|
| Entity state | `entity.properties.healthState` |
| Dependency edge | `relationship.properties.parentEntityName` and `childEntityName` |
| Signal thresholds | `definition.properties.evaluationRules` |
| Entity transitions | `history[]`: `previousState`, `newState`, `occurredAt`, `reason` |
| Signal evaluations | `history[]`: `occurredAt`, `healthState`, optional `additionalContext` and `value` |
| Recommendations | `recommendedSignals` and `recommendedConfigurations` when present |
| Annotations | `annotations[]`: `annotationId`, `annotationDetails`, `description`, `createdAt` |

Use a signal **instance name from the target entity**, not an arbitrary name from the model-wide
definition catalog. Inspect that entity's `properties.signalGroups`: most instances appear in a group's
`signals[]`; the Resource Health signal uses `azureResource.resourceHealth.signalName`.
An unknown signal name can return an empty history without an error. A missing numeric `value` is not
a zero reading.

### Typed query reference

Each query includes `kind`, `resourceGroup`, `healthModel`, and optionally `label`.
Unknown or misplaced fields fail that query's result slot. A malformed or empty batch fails the command.

| Kind | Required beyond scope | Optional fields |
|---|---|---|
| `entityList` | None | `asOf`, `whereHealth`, `page.cursor`, `select` |
| `entityGet` | `entity` | `select` |
| `entityHistory` | `target` | `window`, `page`, `select` |
| `signalHistory` | `target`, `signal` | `window`, `page`, `select` |
| `signalRecommendations` | `target` | `select` |
| `dataAnnotations` | `target` | `window`, `page`, `select` |
| `relationshipList` | None | `asOf`, `page.cursor`, `select` |
| `signalDefinitionList` | None | `asOf`, `page.cursor`, `select` |

| Input | Shape and meaning |
|---|---|
| Named target | `"target":{"entity":"api"}` |
| Health-filtered target | `"target":{"whereHealth":"notHealthy"}`; choose one target form |
| Health filters | `unhealthy`, `degraded`, `unknown`, `notHealthy` |
| Time window | `"window":{"from":"UTC timestamp","to":"UTC timestamp"}` |
| History/annotation page | `"page":{"size":10,"cursor":"returned cursor"}` |
| Collection page | `"page":{"cursor":"returned cursor"}`; no page-size option |

Do not combine a typed history cursor with `window`. The service's history lookback is anchored to the
current time: `from` must be within the last 30 days, even when `to` is in the past.

Typed results use compact projections by default. Add only the field groups you need:

| `select` | Available on |
|---|---|
| `identity`, `audit`, `signals`, `layout` | `entityList`, `entityGet` |
| `context` | `signalHistory` |
| `details` | `dataAnnotations` |
| `configurations` | `signalRecommendations` |
| `full` | Every kind; includes the complete SDK payload |

For example, use `"select":["signals"]` on `entityGet` to discover signal instances. Use
`"select":["full"]` on `signalDefinitionList` when you need fields such as metric names or query text.
An empty definition collection can mean the model uses inline entity signals.

### Graph edit reference

Each change includes `kind`, `resourceGroup`, `healthModel`, and optionally `label`.

| Kind | Required fields | Effect |
|---|---|---|
| `create` | `name`, `resource` | Ensure the supplied properties exist; omitted properties stay unchanged |
| `patch` | `select`, `patch` | Merge the supplied properties into each matching resource |
| `rename` | `select`, `newName` | Create the new name, update referencing relationships, delete the old name |
| `delete` | `select` | Delete selected resources; reject entities that still have relationships |

`resource` names one of `entity`, `relationship`, or `signalDefinition`, each with a `properties` body.
`select` also names exactly one resource kind:

| Selector | Exact-match fields |
|---|---|
| `entity` | `names`, `displayName`, `tags`, `discoveredBy`, or `all:true` |
| `relationship` | `names`, `parent`, `child`, `discoveredBy` |
| `signalDefinition` | `names`, `signalKind`, `displayName` |

Selectors inspect the full collection, not one page. They accept no wildcard or regular-expression
patterns. No match fails the change unless `allowEmptyMatch:true`.

For both create and patch, nested objects merge, an explicit null removes a property, and an array
replaces the existing array. The graph editor rejects server-owned fields at the body root and directly
under `properties`: `id`, `name`, `type`, `systemData`, `provisioningState`, `healthState`, `discoveredBy`.
The same names deeper in caller-owned data remain valid.

Writes run in dependency order: signal definitions, entities, relationships; deletes reverse that order.
A failed dependency causes later affected targets to report `skipped`. Execution order can differ from
input order, while results keep their `changeIndex`.

Changing a relationship endpoint requires delete-and-create (`replace`). On failure, the editor tries
to restore the pre-batch body unless doing so would discard an earlier successful write:

| Result | Meaning |
|---|---|
| Restored | The editor restored the prior body after the replacement failed |
| `RESOURCE LOST:` | Replacement and restoration failed; preserve the response containing the prior body |
| `RESTORE SKIPPED:` | Restoration would discard an earlier write; the resource remains deleted |
| `supersededBy` | A later action removed or superseded an earlier write to the same resource |

Inspect every target. A matching preview count is an upper bound on the apply's writes, not a promise of
all-or-nothing success.

## Results, errors, and limits

The outer command response contains `status`, `message`, `results`, and `duration`.

| Interface | Where to read the answer | Where to check failures |
|---|---|---|
| `query --code` | `.results.result` | `.results.error`; caught per-read errors are whatever your script returns |
| `query --queries` | `.results[]`, correlated by `queryIndex` | Query `success`, then each entity node's `success` and `error` |
| `graphedit` | `.results.changes[]`, correlated by `changeIndex` | Overall and per-target success, actions, errors, and recovery messages |
| `sdk --code` | `.results.result` | `.results.error`; earlier successful writes may already be applied |

**Outer status 200 does not prove that every operation succeeded.** A typed query can succeed while one
entity node fails. A script can report an uncaught error inside `.results.error`.

Code responses also include `logs` and `azureCalls`. The count measures successful logical API calls,
not every HTTP request, transport retry, or failed attempt. In SDK mode it can include writes.

| Read-code limit | Behavior |
|---|---|
| Four active reads | Additional reads wait for a slot |
| No separate call-count cap | The execution limits still apply |
| 30-second script deadline | Covers queued and in-flight script reads; subscription setup happens before it |
| 5,000,000 statements; recursion depth 64 | Stops scripts that exceed either execution limit |
| 24,000 result characters | Sets `truncated:true` and returns truncated text with a marker and size estimate |
| Request cancellation | Cancels active reads, stops queued dispatch, and drains work before returning |

Aggregate or project inside the script to keep the result small. Await every read whose result you need;
the runtime cancels outstanding reads when the script finishes.

The read-code sandbox exposes only the listed client methods and JavaScript. It has no arbitrary
network, filesystem, module-loading, CLR, or credential access. This is an in-process interpreter,
not a separate process with a hard memory boundary. The existing mixed SDK mode also uses Jint, but its
synchronous host calls do not gain concurrency from promises.

### Common mistakes

| Symptom | Check |
|---|---|
| `query` rejects the inputs | Supply only one of `--code` and `--queries`, with a non-empty value |
| Typed query rejects `entityName`, `healthFilter`, or `top` | Use `entity` / `target`, `whereHealth`, and `page.size` as shown above |
| A list or history looks incomplete | Follow the returned `nextLink`, `nextMarker`, or typed `page.cursor` |
| Signal history is empty | Confirm the instance name belongs to that entity and the window has data |
| Signal recommendations fail with `EntityHasNoAzureResource` | The entity needs an Azure resource association |
| A graph edit refuses to apply | Use the same changes and the affected count from a successful preview |
| An SDK script fails after some writes | Read its logs and call count, then inspect Azure; there is no batch rollback |

The tool publishes its JavaScript declaration or JSON Schema in each option's description. The current
contracts live in [HealthModelReadDeclaration.cs](../tools/Azure.Mcp.Tools.Monitor/src/Sandbox/HealthModelReadDeclaration.cs),
[HealthModelQuerySchema.cs](../tools/Azure.Mcp.Tools.Monitor/src/Commands/HealthModels/HealthModelQuerySchema.cs),
and [HealthModelGraphEditSchema.cs](../tools/Azure.Mcp.Tools.Monitor/src/Commands/HealthModels/HealthModelGraphEditSchema.cs).

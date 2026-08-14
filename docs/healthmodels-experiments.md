# Three experiments on the Azure Monitor Health Models MCP surface

# GOAL
How can we query and or modify the health model more efficient than with many little tool calls?

# METHOD

I've generated 3 different tools that show ways to do complex and combined queries with minimizing tools calls and giving flexibility.

| Tool | Position | Contract | Writes |
|---|---|---|---|
| `healthmodels query` | Typed batch of reads | 5.9 KB JSON Schema, 8 closed kinds | none |
| `healthmodels graphedit` | Typed batch of writes with a preview gate | 7.4 KB JSON Schema, 4 change kinds | gated |
| `healthmodels sdk` | Code mode: caller writes JavaScript | 9.6 KB TypeScript declaration | ungated |

## Shared background

A health model is a graph. Entities carry health state, relationships express dependency, signal
definitions carry the thresholds that produce state.

A root entity rolls up its children by a rule such as `WorstOf`. Answering "why is this model unhealthy" means joining three collections and walking edges, so getting one answer might cost many calls. Each of the three tools reduces that call count a different way.

All three take `--subscription` and share one ARM credential. Pass `--tenant` when your default credential
resolves a tenant other than the one owning the subscription. Skip it and you get `AuthorizationFailed` on
`Microsoft.Resources/subscriptions/read`, which blames permissions for a tenant problem.

---

## 1. `healthmodels query`: a typed batch of reads

One call carries any number of read requests, each its own closed type. Results come back in input order,
correlated by a system-assigned `queryIndex`. The eight kinds are `entityList`, `entityGet`,
`entityHistory`, `signalHistory`, `signalRecommendations`, `dataAnnotations`, `relationshipList` and
`signalDefinitionList`.

Every kind carries exactly the inputs it accepts, so the parser rejects a field belonging to another kind
by name. That turns a class of mistakes into a parse error instead of a validator. The `--queries` option
description is the JSON Schema, so a client reads the contract rather than prose about it.

### Reading three collections in one call

```bash
azmcp monitor healthmodels query --subscription <sub> --queries '[
  {"kind":"entityList",           "resourceGroup":"rg-alz-healthmodels","healthModel":"alz-platform-healthmodel"},
  {"kind":"relationshipList",     "resourceGroup":"rg-alz-healthmodels","healthModel":"alz-platform-healthmodel"},
  {"kind":"signalDefinitionList", "resourceGroup":"rg-alz-healthmodels","healthModel":"alz-platform-healthmodel"}
]'
```

The full response is 57 KB. Its per-query envelope:

```console
$ azmcp monitor healthmodels query ... | jq '[.results[] | {queryIndex, kind, success, page}]'
[
  {
    "queryIndex": 0,
    "kind": "entityList",
    "success": true,
    "page": {
      "complete": false,
      "returnedCount": 50,
      "cursor": "https://management.azure.com/subscriptions/b2af20ad-98fa-4aa7-94c3-059663641d9f/resourceGroups/rg-alz-healthmodels/providers/Microsoft.CloudHealth/healthmodels/alz-platform-healthmodel/entities?api-version=2026-05-01-preview&%24skiptoken=OTU3Mjk3ODAtYWQwZC1hMTJmLTYyODItMTQ3YzdlNDA3ZmYy"
    }
  },
  {
    "queryIndex": 1,
    "kind": "relationshipList",
    "success": true,
    "page": {
      "complete": false,
      "returnedCount": 50,
      "cursor": "https://management.azure.com/subscriptions/b2af20ad-98fa-4aa7-94c3-059663641d9f/resourceGroups/rg-alz-healthmodels/providers/Microsoft.CloudHealth/healthmodels/alz-platform-healthmodel/relationships?api-version=2026-05-01-preview&%24skiptoken=OTEyZjM0MTEtZGMyZi03MGE5LWUzMTctMGFhYzIzZTc0NWMy"
    }
  },
  {
    "queryIndex": 2,
    "kind": "signalDefinitionList",
    "success": true,
    "page": {
      "complete": true,
      "returnedCount": 32
    }
  }
]
```

Each result reports `page.complete`, `page.returnedCount`, and the exact cursor to resume from. The tool
returns one Azure page per request and never stitches pages together, so a caller can tell a full answer
from a partial one.

The entities themselves:

```console
$ ... | jq '.results[0].entities[:2]'
[
  {
    "entityName": "01b219c5-f1b7-21a7-94a8-ee460d79b222",
    "success": true,
    "entity": {
      "properties": {
        "displayName": "ag-aria-health",
        "impact": "Standard",
        "discoveredBy": "discover-management",
        "healthState": "Unknown"
      }
    }
  },
  {
    "entityName": "02c62f21-eab5-731b-f928-0bd4f8526f18",
    "success": true,
    "entity": {
      "properties": {
        "displayName": "t1fd4b6f341294ebfmon",
        "impact": "Standard",
        "discoveredBy": "discover-management",
        "healthState": "Healthy"
      }
    }
  }
]
```

### A failure lands on its own slot

```bash
azmcp monitor healthmodels query --subscription <sub> --queries '[
  {"kind":"entityGet","resourceGroup":"rg-alz-healthmodels","healthModel":"alz-platform-healthmodel","entity":"no-such-entity"},
  {"kind":"signalDefinitionList","resourceGroup":"rg-alz-healthmodels","healthModel":"alz-platform-healthmodel"}
]'
```

```console
$ azmcp monitor healthmodels query ... | jq '.results[0]'
{
  "queryIndex": 0,
  "kind": "entityGet",
  "success": true,
  "entities": [
    {
      "entityName": "no-such-entity",
      "success": false,
      "error": "Failed to read resource '/subscriptions/b2af20ad-98fa-4aa7-94c3-059663641d9f/resourceGroups/rg-alz-healthmodels/providers/Microsoft.CloudHealth/healthmodels/alz-platform-healthmodel/entities/no-such-entity' with error: Not Found (HTTP 404, ResourceReadFailed)"
    }
  ]
}
```

The failure lands on the entity node, inside a query that itself reports `success: true`. Its sibling ran
regardless:

```console
$ ... | jq '.results[1] | {queryIndex, kind, success, page}'
{
  "queryIndex": 1,
  "kind": "signalDefinitionList",
  "success": true,
  "page": {
    "complete": true,
    "returnedCount": 32
  }
}
```

Only a batch-level problem, such as unparseable JSON or an empty array, fails the whole call.

Reach for this when you know the shape of the question ahead of time and want the contract checked before
anything runs.

---

## 2. `healthmodels graphedit`: typed writes behind a preview gate

The write-side sibling of `query`, with four change kinds: `create` (full-body upsert), `patch` (RFC 7386
merge patch, where null removes a property and an array replaces wholesale), `rename` and `delete`.
Selectors match target names exactly, against a snapshot the tool enumerates in full. The schema rejects
globs and regexes.

The default mode computes the whole change set and writes nothing, so a caller has to read a preview before
it can write.

### Preview: what would change, per property

```bash
azmcp monitor healthmodels graphedit --subscription <sub> --changes '[
  {"kind":"patch","resourceGroup":"ahm-kpi-reporting-rg","healthModel":"sd-test",
   "select":{"entity":{"all":true}},
   "patch":{"properties":{"impact":"Limited"}}}
]'
```

```console
$ azmcp monitor healthmodels graphedit ... | jq '.results'
{
  "mode": "whatIf",
  "success": true,
  "affectedCount": 3,
  "snapshot": "0a584b94d26742169ad435e06b008e9ca38366a402b321e6c6cbebbdda557fd9",
  "changes": [
    {
      "changeIndex": 0,
      "kind": "patch",
      "success": true,
      "targets": [
        {
          "name": "5d362dd3-ab72-42c0-b69b-50435ab5294f",
          "resourceKind": "entity",
          "action": "update",
          "changes": [
            {
              "path": "properties.impact",
              "before": "Standard",
              "after": "Limited"
            }
          ],
          "success": true
        },
        {
          "name": "a174e46c-c94a-4be2-aa19-cd94f7e27e10",
          "resourceKind": "entity",
          "action": "update",
          "changes": [
            {
              "path": "properties.impact",
              "before": "Standard",
              "after": "Limited"
            }
          ],
          "success": true
        },
        {
          "name": "sd-test",
          "resourceKind": "entity",
          "action": "update",
          "changes": [
            {
              "path": "properties.impact",
              "before": "Standard",
              "after": "Limited"
            }
          ],
          "success": true
        }
      ],
      "scanned": {
        "entities": 3,
        "relationships": 2,
        "signalDefinitions": 2
      }
    }
  ]
}
```

Each target reports an action (`create`, `update`, `replace`, `delete`, `noOp` or `skipped`) and a
per-property diff. The tool issued zero writes.

### The gate: applying requires echoing the count you saw

Writing needs `--mode apply` plus an `--expect` whose `affectedCount` matches. Here I sent the wrong
number:

```bash
azmcp monitor healthmodels graphedit --subscription <sub> \
  --mode apply --expect '{"affectedCount":99}' --changes '[ ...same patch... ]'
```

```console
$ azmcp monitor healthmodels graphedit --mode apply --expect '{"affectedCount":99}' ... \
    | jq '.results | {mode, success, error, affectedCount, snapshot}'
{
  "mode": "apply",
  "success": false,
  "error": "expect.affectedCount is 99 but the change set affects 3 target(s). No writes were issued.",
  "affectedCount": 3,
  "snapshot": "0a584b94d26742169ad435e06b008e9ca38366a402b321e6c6cbebbdda557fd9"
}
```

The change set still computes and still reports its three targets under `.results.changes`; the guard stops
the writes, not the planning. Reading `impact` back afterwards:

```console
$ azmcp monitor healthmodels sdk --code "...listByHealthModel..." \
    | jq -r '.results.result[]'
5d362dd3-ab72-42c0-b69b-50435ab5294f impact=Standard
a174e46c-c94a-4be2-aa19-cd94f7e27e10 impact=Standard
sd-test impact=Standard
```

The optional `snapshot` token covers the other race. Echo it back with the batch and the tool refuses the
whole thing if any matched target changed in between.

The executor writes signal definitions first, then entities, then
relationships, and reverses that order for deletes; a target whose dependency failed reports `skipped`
rather than attempting the write. A rename expands into create-under-new-name, repoint every referring
relationship, delete the old. The tool rejects the seven server-owned properties as input
(`provisioningState`, `healthState`, `discoveredBy`, `systemData`, `id`, `name`, `type`) and leaves them
out of every diff.

Reach for this when someone should approve a specific change set before it touches Azure.

---

## 3. `healthmodels sdk`: code mode

One tool runs the caller's JavaScript in a sandbox holding an authenticated `client`, shaped like
`@azure/arm-cloudhealth`'s `CloudHealthClient`. The `--code` option description is the TypeScript
declaration of that surface.

Cloudflare's [Code Mode](https://blog.cloudflare.com/code-mode/) and Anthropic's
[code execution with MCP](https://www.anthropic.com/engineering/code-execution-with-mcp) both argue that a
model writes better JavaScript than it writes tool calls, and that intermediate results should stay out of
the context window. This tool tests that claim against one Azure service.

```bash
azmcp monitor healthmodels sdk --subscription <sub> --code "
let cursor = null, all = [], pages = 0;
do {
  const p = await client.entities.listByHealthModel('rg-alz-healthmodels','alz-platform-healthmodel', cursor ? { cursor } : undefined);
  all.push(...p.value); cursor = p.nextLink; pages++;
} while (cursor && pages < 5);

const byState = {};
for (const e of all) { const s = e.properties.healthState || 'none'; byState[s] = (byState[s]||0)+1; }
console.log('pages', pages, 'entities', all.length);
return { pages, entities: all.length, byState };
"
```

```console
$ azmcp monitor healthmodels sdk ... | jq '.'
{
  "status": 200,
  "message": "Success",
  "results": {
    "result": {
      "pages": 2,
      "entities": 90,
      "byState": {
        "Unknown": 53,
        "Healthy": 37
      }
    },
    "logs": [
      "pages 2 entities 90"
    ],
    "azureCalls": 2
  },
  "duration": 5737
}
```

The script fetched 90 entities, counted them and threw them away inside the sandbox. Only the tally came
back. A separate run over 20 entity histories pulled 102,418 characters from Azure and returned 66.

### When something fails

```console
$ azmcp monitor healthmodels sdk ... | jq '.results'
{
  "result": null,
  "logs": [
    "entities read: 50"
  ],
  "error": "The Resource 'Microsoft.CloudHealth/healthmodels/no-such-model' under resource group 'rg-alz-healthmodels' was not found. For more details please go to https://aka.ms/ARMResourceNotFoundFix (HTTP 404, ResourceNotFound)",
  "azureCalls": 1
}
```

The read that landed before the failure survives in `logs` and `azureCalls`. This tool does not gate
writes, so `azureCalls` is how you learn that three of five writes already applied.

### The sandbox

Jint with CLR interop off. I ran each of these against the shipped binary:

```
typeof fetch / require / process             -> "undefined,undefined,undefined"
System.IO.File.ReadAllText('/etc/passwd')    -> ReferenceError: System is not defined
globals matching /credential|token|armclient/i -> 0
while (true) {}                              -> "Script exceeded the 5000000 statement limit."
```

A script reaches nothing beyond the client: no network, no filesystem, no module loading, no .NET type.
The CloudHealth functions are C# closures that hold the credential, and the script holds only handles to
them, so it has no name to reference the token by.

Three limits bound a run: a 30-second wall clock, a five-million statement ceiling, and a recursion cap.
Host calls resolve one at a time, so a script can call `Promise.all` but gains nothing from it. Budget
roughly 25 sequential Azure calls per run.

Full walkthrough with more examples: [`healthmodels-code-mode.md`](./healthmodels-code-mode.md).

Reach for this when the answer needs computing rather than fetching: chaining, filtering, loops,
aggregation, or anything spanning several calls.

---

## What comparing them showed

The three contracts measure 5.9, 7.4 and 9.6 KB. The two schemas spend most of that on repetition: each of
the eight query branches restates `kind`, `resourceGroup` and `healthModel`, and adding a ninth kind adds a
ninth copy. Code mode's 9.6 KB is mostly type definitions, and adding an operation costs one line. The
schemas get something back for the repetition. Sending `asOf` on an `entityGet`, where it does not belong,
fails that query by name before any call goes out:

```
queryIndex=0 kind=entityGet success=false
error: queries[0] (entityGet) is invalid. The JSON property 'asOf' could not be mapped to any .NET member
       contained in type '...Queries.EntityGetQuery'.
```

In code mode the same misplaced argument reaches Azure, or gets ignored.

Building the demo model for the code-mode walkthrough, ARM rejected two of my writes: an entity named `db`,
which needs three characters, and `impact: 'Degraded'`, where the legal set is `Standard`, `Limited` and
`Suppressed`. `query` and `graphedit` refuse both before a call goes out. Code mode learned them from the
service and now carries both in its declaration.

Wrong response shapes cost more than wrong request shapes. A script read `point.value` on 4,000
signal-history points and got null for all of them with no error, because the declaration named
`SignalHistoryResponse` and never defined it. The real points carry `occurredAt`, `healthState` and
`additionalContext`, and no numeric field. A rejected write costs a retry; that one nearly went into this
documentation as a finding. All nine undefined types now have shapes.

A preview gate is worth less than it looks. `graphedit` refuses to write without a matching
`affectedCount`, which bounds how much a wrong change set can touch. Adding the same gate to `sdk` would
not have caught either payload error above: a client-side preview can only replay what the script intended
to send, and `impact: 'Degraded'` looks valid until ARM sees it. The CloudHealth SDK exposes no
validate-only call, so there is nothing to check a preview against.

## Status

Exploration. The source records every gap below:

- `sdk` writes with no gate and no rollback, which the tool's owner chose. A script that fails midway leaves its earlier writes applied.
- `sdk` runs on Jint, whose reflection-based interop trips NativeAOT trim analysis. Scoped `TrimmerSingleWarn`, link-attributes XML and a feature-switch substitution take 46 errors down to 2; those two are `IL2026` warnings demoted at the ILC step alone.
- `graphedit` carries EXPERIMENTAL in its own tool description.
- Only `query` has recorded live tests.

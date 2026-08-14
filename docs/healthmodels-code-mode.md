# Code mode for Azure Monitor Health Models

`azmcp monitor healthmodels sdk` runs JavaScript you write against the authenticated CloudHealth SDK. You get one tool instead of a dozen, and the script does the chaining, filtering and aggregation before anything reaches the model's context.

I ran every example against a live subscription and pasted the output from the terminal, trimming it only
where a stack trace or a repeated array element added nothing.

The approach follows Cloudflare's [Code Mode](https://blog.cloudflare.com/code-mode/) and Anthropic's [code execution with MCP](https://www.anthropic.com/engineering/code-execution-with-mcp): convert the tool surface into an API the model already knows how to write against, then return only the answer.

## The API

The sandbox hands your script a `client` shaped like `@azure/arm-cloudhealth`'s `CloudHealthClient`, backed by the .NET `Azure.ResourceManager.CloudHealth` package:

```javascript
client.healthModels      // get, listByResourceGroup, listBySubscription
client.entities          // get, listByHealthModel, getHistory, getSignalHistory,
                         // getSignalRecommendations, getDataAnnotations,
                         // addDataAnnotation, ingestHealthReport, createOrUpdate, delete
client.relationships     // listByHealthModel, createOrUpdate, delete
client.signalDefinitions // listByHealthModel, createOrUpdate, delete
```

Method names and argument order match the npm SDK, so `client.entities.get(rg, model, entity)` behaves the way the published `.d.ts` says. The service returns its own JSON, which is why entity health reads as `entity.properties.healthState`.

Run a script with:

```bash
azmcp monitor healthmodels sdk --subscription <sub> --code "<javascript>"
```

## 1. Build a model

Five entities and four dependency edges, in one call. The root carries a `WorstOf` dependency rollup over its children.

```javascript
const rg = 'rg-codemode-demo', hm = 'hm-shop';

const tiers = [
  { name: 'shop',          display: 'Shop (root)',   impact: 'Standard', x: 400, y: 100 },
  { name: 'web',           display: 'Web frontend',  impact: 'Standard', x: 200, y: 300 },
  { name: 'api',           display: 'Checkout API',  impact: 'Standard', x: 400, y: 300 },
  { name: 'orders-db',     display: 'Orders DB',     impact: 'Standard', x: 600, y: 300 },
  { name: 'session-cache', display: 'Session cache', impact: 'Limited',  x: 800, y: 300 },
];

for (const t of tiers) {
  const props = { displayName: t.display, impact: t.impact, canvasPosition: { x: t.x, y: t.y } };
  if (t.name === 'shop') props.signalGroups = { dependencies: { aggregationType: 'WorstOf', ignoreUnknown: true } };
  await client.entities.createOrUpdate(rg, hm, t.name, { properties: props });
}
console.log('entities created:', tiers.length);

for (const child of ['web', 'api', 'orders-db', 'session-cache']) {
  await client.relationships.createOrUpdate(rg, hm, `shop-to-${child}`, {
    properties: { displayName: `Shop depends on ${child}`, parentEntityName: 'shop', childEntityName: child }
  });
}
console.log('relationships created: 4');

const page = await client.entities.listByHealthModel(rg, hm);
const rels = await client.relationships.listByHealthModel(rg, hm);
return {
  entities: page.value.length,
  relationships: rels.value.length,
  names: page.value.map(e => e.name).sort(),
  edges: rels.value.map(r => `${r.properties.parentEntityName}->${r.properties.childEntityName}`).sort()
};
```

```json
{
  "status": 200,
  "results": {
    "result": {
      "entities": 6,
      "relationships": 4,
      "names": ["api", "hm-shop", "orders-db", "session-cache", "shop", "web"],
      "edges": ["shop->api", "shop->orders-db", "shop->session-cache", "shop->web"]
    },
    "logs": ["entities created: 5", "relationships created: 4"]
  },
  "duration_ms": 12534
}
```

Nine writes and two reads, one invocation. The service creates that sixth entity, `hm-shop`, alongside the model itself.

### When the service rejects a payload

The first version of that script used `db` as an entity name and `Degraded` as an impact value. Both came back as ARM validation errors with the service's own text:

```json
{
  "status": 400,
  "error": "String does not match pattern ^[a-zA-Z0-9][a-zA-Z0-9-]{1,258}[a-zA-Z0-9]$: db. Paths in payload: '.path.entityName'"
}
```

```json
{
  "status": 400,
  "error": "'Properties.Impact': Invalid value for enum Impact: 'Degraded'. Valid values are: Standard, Limited, Suppressed."
}
```

Entity names need three characters. `Impact` takes `Standard`, `Limited` or `Suppressed`. Both facts now
appear in the `--code` declaration, so a caller can read them instead of learning them from a rejection.

A failure inside a script stays inside the script's result. The response keeps its `{result, logs, error}`
shape, so whatever the script logged before the failure survives, and `azureCalls` reports how many calls
completed:

```json
{
  "result": null,
  "logs": ["entities read: 50"],
  "error": "The Resource 'Microsoft.CloudHealth/healthmodels/no-such-model' under resource group 'rg-alz-healthmodels' was not found. For more details please go to https://aka.ms/ARMResourceNotFoundFix (HTTP 404, ResourceNotFound)",
  "azureCalls": 1
}
```

The error keeps the service's own sentence and adds the two facts you act on, the HTTP status and the
service error code. It drops the status line, body echo, header dump and interpreter stack. The read that
succeeded before the failure is still visible in both `logs` and `azureCalls`.

Writes apply as the script runs. A failure partway leaves earlier writes in place: the first run created
`shop`, `web` and `api` before ARM rejected `db`. `azureCalls` is how you find out that three writes landed.
There is no `whatIf` here, unlike `azmcp monitor healthmodels graphedit`, and no rollback.

## 2. Add signals

Three signal definitions, each with degraded and unhealthy thresholds.

```javascript
const rg = 'rg-codemode-demo', hm = 'hm-shop';

const signals = [
  { name: 'cpu-high',     display: 'CPU utilisation', metric: 'Percentage CPU',    ns: 'microsoft.compute/virtualmachines', agg: 'Average', unit: 'Percent',      degraded: 75,  unhealthy: 90,   op: 'GreaterThan' },
  { name: 'availability', display: 'Availability',    metric: 'Availability',      ns: 'microsoft.storage/storageaccounts', agg: 'Average', unit: 'Percent',      degraded: 99,  unhealthy: 95,   op: 'LessThan' },
  { name: 'latency-p95',  display: 'Latency p95',     metric: 'SuccessE2ELatency', ns: 'microsoft.storage/storageaccounts', agg: 'Average', unit: 'MilliSeconds', degraded: 500, unhealthy: 2000, op: 'GreaterThan' },
];

for (const s of signals) {
  await client.signalDefinitions.createOrUpdate(rg, hm, s.name, {
    properties: {
      displayName: s.display, signalKind: 'AzureResourceMetric',
      metricNamespace: s.ns, metricName: s.metric,
      aggregationType: s.agg, dataUnit: s.unit,
      timeGrain: 'PT5M', refreshInterval: 'PT5M',
      evaluationRules: {
        degradedRule:  { operator: s.op, threshold: s.degraded },
        unhealthyRule: { operator: s.op, threshold: s.unhealthy }
      }
    }
  });
  console.log('defined', s.name, s.op, 'deg=' + s.degraded, 'unh=' + s.unhealthy);
}

const defs = await client.signalDefinitions.listByHealthModel(rg, hm);
return {
  count: defs.value.length,
  thresholds: defs.value.map(d => ({
    name: d.name, metric: d.properties.metricName,
    degraded: d.properties.evaluationRules.degradedRule.threshold,
    unhealthy: d.properties.evaluationRules.unhealthyRule.threshold,
    operator: d.properties.evaluationRules.degradedRule.operator
  })).sort((a, b) => a.name.localeCompare(b.name))
};
```

```json
{
  "status": 200,
  "results": {
    "result": {
      "count": 3,
      "thresholds": [
        { "name": "availability", "metric": "Availability",      "degraded": 99,  "unhealthy": 95,   "operator": "LessThan" },
        { "name": "cpu-high",     "metric": "Percentage CPU",    "degraded": 75,  "unhealthy": 90,   "operator": "GreaterThan" },
        { "name": "latency-p95",  "metric": "SuccessE2ELatency", "degraded": 500, "unhealthy": 2000, "operator": "GreaterThan" }
      ]
    },
    "logs": [
      "defined cpu-high GreaterThan deg=75 unh=90",
      "defined availability LessThan deg=99 unh=95",
      "defined latency-p95 GreaterThan deg=500 unh=2000"
    ]
  },
  "duration_ms": 6043
}
```

## 3. Modify what you read

Retune every `GreaterThan` threshold 20% tighter and leave the rest alone. The decision about which definitions qualify happens in the loop, so the caller never has to fetch the list, decide, and send a second batch.

```javascript
const rg = 'rg-codemode-demo', hm = 'hm-shop';
const defs = await client.signalDefinitions.listByHealthModel(rg, hm);
const changes = [];

for (const d of defs.value) {
  const rules = d.properties.evaluationRules;
  if (rules.degradedRule.operator !== 'GreaterThan') {
    changes.push({ name: d.name, action: 'skipped', why: 'not a GreaterThan rule' });
    continue;
  }

  const before = { deg: rules.degradedRule.threshold, unh: rules.unhealthyRule.threshold };
  const after  = { deg: Math.round(before.deg * 0.8), unh: Math.round(before.unh * 0.8) };

  const body = JSON.parse(JSON.stringify(d.properties));
  body.evaluationRules.degradedRule.threshold  = after.deg;
  body.evaluationRules.unhealthyRule.threshold = after.unh;

  await client.signalDefinitions.createOrUpdate(rg, hm, d.name, { properties: body });
  changes.push({ name: d.name, action: 'retuned', before, after });
}

const after = await client.signalDefinitions.listByHealthModel(rg, hm);
return {
  changes,
  verified: after.value
    .map(d => ({ name: d.name, deg: d.properties.evaluationRules.degradedRule.threshold, unh: d.properties.evaluationRules.unhealthyRule.threshold }))
    .sort((a, b) => a.name.localeCompare(b.name))
};
```

```json
{
  "status": 200,
  "results": {
    "result": {
      "changes": [
        { "name": "availability", "action": "skipped", "why": "not a GreaterThan rule" },
        { "name": "cpu-high",     "action": "retuned", "before": { "deg": 75,  "unh": 90   }, "after": { "deg": 60,  "unh": 72   } },
        { "name": "latency-p95",  "action": "retuned", "before": { "deg": 500, "unh": 2000 }, "after": { "deg": 400, "unh": 1600 } }
      ],
      "verified": [
        { "name": "availability", "deg": 99,  "unh": 95 },
        { "name": "cpu-high",     "deg": 60,  "unh": 72 },
        { "name": "latency-p95",  "deg": 400, "unh": 1600 }
      ]
    }
  },
  "duration_ms": 5474
}
```

The body comes back carrying `provisioningState`, which the write endpoint rejects. The bridge strips the
seven properties the resource provider owns (`id`, `name`, `type`, `systemData`, `provisioningState`,
`healthState`, `discoveredBy`) before a resource write, so a script can read a resource, edit it and write
it back without deleting them by hand. Only `createOrUpdate` strips. `ingestHealthReport` and
`addDataAnnotation` send what you wrote, because a health report exists to carry a `healthState` and
stripping by name would delete the field the call is for. The bridge anchors that stripping to the envelope root and to `properties`. A `name`
deeper in the body, such as a signal's own name, stays caller data and survives. That final re-read proves the
change landed.

## 4. Walk the topology and explain a rollup

A `WorstOf` rollup says the parent inherits its worst child's state. Answering "why is the root Unknown" means joining entities to relationships and ranking the children.

```javascript
const rg = 'rg-codemode-demo', hm = 'hm-shop';
const [ents, rels] = [await client.entities.listByHealthModel(rg, hm),
                      await client.relationships.listByHealthModel(rg, hm)];

const byName = new Map(ents.value.map(e => [e.name, e]));
const children = new Map();
for (const r of rels.value) {
  const p = r.properties.parentEntityName, c = r.properties.childEntityName;
  if (!children.has(p)) children.set(p, []);
  children.get(p).push(c);
}

const hasParent = new Set(rels.value.map(r => r.properties.childEntityName));
const roots = ents.value.filter(e => !hasParent.has(e.name) && children.has(e.name)).map(e => e.name);

const lines = [];
function walk(name, depth) {
  const e = byName.get(name);
  lines.push('  '.repeat(depth) + (depth ? '└─ ' : '') + name + ' [' + (e.properties.healthState || 'none') + ']');
  for (const c of (children.get(name) || []).sort()) walk(c, depth + 1);
}
roots.forEach(r => walk(r, 0));

const root = byName.get(roots[0]);
const rule = root.properties.signalGroups && root.properties.signalGroups.dependencies;
const kids = (children.get(roots[0]) || []).map(n => ({ name: n, state: byName.get(n).properties.healthState || 'none' }));
const rank = { Unhealthy: 3, Degraded: 2, Unknown: 1, Healthy: 0, none: 0 };
const worst = kids.slice().sort((a, b) => (rank[b.state] || 0) - (rank[a.state] || 0))[0];

return {
  topology: lines,
  rollup: {
    root: roots[0], rootState: root.properties.healthState || 'none',
    aggregation: rule ? rule.aggregationType : '(none)',
    ignoreUnknown: rule ? rule.ignoreUnknown : null,
    children: kids, decidedBy: worst ? worst.name + ' (' + worst.state + ')' : null
  }
};
```

```json
{
  "status": 200,
  "results": {
    "result": {
      "topology": [
        "shop [Unknown]",
        "  └─ api [Unknown]",
        "  └─ orders-db [Unknown]",
        "  └─ session-cache [Unknown]",
        "  └─ web [Unknown]"
      ],
      "rollup": {
        "root": "shop",
        "rootState": "Unknown",
        "aggregation": "WorstOf",
        "ignoreUnknown": true,
        "children": [
          { "name": "api",           "state": "Unknown" },
          { "name": "orders-db",     "state": "Unknown" },
          { "name": "session-cache", "state": "Unknown" },
          { "name": "web",           "state": "Unknown" }
        ],
        "decidedBy": "api (Unknown)"
      }
    }
  },
  "duration_ms": 3915
}
```

Everything reads `Unknown` because these entities carry no signals yet. Two Azure calls produced both the tree and the ranked children.

## 5. Timeline analytics across a fleet

This runs against a populated model. It pages through every entity, pulls each one's transition history, and computes time-weighted state occupancy — how long each entity actually spent in each state, not how many events it logged.

```javascript
const rg = 'rg-alz-healthmodels', hm = 'alz-platform-healthmodel';
const WINDOW_DAYS = 14;
const now = Date.now(), from = new Date(now - WINDOW_DAYS * 86400000).toISOString();

let cursor = null, entities = [], pages = 0;
do {
  const p = await client.entities.listByHealthModel(rg, hm, cursor ? { cursor } : undefined);
  entities.push(...p.value); cursor = p.nextLink; pages++;
} while (cursor && pages < 10);

const SCAN_LIMIT = 25;
const scanned = entities.slice(0, SCAN_LIMIT);

let apiCalls = pages, withHistory = 0, transitions = 0;
const perEntity = [];

for (const e of scanned) {
  const h = await client.entities.getHistory(rg, hm, e.name, { startTime: from }); apiCalls++;
  const evs = (h.history || []).slice().sort((a, b) => new Date(a.occurredAt) - new Date(b.occurredAt));
  if (!evs.length) continue;
  withHistory++; transitions += evs.length;

  let cur = evs[0].previousState, t0 = new Date(evs[0].occurredAt).getTime();
  const dwell = {};
  for (const ev of evs) {
    const t = new Date(ev.occurredAt).getTime();
    dwell[cur] = (dwell[cur] || 0) + (t - t0);
    cur = ev.newState; t0 = t;
  }
  dwell[cur] = (dwell[cur] || 0) + (now - t0);

  const total = Object.values(dwell).reduce((a, b) => a + b, 0) || 1;
  const bad = (dwell.Unhealthy || 0) + (dwell.Degraded || 0);

  perEntity.push({
    name: e.properties.displayName || e.name,
    transitions: evs.length,
    uptimePct: +(100 * (1 - bad / total)).toFixed(2),
    unknownPct: +(100 * ((dwell.Unknown || 0) / total)).toFixed(2),
    lastChange: evs[evs.length - 1].occurredAt,
    lastReason: evs[evs.length - 1].reason
  });
}

perEntity.sort((a, b) => b.transitions - a.transitions || a.uptimePct - b.uptimePct);
const avg = a => a.length ? +(a.reduce((x, y) => x + y, 0) / a.length).toFixed(2) : null;

return {
  windowDays: WINDOW_DAYS, azureCalls: apiCalls,
  entitiesDiscovered: entities.length, entitiesScanned: scanned.length,
  entitiesWithHistory: withHistory, totalTransitions: transitions,
  fleetAvgUptimePct: avg(perEntity.map(e => e.uptimePct)),
  fleetAvgUnknownPct: avg(perEntity.map(e => e.unknownPct)),
  flappiest: perEntity.slice(0, 5)
};
```

```json
{
  "status": 200,
  "results": {
    "result": {
      "windowDays": 14,
      "azureCalls": 27,
      "entitiesDiscovered": 91,
      "entitiesScanned": 25,
      "entitiesWithHistory": 9,
      "totalTransitions": 58,
      "fleetAvgUptimePct": 99.92,
      "fleetAvgUnknownPct": 4.22,
      "flappiest": [
        { "name": "workspacemvqxdyu4fwdai", "transitions": 28, "uptimePct": 100,   "unknownPct": 28.02, "lastChange": "2026-08-12T03:53:10.2281384Z", "lastReason": "SignalTransition" },
        { "name": "stahm4dobgr3v2x2hq",     "transitions": 11, "uptimePct": 99.54, "unknownPct": 0.04,  "lastChange": "2026-08-12T03:40:36.0663864Z", "lastReason": "SignalTransition" },
        { "name": "t1fd4b6f341294ebfmon",   "transitions": 7,  "uptimePct": 99.91, "unknownPct": 0,     "lastChange": "2026-07-31T14:40:49.8341762Z", "lastReason": "SignalTransition" },
        { "name": "t1fd4b6f341294ebf",      "transitions": 6,  "uptimePct": 100,   "unknownPct": 9.89,  "lastChange": "2026-08-12T00:27:56.7956221Z", "lastReason": "SignalTransition" },
        { "name": "myaliase2ee2etestkv",    "transitions": 2,  "uptimePct": 99.8,  "unknownPct": 0,     "lastChange": "2026-08-07T12:01:00.2140756Z", "lastReason": "SignalTransition" }
      ]
    }
  },
  "duration_ms": 13209
}
```

The top entity shows 100% uptime and 28 transitions. Both are true: it never went Unhealthy or Degraded, it flapped between Healthy and Unknown. Counting events alone would have flagged it as the worst performer; weighting by time shows it never actually broke.

That `SCAN_LIMIT` exists because the first version scanned all 91 entities and hit the wall clock:

```json
{ "status": 200, "error": "Script exceeded the 30s execution limit." }
```

Host calls resolve one at a time, so `Promise.all` is accepted but does not run them concurrently. Budget roughly 25 to 30 sequential Azure calls per script and page across invocations for anything larger.

## 6. Attribute churn to a signal

Find the noisiest entity, bucket its transitions by hour, then look at what its signals were doing.

```javascript
const rg = 'rg-alz-healthmodels', hm = 'alz-platform-healthmodel';
const from = new Date(Date.now() - 7 * 86400000).toISOString();

const page = await client.entities.listByHealthModel(rg, hm);
let worst = null, calls = 1;
for (const e of page.value.slice(0, 20)) {
  const h = await client.entities.getHistory(rg, hm, e.name, { startTime: from }); calls++;
  const n = (h.history || []).length;
  if (!worst || n > worst.n) worst = { name: e.name, display: e.properties.displayName, n, history: h.history || [] };
}

const byHour = {};
for (const ev of worst.history) {
  const hr = new Date(ev.occurredAt).getUTCHours();
  byHour[hr] = (byHour[hr] || 0) + 1;
}

const detail = await client.entities.get(rg, hm, worst.name); calls++;
const signalNames = [];
for (const g of Object.values(detail.properties.signalGroups || {})) {
  for (const s of (g.azureResourceMetricSignals || g.signals || [])) signalNames.push(s.signalDefinitionName || s.name);
}

const culprits = [];
for (const sig of signalNames.slice(0, 5)) {
  const sh = await client.entities.getSignalHistory(rg, hm, worst.name, { signalName: sig, startTime: from }); calls++;
  const pts = sh.history || [];
  const states = {};
  let flips = 0, prev = null;
  for (const p of pts) {
    states[p.healthState] = (states[p.healthState] || 0) + 1;
    if (prev !== null && p.healthState !== prev) flips++;
    prev = p.healthState;
  }
  culprits.push({ signal: sig.slice(0, 8), points: pts.length, distinctStates: Object.keys(states), stateCounts: states, flips, pagedOut: !!sh.nextMarker });
}
culprits.sort((a, b) => b.flips - a.flips);

return {
  azureCalls: calls, entity: worst.display, transitionsIn7d: worst.n,
  statesSeen: [...new Set(worst.history.map(h => h.newState))],
  transitionsByHourUTC: byHour, signalsAttached: signalNames.length,
  topCulprits: culprits.slice(0, 3)
};
```

```json
{
  "status": 200,
  "results": {
    "result": {
      "azureCalls": 27,
      "entity": "workspacemvqxdyu4fwdai",
      "transitionsIn7d": 14,
      "statesSeen": ["Unknown", "Healthy"],
      "transitionsByHourUTC": { "3": 7, "10": 7 },
      "signalsAttached": 12,
      "topCulprits": [
        { "signal": "796bb813", "points": 1000, "distinctStates": ["Unknown"], "stateCounts": { "Unknown": 1000 }, "flips": 0, "pagedOut": true },
        { "signal": "416cd5fd", "points": 1000, "distinctStates": ["Unknown"], "stateCounts": { "Unknown": 1000 }, "flips": 0, "pagedOut": true },
        { "signal": "4a8cd04d", "points": 1000, "distinctStates": ["Unknown"], "stateCounts": { "Unknown": 1000 }, "flips": 0, "pagedOut": true }
      ]
    }
  },
  "duration_ms": 13759
}
```

Every transition lands at 03:00 or 10:00 UTC, seven each, nothing in the other 22 hours. That shape points at a schedule rather than a fault. The five signal histories carry 5,000 data points between them and reduce to three rows; `pagedOut: true` says each hit the service's page size, so a longer window needs the `nextMarker` echoed back.

An earlier version of this script read `p.value` on each point and got nulls back. Signal history points carry `occurredAt`, `healthState` and `additionalContext`, and no numeric field:

```json
{ "occurredAt": "2026-08-12T07:17:41.0749121Z", "healthState": "Unknown", "additionalContext": "" }
```

## 7. Audit a model for drift

Code expresses a governance rule more directly than a query language does.

```javascript
const rg = 'rg-codemode-demo', hm = 'hm-shop';
const [ents, rels] = [await client.entities.listByHealthModel(rg, hm),
                      await client.relationships.listByHealthModel(rg, hm)];
const linked = new Set(rels.value.flatMap(r => [r.properties.parentEntityName, r.properties.childEntityName]));

const findings = [];
for (const e of ents.value) {
  const p = e.properties;
  if (!linked.has(e.name))                 findings.push({ entity: e.name, issue: 'orphan',              detail: 'no relationship references it' });
  if (!p.displayName)                      findings.push({ entity: e.name, issue: 'no-display-name',     detail: 'falls back to resource name in the portal' });
  if (!p.canvasPosition)                   findings.push({ entity: e.name, issue: 'no-canvas-position',  detail: 'renders at origin' });
  if (p.impact && p.impact !== 'Standard') findings.push({ entity: e.name, issue: 'non-standard-impact', detail: 'impact=' + p.impact });
}

const byIssue = {};
for (const f of findings) byIssue[f.issue] = (byIssue[f.issue] || 0) + 1;
return { entitiesAudited: ents.value.length, findingCount: findings.length, byIssue, findings };
```

```json
{
  "status": 200,
  "results": {
    "result": {
      "entitiesAudited": 6,
      "findingCount": 2,
      "byIssue": { "orphan": 1, "non-standard-impact": 1 },
      "findings": [
        { "entity": "hm-shop",       "issue": "orphan",              "detail": "no relationship references it" },
        { "entity": "session-cache", "issue": "non-standard-impact", "detail": "impact=Limited" }
      ]
    }
  },
  "duration_ms": 4876
}
```

Both findings are real. `hm-shop` is the entity the service created with the model, sitting outside the graph built in section 1, and `session-cache` is the one entity given `Limited` impact.

## What it costs

The analytics in section 5 fetched a 91-entity page plus 20 histories, then returned a summary. Measured inside the script:

```javascript
let rawChars = 0, calls = 0;
const page = await client.entities.listByHealthModel(rg, hm); calls++;
rawChars += JSON.stringify(page).length;

for (const e of page.value.slice(0, 20)) {
  const h = await client.entities.getHistory(rg, hm, e.name, { startTime: from }); calls++;
  rawChars += JSON.stringify(h).length;
}

const summary = { calls, entitiesScanned: 20, withHistory, transitions };
return { ...summary, rawCharsFetched: rawChars, returnedChars: JSON.stringify(summary).length, ... };
```

```json
{
  "status": 200,
  "results": {
    "result": {
      "calls": 21,
      "entitiesScanned": 20,
      "withHistory": 9,
      "transitions": 58,
      "rawCharsFetched": 102418,
      "rawTokensApprox": 25605,
      "returnedChars": 66,
      "returnedTokensApprox": 17,
      "reductionFactor": 1551.8,
      "savedPct": 99.94
    }
  },
  "duration_ms": 10233
}
```

102 KB fetched, 66 characters returned. A single entity page on this model measures 94,109 characters on its own, around 23,500 tokens, which is what a plain list call would put into the context before any analysis started.

The tool caps results at roughly 6,000 tokens. Past that it truncates the response and reports the original estimated size, telling you to aggregate more inside the script.

## The sandbox

Scripts run in a Jint interpreter with CLR interop turned off. I checked each of these against the shipped binary:

```
typeof fetch / require / process             -> "undefined,undefined,undefined"
System.IO.File.ReadAllText('/etc/passwd')    -> ReferenceError: System is not defined
globals matching /credential|token|armclient/i -> 0
while (true) {}                              -> "Script exceeded the 5000000 statement limit."
```

No network, no filesystem, no module loading, no route to a .NET type. The CloudHealth functions are C# closures that hold the credential; the script holds function handles and can never name the token. This is the capability model from Cloudflare's post, where the sandbox reaches the service without holding the key.

Three limits bound a run: a 30-second wall clock, a five-million statement ceiling, and a recursion cap. For
a tight loop the statement ceiling trips first, in under four seconds. Exceeding any of them returns an
error plus whatever the script logged before it stopped and the `azureCalls` count, so you can see how far a
partial run got. Host calls resolve one at a time, so `Promise.all` is accepted but does not run them
concurrently; budget roughly 25 sequential Azure calls per script. All of this is stated in the `--code`
declaration, alongside the type of every request and response the client returns.

## Choosing between the health-model tools

| Tool | Use it for |
|---|---|
| `healthmodels list` / `get` | Naming a model or reading its top-level state |
| `healthmodels query` | A typed batch of reads with a JSON Schema contract and per-query error isolation |
| `healthmodels graphedit` | Writes needing a `whatIf` preview and an affected-count guard |
| `healthmodels sdk` | Chaining, filtering, loops, aggregation, and analysis that spans several calls |

Reach for `query` when you know the shape of the request ahead of time and want the contract checked. Reach for `sdk` when the answer needs computing rather than fetching.

## Reproducing this

I deployed an empty health model with Bicep and built everything else through sections 1 to 3. Sections 5 and 6 read a model that already carried signal data in the same subscription. Clean up with:

```bash
az group delete -n rg-codemode-demo --yes --no-wait
```

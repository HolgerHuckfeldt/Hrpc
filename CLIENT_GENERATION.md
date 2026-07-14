# Client Generation — Stub Generators from Discovery

> Operationalizes the roadmap items in [`ROADMAP.md`](ROADMAP.md) → *v1.1 Versioning &
> build-time contract* and *Later → Discovery → TypeScript codegen*. Status: Increment 1
> in progress. As of: 2026-07-14.

## Why

Trame's code-first promise — *the C# classes are the contract, no IDL, no `.proto`* — is a
**server-side** advantage. The .NET client gets a typed fluent builder (`TrameCall` /
`ITrameClient`), but every **non-.NET client** (a browser TypeScript frontend, a Node service,
a Python tool) gets the raw wire shape to hand-roll: `params` arrays of `{ parameterName, data }`,
`dependencyMapping`, the camelCase-vs-PascalCase casing contract, the `@alias` chaining rules.
There are no generated, typed stubs the way gRPC produces them from `.proto`.

That is the strongest adoption objection against Trame for polyglot shops, and it is
addressable **without giving up the code-first principle**: the runtime discovery payload
(`GET /api/trame/discovery`) already carries the full contract as structured JSON — controllers,
methods, parameters, contract type schemas. A generator turns that payload into typed client
stubs in any language. The contract source stays the C# classes; generation is a *downstream
convenience*, not a second contract.

## The decision: one TypeScript core, all languages

The generator is written **once in TypeScript** and emits stubs for **TypeScript, JavaScript,
C#, and Python**. Rationale:

- The reusable logic already exists in TypeScript — the type-name tables and static binding
  analysis in `TrameDeveloperUi/src/lib/utils/{params,dependencyCheck}.ts`, the wire + discovery
  types and the fluent builder in `clients/ts/` (`trame-client`). A TS core reuses these directly;
  a C#-based generator would have to re-port them.
- The non-.NET clients are the gap; TS/JS are the highest-value emitters first. C# already has a
  real client (`Trame.Client`) — a generated typed C# surface is an ergonomics win, not a gap-closer.
- One core, pluggable emitters per target language, means a single type-mapping table and a
  single naming/casing policy serve all outputs. The DevUI's existing inline codegen becomes a
  thin wrapper over the core (de-duplicating ~4 divergent copies of the scalar type table).

The generator lives in a new package [`clients/codegen/`](clients/codegen/) (`trame-codegen`),
depends on `trame-client` (`file:../ts`) for the wire types, the fluent builder, and
`TrameRestClient.discover()`. It ships a CLI: `npx trame-gen`.

## What the generator produces (Increment 1: TS + JS)

A typed client with **dependency chaining as a first-class, compile-checked surface** — not just
fluent call snippets. Concretely, from Story 01's six controllers:

```ts
const client = new TrameClient("http://localhost:5001");

// Single call — typed params + typed return.
const order = await client.order.getById(42);          // Promise<TrameResponse<Order | null>>

// The diamond, with compile-time guarantees.
const batch = client.batch();                          // mode locked to Serial (@alias needs order)
const o = batch.add(
  client.order.getById(42)
    .exposes("$.customerId", "customerId")             // JsonPathOf<Order | null> literal union —
    .exposes("$.id",           "orderId")              // $.CustomerId (PascalCase) is a compile error
    .exposes("$.shippingAddressId", "addressId"),
);
const c      = batch.add(client.customer.getById(batch.alias("@customerId"))); // number
const lines  = batch.add(
  client.orderLine.getByOrder(batch.alias("@orderId")).exposes("$[*].articleId", "articleIds"),
);
const arts   = batch.add(client.article.getMany(batch.alias("@articleIds")));   // number[]
const stock  = batch.add(client.stock.getByArticles(batch.alias("@articleIds")));
const addr   = batch.add(client.address.getById(batch.alias("@addressId")));
const responses = await batch.send();                  // one roundtrip, six responses
```

`batch.alias("@x")` returns the **type the producer exposed** for that alias, so a consumer's
parameter typechecks against it. `JsonPathOf<T>` is a generated literal union of valid `$`-paths
for `T`, so a wrong-cased or nonexistent path is a compile error, not a runtime `400`. The raw
fluent builder (`TrameCall.batch([...])` from `trame-client`) remains available as an escape hatch.

The JS output mirrors the TS API shape with JSDoc `@typedef` blocks (IntelliSense without a
compile step); `--lang js` loses only the hard compile errors.

## Discovery as a stable spec (a prerequisite for independent implementers)

The discovery payload is the generator's input and — for anyone who wants to implement a Trame
client without this generator — the de-facto spec. It is not yet a *versioned, documented* contract.
A later phase formalizes it:

- A `discoveryVersion` field atop `DiscoveryInfo` (additive-only compatibility rule).
- A spec document (`docs/discovery-schema.md`) describing the `DiscoveryInfo` shape, the
  `List<T>` collection convention, and the known gaps.
- The generator's `assertDiscoveryShape` runtime guard today (no version field yet) refuses
  malformed input loudly — the interim mitigation.

This does not solve the bus-factor-one concern structurally, but it makes it *attackable*: with a
versioned spec + a working generator + cross-language samples, a second implementer can build a
client (or server) without reading Trame's source.

## Known gaps the schema carries (generator handles conservatively)

The discovery payload carries **no** enum members, nullability, default values, or an explicit
collection-kind flag (collections are encoded only as the string `List<T>`). The generator's
policies:

- **Nullability unknown** → every property emitted **optional** (`prop?: T`); the caller narrows.
  Conservative; the server may omit fields.
- **Enums opaque** (friendly name, no members) → emit `string` with a `// TODO: enum values not in
  discovery` marker. A future server-side extension to carry enum members closes this.
- **Collections** → unwrap `List<T>` / `T[]` / `IEnumerable<T>` to `T[]` (`Array<T>` in JSDoc).
  Other generics (`Dictionary<,>`, `HashSet`) → `unknown` + TODO (rare in practice; document).
- **No default values** → parameters are all required in the generated signature (except
  `CancellationToken`, which discovery already drops).

## Phased plan

- **Increment 1 (in progress)** — `clients/codegen` core (extracted + consolidated) + **TS** and
  **JS** emitters + `trame-gen` CLI + DevUI `CodegenPage` refactor (thin wrapper over the core) +
  golden-file + compile + live e2e tests against Story 01. Verified end-to-end: the Story-01
  diamond compiles and executes in one roundtrip via the generated typed client.
- **Increment 2** — **C#** and **Python** emitters (`csTypeOf` / `pyTypeOf` tables already shipped
  in the core in Increment 1). C#: nullable reference types, `[JsonPropertyName]` camelCase, a
  typed `TrameCall.Init(...).Exposes(...).WithAlias(...)` surface. Python: `dataclasses` + async
  over `httpx`, `py.typed` marker. CLI gains `--lang cs|py`; the DevUI C# tab switches to the core.
- **Discovery schema versioning** — `discoveryVersion` + `docs/discovery-schema.md`;
  `assertDiscoveryShape` → version check once a version exists.
- **.NET Roslyn source-generator** — the v1.1 endgame from `ROADMAP.md`: an incremental generator
  that emits C# stubs **into the compilation** at build time, from a checked-in discovery snapshot
  (offline) or a build-time fetch. The drift check (regenerate at build, fail if the committed
  snapshot differs) is the mandatory component — without it, this is exactly the `wsdl.exe` trap.
  This gives .NET the compile-time contract boundary that is gRPC's real edge. It reuses the
  C# emitter's logic (ported, or invoked as a TS subprocess during build).
- **CI publishing** — publish `trame-client` and `trame-codegen` to npm from
  `.github/workflows/` (today both are local `file:` dependencies).
- **Deferred cleanups** — unify `BatchCodegen`'s snippet renderer into `trame-codegen/snippets`;
  delete the legacy scalar-table copies in `EditorPane` / `ParamEditor` / `DependencyBuilderPage`;
  `--out-mode single` emitter option for one-file output.

## Relationship to the code-first default

Generation is **opt-in**. The default Trame model — runtime discovery, no code generation, the
untyped `TrameCall` builder — is unchanged and remains the recommendation for flexibility and
zero-tooling. The generator is the counterpart for teams that want a compile-time contract
boundary and typed cross-language clients. It does not add a second protocol: the generated stubs
build on `trame-client`'s `TrameCall` / `TrameRestClient` (TS) and `TrameCall` / `ITrameClient`
(.NET) — they are a typed wrapper over the existing wire, not a parallel one.
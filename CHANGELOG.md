# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Background

Contract-correctness pass before the first public GitHub release. Several v1.0.0 wire-facing claims did not match the implementation; this release reconciles the wire format, error semantics, and parameter binding with `PROTOCOL.md` so the public contract is consistent and stable.

### Fixed
- **Parameter binding (P0):** The fluent client API sent `ParameterName = "param0/param1"`, but the server bound strictly by the real C# parameter name — a mismatch silently fell back to defaults with `200 OK`. The server now binds each method parameter by name first, then falls back to the positional `num` index (counting non-`CancellationToken` parameters). `byte[]` parameters are never bound positionally (injected from `binaryData`).
- **Duplicate parameter name:** A duplicate `parameterName` no longer throws inside `ToDictionary`; it returns a `400 Bad Request`.
- **Error semantics (P0):**
  - Authorization failures now return `401 Unauthorized` (previously `405 Method Not Allowed`). `405` was dead/wrong and has been removed.
  - `TrameError.requestId` is now populated from the originating request `id` on every non-2xx response, so failures can be correlated even within batch calls.
  - `TrameError.details` (stack trace) is populated only when detailed errors are enabled (`EnableDetailedErrors` option or `IHostEnvironment.IsDevelopment()`); in production the error `message` is generic and does not leak exception internals. `TargetInvocationException` no longer leaks the inner exception message.
  - `void` / `Task`-without-result methods now return `204 No Content` with `data = null` (previously returned a localized success string as `data`).
- **REST transport (P0):** Cancelled requests (`OperationCanceledException`) now return `499` on both the Minimal-API and MVC endpoints (previously only the MVC controller). The HTTP envelope over REST is always `200 OK` with the `TrameResponse` (including any error) in the body — the logical `code` is a body field, not the HTTP status (JSON-RPC style).
- **Wire language (P0):** All wire-facing strings (`TrameError.message`, error `data`, transport problem titles) are now English. German is retained only for internal logs and comments, per the project convention.
- **`TrameCall.WithAlias`:** Removed the unused `fallbackValue` parameter. An unresolvable `@alias` is now a hard error (returns `400`), not a silent default.
- **Discovery: Signatur-Inferenz als Default (Weg C).** `[TrameDataContract]` ist nicht mehr erforderlich — die Discovery expandiert automatisch jeden Klassentyp, der in einer Methodensignatur auftaucht und dessen Assembly zum Set der Controller-Assemblies gehört (Property-Schema, Beispiel, Nested-Types). Typen aus anderen Assemblies (BCL, Trame-Framework-Envelope, Fremdlibs) bleiben opaque. `[TrameDataContract]` ist nun ein optionaler Override: bare = force-expand (z. B. für Fremdtypen, die man nicht besitzt), `Exclude = true` = force-opaque. Keine Auswirkung auf den RPC-Aufrufpfad — nur Discovery-Metadaten betreffen.

### Added
- Client: `TrameCall.Param(string parameterName, object? value)` named overload, so callers can bind by explicit server-side parameter name regardless of order.
- Support for dotted controller namespaces (e.g. `[TrameController("Customer.Address.Contact")]`) — verified and tested; no fixed two-level routing limit.
- New tests: positional vs named parameter binding (unit + integration), duplicate-name `400`, missing-parameter default, `void`→`204`, `401` on authorization failure, dotted-namespace routing.
- `[TrameDataContract(Exclude = true)]` — Opt-out-Override für die Discovery-Inferenz (force-opaque trotz Zugehörigkeit zur Controller-Assembly).
- New tests: Discovery-Inferenz (unmarkierter Own-Assembly-Typ wird expandiert; Framework-Envelope bleibt opaque; `Exclude` hält Own-Assembly-Typ opaque).

### Changed
- `TrameInvoker` exposes `EnableDetailedErrors`, wired from `TrameOptions.EnableDetailedErrors` or `IHostEnvironment.IsDevelopment()` during `AddTrame` registration.
- `PROTOCOL.md` rewritten: status codes are logical body codes (envelope-at-200); `405` removed; `401` for auth; `403` marked roadmap; `204` for void; transport-level `429`/`499`/`400` clarified; `TrameError.requestId` added to the TypeScript client type.

### Removed
- `MethodNotAllowed()` (405) and the unused `Forbidden()` factory from `TrameInvoker`.

## [1.0.0] - 2026-07-02

### Background

Trame was created to solve a fundamental mismatch: REST is designed for resource-oriented APIs (CRUD on nouns), but real-world applications often need to call *actions* (RPC-style). REST forces awkward patterns like `POST /api/customer/42/add-address`, N+1 roundtrips for dependent calls, and no way to batch multiple calls.

**Why not gRPC?** gRPC was not well-supported when Trame was created (no native browser support, immature .NET tooling), and its schema-first approach (`.proto` files + code generation) conflicts with .NET's natural code-first workflow. In .NET, your C# classes already define the contract — maintaining a separate IDL is unnecessary overhead.

Trame takes inspiration from GraphQL (dependency resolution, batch queries) and gRPC (method-oriented calling) but stays within the .NET ecosystem as a **code-first** framework: no `.proto` files, no code generation, just attributes on C# classes.

### Added
- Multi-transport RPC framework: REST, WebSocket, SignalR
- Batch-call support: parallel and serial execution of multiple RPC calls in one roundtrip
- Dependency-chaining: `@alias` placeholders resolved server-side via JsonPath
- Topological sort with cycle detection for dependency-aware batch execution
- Streaming support: `IAsyncEnumerable<T>` and `Task<IAsyncEnumerable<T>>`
- Interceptor pipeline: `ITrameInterceptor` with built-in `TrameLoggingInterceptor`
- Discovery/MEX: `GET /api/trame/discovery` returns full API metadata
- Developer UI: built-in web interface at `/developer`
- Rate-limiting: configurable fixed-window rate limiter
- Authorization: `[TrameAuthorise]` attribute with role-based access
- `TrameError` error model introduced (populated consistently and gated on `EnableDetailedErrors` in `[Unreleased]`)
- Expression-Tree invocation: pre-compiled method delegates for performance
- Fluent client API: `TrameCall.Init().With().Exposes().WithAlias().ToRequest()`
- `TrameDataContract`, `TrameDocumentation`, `TrameExample` attributes for API metadata
- gRPC comparison endpoint for benchmarks
- 67 unit and integration tests

### Performance
- Trame REST is 2.5x-16x faster than classic REST for single calls
- Trame Batch is 7x faster than REST parallel for 10 calls
- Trame WebSocket is 2.3x faster than REST for persistent small calls
- Trame Dependency-Chain replaces N+1 pattern: 1 roundtrip instead of 3
- Consistently 3-9x less memory allocation compared to REST

### Changed
- Consolidated all shared models (`TrameRequest`, `TrameResponse`, `TrameMultiRequest`, `TrameParameter`, `ExecutionMode`) into `TrameCommon`
- Unified exception handling with `TrameException` + `TrameError`
- Migrated from `Newtonsoft.Json` to `System.Text.Json` as sole JSON serializer
- `TrameCommon` target framework changed from `netstandard2.1` to `net8.0`
- `EnableDetailedErrors` option introduced (wiring to `IHostEnvironment.IsDevelopment()` and `TrameOptions` completed in `[Unreleased]`)

### Removed
- Removed `TrameGrpc` stub project (gRPC transport was not implemented)
- Removed `Newtonsoft.Json` dependency from `TrameRest`
- Removed duplicate model definitions from `TrameCore` and `TrameClient`
- Removed duplicate `TrameException` from `TrameClient`

### Fixed
- `TrameDiscoveryService`: `RegisterType()` was not called for non-generic `[TrameDataContract]` return types and parameters, causing `KeyNotFoundException`
- `IAsyncEnumerable<T>` interface detection: now checks `GetInterfaces()` for compiler-generated state machine types